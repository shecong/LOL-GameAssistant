using System.Net.WebSockets;
using System.Text;
using LOL_GameAssistant.Helper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LOL_GameApi.Tests;

public sealed class WebSocketLifecycleTests
{
    private static async Task<WebApplication> Server(Func<WebSocket, Task> handle)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/events", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            try { await handle(socket); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        });
        await app.StartAsync();
        return app;
    }

    private static string Address(WebApplication app) => app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single().Replace("http://", "ws://") + "/events";

    private static Task Send(WebSocket socket, string message) => socket.SendAsync(
        new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, default);

    [Fact]
    public async Task CloseCancelsPendingHandlerAndClientCanBeStartedAgain()
    {
        await using var app = await Server(async socket =>
        {
            await Send(socket, "first");
            await Send(socket, "later");
            await socket.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), default);
        });
        await using var client = new WebSocketClient(Address(app)) { ReconnectEnabled = false };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delivered = 0;
        client.OnMessageAsync += async (_, token) =>
        {
            Interlocked.Increment(ref delivered);
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(client.IsConnected);
        Assert.Equal(1, delivered);
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, delivered);
        await client.DisposeAsync();
        await client.CloseAsync(); // 重复关闭或释放不应重新触发重连或抛出 CTS 已释放异常。
    }

    [Fact]
    public async Task TransportReconnectsAndPreservesMessageOrderAcrossSessions()
    {
        int sessions = 0;
        var delivered = new List<string>();
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await Server(async socket =>
        {
            int session = Interlocked.Increment(ref sessions);
            if (session > 1)
                await socket.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), default); // 自动重新订阅。
            await Send(socket, session.ToString());
            if (session == 1)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect", default);
            await socket.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), default);
        });
        await using var client = new WebSocketClient(Address(app)) { ReconnectDelayMs = 1000 };
        client.OnMessageAsync += (message, _) =>
        {
            delivered.Add(message);
            if (message == "2") second.TrySetResult();
            return Task.CompletedTask;
        };
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "1", "2" }, delivered);
    }
}
