using System.Net.WebSockets;
using System.Text;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class WebSocketMessagePumpTests
{
    private sealed class Socket(params (byte[] Bytes, bool End, WebSocketMessageType Type)[] frames) : WebSocket
    {
        private int _index;
        public int Reads;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken token) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            Interlocked.Increment(ref Reads);
            if (_index >= frames.Length)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }
            var frame = frames[_index++];
            frame.Bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new(frame.Bytes.Length, frame.Type, frame.End);
        }
    }

    private static (byte[], bool, WebSocketMessageType) Text(string value, bool end = true) =>
        (Encoding.UTF8.GetBytes(value), end, WebSocketMessageType.Text);
    private static readonly (byte[], bool, WebSocketMessageType) Close = ([], true, WebSocketMessageType.Close);

    [Fact]
    public async Task SlowConsumerPreservesOrderAndAppliesBackpressure()
    {
        using var socket = new Socket(Text("ReadyCheck"), Text("ChampSelect"), Text("InProgress"), Close);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var running = WebSocketMessagePump.RunAsync(socket, async (value, token) =>
        {
            events.Add(value);
            if (value == "ReadyCheck") { entered.SetResult(); await release.Task.WaitAsync(token); }
        }, (_, _) => Task.CompletedTask, new(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, socket.Reads);
        Assert.Equal(new[] { "ReadyCheck" }, events);
        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "ReadyCheck", "ChampSelect", "InProgress" }, events);
    }

    [Fact]
    public async Task CancellationStopsBlockedConsumerAndNeverDeliversLaterEvents()
    {
        using var socket = new Socket(Text("old"), Text("later"), Close);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var running = WebSocketMessagePump.RunAsync(socket, async (value, token) =>
        {
            events.Add(value);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, (_, _) => Task.CompletedTask, new(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(new[] { "old" }, events);
        Assert.Equal(1, socket.Reads);
    }

    [Fact]
    public async Task FragmentedUtf8IsReassembledWithoutCorruption()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("选人");
        using var socket = new Socket((bytes[..2], false, WebSocketMessageType.Text), (bytes[2..], true, WebSocketMessageType.Text));
        Assert.Equal("选人", await WebSocketMessagePump.ReadAsync(socket, new(), default));
    }

    [Fact]
    public async Task MessageLimitIncludesAllFragments()
    {
        using var socket = new Socket(Text("123", false), Text("456"));
        await Assert.ThrowsAsync<InvalidDataException>(() => WebSocketMessagePump.ReadAsync(socket, new() { MaxMessageBytes = 5 }, default));
    }

    [Fact]
    public async Task UnfinishedFragmentTimesOut()
    {
        using var socket = new Socket(Text("partial", false));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WebSocketMessagePump.ReadAsync(socket,
            new() { FragmentTimeout = TimeSpan.FromMilliseconds(100) }, default).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task HeartbeatIsAcknowledgedBeforeDispatch()
    {
        using var socket = new Socket(Text("PING"), Close);
        var order = new List<string>();
        await WebSocketMessagePump.RunAsync(socket,
            (message, _) => { order.Add(message); return Task.CompletedTask; },
            (message, _) => { order.Add(message); return Task.CompletedTask; }, new(), default);
        Assert.Equal(new[] { "[8,\"PONG\"]", "PING" }, order);
    }
}
