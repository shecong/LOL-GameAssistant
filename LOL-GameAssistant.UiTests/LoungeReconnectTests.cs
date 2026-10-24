using System.Reflection;
using LOL.Lounge;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LoungeReconnectTests
{
    [Theory]
    [InlineData(41)]
    [InlineData(100)]
    [InlineData(201)]
    public async Task ReconnectReplaysAllPagesAndDoesNotDoubleCountLiveBroadcasts(int missed)
    {
        using var client = CreateClient();
        var rows = Rows(50);
        var cursors = new List<long>();
        Task<LoungeSnapshot> Read(long after, CancellationToken token)
        {
            cursors.Add(after);
            return Task.FromResult(new LoungeSnapshot((after == 0 ? rows.TakeLast(40) : rows.Where(m => m.Id > after).Take(200)).ToArray(), []));
        }
        await client.SynchronizeAsync(Read, default);
        Assert.Equal(40, client.Messages.Length);
        Assert.Equal(0, client.UnreadCount);
        rows = Rows(50 + missed);
        typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [rows[^1], true]);
        await client.SynchronizeAsync(Read, default);
        Assert.Equal(50, cursors[1]);
        Assert.Equal(missed, client.UnreadCount);
        Assert.Equal(Enumerable.Range(11, 40 + missed).Select(i => (long)i), client.Messages.Select(m => m.Id));
        await client.SynchronizeAsync(Read, default);
        Assert.Equal(50 + missed, cursors[^1]);
        Assert.Equal(missed, client.UnreadCount);
    }

    [Fact]
    public async Task EmptyInitialSyncUsesReplayCursorAndInterruptedCatchupResumesLastCompletedPage()
    {
        using var client = CreateClient();
        await client.SynchronizeAsync((_, _) => Task.FromResult(new LoungeSnapshot([], [])), default);
        var rows = Rows(241);
        var cursors = new List<long>();
        bool fail = true;
        Task<LoungeSnapshot> Read(long after, CancellationToken token)
        {
            cursors.Add(after);
            if (after == 200 && fail) throw new IOException("connection lost during replay");
            return Task.FromResult(new LoungeSnapshot(rows.Where(m => m.Id > after).Take(200).ToArray(), []));
        }
        await Assert.ThrowsAsync<IOException>(() => client.SynchronizeAsync(Read, default));
        Assert.Equal(new long[] { LoungeRules.BeforeFirstMessage, 200 }, cursors);
        fail = false;
        await client.SynchronizeAsync(Read, default);
        Assert.Equal(200, cursors[^1]);
        Assert.Equal(241, client.Messages.Length);
        Assert.Equal(241, client.UnreadCount);
    }

    private static LoungeClient CreateClient()
    {
        var client = new LoungeClient(new LoungeLcuReader(new LcuHttpRequestSender()));
        typeof(LoungeClient).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, new LoungeSettings());
        return client;
    }

    private static LoungeMessage[] Rows(int count) => Enumerable.Range(1, count)
        .Select(i => new LoungeMessage(i, "", "other", "player", "region", "chat", "message", DateTimeOffset.UtcNow)).ToArray();
}
