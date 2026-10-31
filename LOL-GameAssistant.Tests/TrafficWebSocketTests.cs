using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class TrafficWebSocketTests
{
    [Fact]
    public async Task DisabledSocketNeverConnects()
    {
        var traffic = new NetworkTrafficService();
        traffic.SetEnabled(NetworkModule.Lounge, false);
        using var socket = new TrafficWebSocket(NetworkModule.Lounge, new ClientWebSocket(), traffic);
        await Assert.ThrowsAsync<HttpRequestException>(() => socket.ConnectAsync(new Uri("ws://127.0.0.1:1/"), default));
        Assert.Equal(WebSocketState.None, socket.State);
        Assert.Equal(1, traffic.Snapshot().Single(s => s.Info.Module == NetworkModule.Lounge).Blocked);
    }

    [Fact]
    public async Task ConnectedSocketCountsPayloadAndDisablingAbortsAnIdleReceive()
    {
        var traffic = new NetworkTrafficService();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = AcceptAsync(listener, deadline.Token);
        using var socket = new TrafficWebSocket(NetworkModule.Lounge, new ClientWebSocket(), traffic);
        using var connectOnly = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), connectOnly.Token);
        var peer = await accept;
        using var peerClient = peer.Client;
        using var server = peer.Socket;
        // A connection deadline must not later abort an established connection.
        connectOnly.Cancel();
        Assert.Equal(WebSocketState.Open, socket.State);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("你好")), WebSocketMessageType.Text, true, deadline.Token);
        byte[] buffer = new byte[64];
        Assert.Equal(6, (await server.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token)).Count);
        await server.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hello")), WebSocketMessageType.Text, true, deadline.Token);
        Assert.Equal(5, (await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token)).Count);
        var stats = traffic.Snapshot().Single(s => s.Info.Module == NetworkModule.Lounge);
        Assert.Equal(6, stats.Uploaded);
        Assert.Equal(5, stats.Downloaded);
        Assert.Equal(1, stats.Requests);
        var idle = socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
        traffic.SetEnabled(NetworkModule.Lounge, false);
        var failure = await Record.ExceptionAsync(() => idle.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(failure is WebSocketException or OperationCanceledException, failure?.ToString());
        Assert.Equal(WebSocketState.Aborted, socket.State);
        traffic.SetEnabled(NetworkModule.Lounge, true);
        Assert.True(traffic.IsEnabled(NetworkModule.Lounge));
    }

    private static async Task<(TcpClient Client, WebSocket Socket)> AcceptAsync(TcpListener listener, CancellationToken token)
    {
        var client = await listener.AcceptTcpClientAsync(token);
        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string key = "";
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)) key = line.Split(':', 2)[1].Trim();
            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token);
            return (client, WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan));
        }
        catch { client.Dispose(); throw; }
    }
}
