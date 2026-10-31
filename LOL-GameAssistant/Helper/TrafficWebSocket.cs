using System.Net.WebSockets;

namespace LOL_GameAssistant.Helper;

/// <summary>Accounts for WebSocket payload and aborts established connections when access is disabled.</summary>
internal sealed class TrafficWebSocket(NetworkModule module, ClientWebSocket inner, NetworkTrafficService? traffic = null) : WebSocket
{
    private readonly NetworkTrafficService _traffic = traffic ?? NetworkTrafficService.Shared;
    private NetworkTrafficService.Lease? _lease;
    private CancellationTokenRegistration _abort;
    internal async Task ConnectAsync(Uri uri, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _lease = _traffic.Begin(module, CancellationToken.None);
        _abort = _lease.Token.Register(inner.Abort);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(token, _lease.Token);
        try { await inner.ConnectAsync(uri, connect.Token).ConfigureAwait(false); }
        catch { Dispose(); throw; }
    }
    public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
    public override string? CloseStatusDescription => inner.CloseStatusDescription;
    public override WebSocketState State => inner.State;
    public override string? SubProtocol => inner.SubProtocol;
    public override void Abort() => inner.Abort();
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => inner.CloseAsync(closeStatus, statusDescription, cancellationToken);
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => inner.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var result = await inner.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        _traffic.AddBytes(module, result.Count, false);
        return result;
    }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        await inner.SendAsync(buffer, messageType, endOfMessage, cancellationToken).ConfigureAwait(false);
        _traffic.AddBytes(module, buffer.Count, true);
    }
    public override void Dispose() { _abort.Dispose(); inner.Dispose(); _lease?.Dispose(); }
}
