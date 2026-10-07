using System.Net.WebSockets;
using System.Text;

namespace LOL_GameAssistant.Helper;

/// <summary>最多持有一条有限大小消息；上一事件处理完成后才读取下一条。</summary>
internal static class WebSocketMessagePump
{
    internal static async Task RunAsync(WebSocket socket, Func<string, CancellationToken, Task> dispatch,
        Func<string, CancellationToken, Task> send, WebSocketClientOptions options, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? message = await ReadAsync(socket, options, cancellationToken).ConfigureAwait(false);
            if (message == null) return;
            if (message is "[8,\"PING\"]" or "PING")
                await send("[8,\"PONG\"]", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await dispatch(message, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task<string?> ReadAsync(WebSocket socket, WebSocketClientOptions options,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(4096, options.MaxMessageBytes)];
        using var message = new MemoryStream();
        using var fragments = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool first = true;
        while (true)
        {
            var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), fragments.Token).ConfigureAwait(false);
            if (frame.MessageType == WebSocketMessageType.Close) return null;
            if (frame.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("LCU WebSocket only accepts text messages.");
            if (message.Length + frame.Count > options.MaxMessageBytes)
                throw new InvalidDataException("WebSocket message exceeds the size limit.");
            message.Write(buffer, 0, frame.Count);
            if (frame.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            if (first)
            {
                // 空闲连接不超时；收到首个分片后，整条消息必须在固定时间内完成。
                fragments.CancelAfter(options.FragmentTimeout);
                first = false;
            }
        }
    }
}
