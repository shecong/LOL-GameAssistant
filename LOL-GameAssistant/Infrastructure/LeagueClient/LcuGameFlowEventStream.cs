using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Helper;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 基于本机 LCU WebSocket 的事件流实现。
/// 认证、协议报文和 JSON 解码都留在基础设施层，避免 WinForms 了解 LCU 传输细节。
/// </summary>
public sealed class LcuGameFlowEventStream : ILeagueClientEventStream
{
    private const string JsonApiSubscription = "[5, \"OnJsonApiEvent\"]";
    private readonly LeagueClientConnection _connection;
    private WebSocketClient? _client;
    private bool _disposed;

    public LcuGameFlowEventStream(LeagueClientConnection connection) => _connection = connection;

    public event Action<LeagueClientEvent>? EventReceived;

    public event Action<string>? ErrorOccurred;

    public event Action<bool>? ConnectionChanged;

    public event Action<string>? Reconnecting;

    public async Task<bool> ConnectAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        LeagueClientCredentials? credentials;
        try
        {
            credentials = _connection.TryGetCredentials(forceRefresh);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"LCU 认证读取失败：{ex.Message}");
            return false;
        }

        if (credentials == null) return false;

        await DisconnectAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var client = new WebSocketClient(
            $"wss://127.0.0.1:{credentials.Port}",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{credentials.Token}")));
        client.OnMessage += HandleRawMessage;
        client.OnError += error => ErrorOccurred?.Invoke(error.Message);
        client.OnConnectChanged += connected => ConnectionChanged?.Invoke(connected);
        client.OnReconnecting += message => Reconnecting?.Invoke(message);
        _client = client;

        await client.ConnectAsync().ConfigureAwait(false);
        return client.IsConnected;
    }

    public Task SubscribeToJsonApiEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client?.SendAsync(JsonApiSubscription) ?? Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        WebSocketClient? client = _client;
        _client = null;
        if (client == null) return;

        client.OnMessage -= HandleRawMessage;
        try
        {
            await client.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>将 LCU 数组报文转换为稳定的领域事件。</summary>
    private void HandleRawMessage(string message)
    {
        // LCU WebSocket 除业务数组外，还可能传来空帧、心跳或其他协议控制文本。
        // 只有数组报文才可能是 [8, "OnJsonApiEvent", ...]，其余内容无需进入 JSON 解析器。
        string payloadText = message.TrimStart();
        if (payloadText.Length == 0 || payloadText[0] != '[') return;

        JsonArray? payload;
        try
        {
            payload = JsonNode.Parse(payloadText)?.AsArray();
        }
        catch (JsonException)
        {
            // LCU 偶发传入不完整的协议帧；它不是业务事件，等待下一帧即可。
            return;
        }

        if (payload == null || payload.Count < 3) return;
        if (payload[1] is not JsonValue eventNameValue ||
            !eventNameValue.TryGetValue<string>(out string? eventName) ||
            !string.Equals(eventName, "OnJsonApiEvent", StringComparison.Ordinal)) return;
        if (payload[2] is not JsonObject eventBody ||
            !eventBody.TryGetPropertyValue("uri", out JsonNode? uriNode) ||
            uriNode is not JsonValue uriValue ||
            !uriValue.TryGetValue<string>(out string? uri) ||
            string.IsNullOrWhiteSpace(uri) ||
            !eventBody.TryGetPropertyValue("data", out JsonNode? data) ||
            data == null) return;

        EventReceived?.Invoke(new LeagueClientEvent(uri, GetEventDataText(data)));
    }

    /// <summary>字符串事件保留原文本，复合事件保留 JSON 文本供后续应用用例扩展。</summary>
    private static string GetEventDataText(JsonNode data) =>
        data is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text
            : data.ToJsonString();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LcuGameFlowEventStream));
    }
}