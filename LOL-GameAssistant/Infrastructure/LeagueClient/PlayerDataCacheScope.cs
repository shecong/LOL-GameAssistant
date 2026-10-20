using System.Text.Json;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>持久化数据按真实大区隔离；无法确认大区时仅在当前客户端会话内复用。</summary>
internal sealed class PlayerDataCacheScope(ILcuRequestSender lcu)
{
    private readonly object _sync = new();
    private string? _session;
    private Task<string?>? _pending;

    internal async Task<string> GetAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string session = PlayerHistorySession.Key;
        Task<string?> request;
        lock (_sync)
        {
            if (_session != session || _pending == null || _pending.IsFaulted || _pending.IsCanceled ||
                (_pending.IsCompletedSuccessfully && _pending.Result == null))
            {
                _session = session;
                _pending = ReadPlatformAsync();
            }
            request = _pending;
        }
        string? platform = await request.WaitAsync(token).ConfigureAwait(false);
        return platform == null ? "sgp-v1:session:" + session : "sgp-v1:platform:" + platform;
    }

    private async Task<string?> ReadPlatformAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            string? raw = await lcu.GetStringAsync("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", deadline.Token).ConfigureAwait(false);
            string? platform = ReadString(raw);
            if (string.IsNullOrWhiteSpace(platform))
            {
                string? me = await lcu.GetStringAsync("/lol-chat/v1/me", deadline.Token).ConfigureAwait(false);
                platform = ReadProperty(me, "platformId");
            }
            return string.IsNullOrWhiteSpace(platform) ? null : platform.Trim().ToUpperInvariant();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
        { return null; }
    }

    private static string? ReadString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        using var json = JsonDocument.Parse(raw);
        return json.RootElement.ValueKind == JsonValueKind.String ? json.RootElement.GetString() : null;
    }
    private static string? ReadProperty(string? raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        using var json = JsonDocument.Parse(raw);
        return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
