using LOL.Lounge;
using LOL_GameApi.Lounge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LOL_GameApi.Tests;

public sealed class LoungeReliabilityTests
{
    [Fact]
    public void ExpiryAbortsOutsideLockAndKeepsQuotaUntilDisconnectEvenIfOneAbortFails()
    {
        var clock = new ManualClock();
        using var services = new ServiceCollection().AddOptions<LoungeOptions>()
            .Configure(o => { o.MaxConnections = 2; o.MaxConnectionsPerIp = 2; }).Services.BuildServiceProvider();
        var room = new LoungeRoom(services.GetRequiredService<IOptionsMonitor<LoungeOptions>>(), clock);
        int attempts = 0;
        room.Connect("broken", "a", "ip", () => { attempts++; throw new IOException("abort failed"); });
        room.Connect("closing", "b", "ip", () =>
        {
            // A different thread can enter the room while Abort runs.
            Assert.True(Task.Run(() => room.Members()).Wait(TimeSpan.FromSeconds(2)));
            attempts++;
        });
        clock.Advance(TimeSpan.FromSeconds(46));
        Assert.Equal(2, room.StaleConnections().Length);
        Assert.Equal(2, attempts);
        Assert.Throws<HubException>(() => room.Connect("new", "c", "ip", () => { }));
        Assert.Throws<HubException>(() => room.Update("closing", new("p", "name", "HN1", "region", "online")));
        room.Remove("closing");
        room.Connect("new", "c", "ip", () => { });
        Assert.Single(room.StaleConnections()); // Retry the failed abort, leave the fresh connection alone.
        Assert.Equal(3, attempts);
        Assert.Single(room.Members());
    }

    [Fact]
    public async Task StaleSignalRTransportActuallyClosesAndReplacementCanReplayEmptyChannel()
    {
        using var folder = new Folder();
        var clock = new ManualClock();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lounge:DatabasePath"] = Path.Combine(folder.Path, "hub.db"),
            ["Lounge:MaxConnections"] = "1"
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddLounge();
        builder.Services.AddRateLimiter(o => o.AddPolicy("lounge-connect", _ =>
            System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapLounge();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var client = Connect(address);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await client.StartAsync();
        Assert.Empty((await client.InvokeAsync<LoungeSnapshot>("Sync", 0L)).Messages);
        clock.Advance(TimeSpan.FromSeconds(40));
        await client.InvokeAsync("UpdatePresence", new LoungePresence("p", "name", "HN1", "region", "online"));
        clock.Advance(TimeSpan.FromSeconds(10));
        var room = app.Services.GetRequiredService<LoungeRoom>();
        Assert.Empty(room.StaleConnections());
        clock.Advance(TimeSpan.FromSeconds(36));
        Assert.Single(room.StaleConnections());
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HubConnectionState.Disconnected, client.State);

        var store = app.Services.GetRequiredService<LoungeStore>();
        for (int i = 1; i <= 241; i++)
            await store.AppendAsync("m" + i, "", "other", "name", "region", "chat", "message");
        await using var replacement = Connect(address);
        await replacement.StartAsync();
        var page = await replacement.InvokeAsync<LoungeSnapshot>("Sync", LoungeRules.BeforeFirstMessage);
        Assert.Equal(200, page.Messages.Length);
        Assert.Equal(1, page.Messages[0].Id);
        var remaining = await replacement.InvokeAsync<LoungeSnapshot>("Sync", page.Messages[^1].Id);
        Assert.Equal(41, remaining.Messages.Length);
        Assert.Equal(241, remaining.Messages[^1].Id);
        await replacement.StopAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task PruningRunsAtMostOncePerMinuteAndRetriesFailedCleanup()
    {
        using var folder = new Folder();
        var clock = new ManualClock();
        string path = Path.Combine(folder.Path, "prune.db");
        using var store = new LoungeStore(path, clock: clock);
        await store.AppendAsync("old", "", "sender", "name", "region", "chat", "old");
        using var db = new SqliteConnection("Data Source=" + path);
        await db.OpenAsync();
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE messages SET sentAt='2000-01-01T00:00:00.0000000+00:00'";
        await command.ExecuteNonQueryAsync();
        await store.AppendAsync("fresh", "", "sender", "name", "region", "chat", "fresh");
        Assert.Equal(2, (await store.ReadAsync()).Length);
        Assert.Equal(2, (await store.ReadBeforeAsync(3)).Messages.Length);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(2, (await store.ReadAsync()).Length);
        command.CommandText = "CREATE TRIGGER fail_prune BEFORE DELETE ON messages BEGIN SELECT RAISE(ABORT, 'test cleanup failure'); END";
        await command.ExecuteNonQueryAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<SqliteException>(() => store.ReadAsync());
        command.CommandText = "DROP TRIGGER fail_prune";
        await command.ExecuteNonQueryAsync();
        Assert.Equal("fresh", Assert.Single(await store.ReadAsync()).Text);
    }

    private static HubConnection Connect(string address)
    {
        string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        return new HubConnectionBuilder().WithUrl(address + "/hubs/lounge",
            o => o.AccessTokenProvider = () => Task.FromResult<string?>(token)).Build();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lounge-reliability-" + Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(Path, true); }
    }
}
