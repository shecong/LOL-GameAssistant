using LOL.Lounge;
using LOL_GameApi.Lounge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace LOL_GameApi.Tests;

public sealed class LoungeTests
{
    [Fact]
    public async Task OnlineNoticesAreNotStoredAndLegacyRowsDoNotConsumeHistoryPages()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "legacy.db");
        using var store = new LoungeStore(path);
        Assert.Null(await store.AppendAsync("online:new", "", "sender", "玩家", "艾欧尼亚", "online", "加入休息室"));
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            await db.OpenAsync();
            using var command = db.CreateCommand();
            // Simulate rows written by the previous server version, interleaved with chat.
            command.CommandText = """
                INSERT INTO messages(dedup,clientId,senderId,name,region,kind,text,sentAt)
                VALUES($key,'','sender','玩家','艾欧尼亚',$kind,'内容',$time)
                """;
            command.Parameters.AddWithValue("$key", "");
            command.Parameters.AddWithValue("$kind", "");
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            for (int i = 1; i <= 100; i++)
            {
                command.Parameters["$key"].Value = "legacy:" + i;
                command.Parameters["$kind"].Value = i % 2 == 0 ? "online" : "chat";
                await command.ExecuteNonQueryAsync();
            }
        }
        var latest = await store.ReadAsync();
        Assert.Equal(40, latest.Length);
        Assert.All(latest, m => Assert.Equal("chat", m.Kind));
        Assert.Equal(21, latest[0].Id);
        Assert.Equal(50, (await store.ReadAsync(1)).Length + 1);
        var earlier = await store.ReadBeforeAsync(latest[0].Id);
        Assert.Equal(10, earlier.Messages.Length);
        Assert.All(earlier.Messages, m => Assert.Equal("chat", m.Kind));
        Assert.False(earlier.HasMore);
    }

    [Fact]
    public async Task HistoryStartsWithLatestPageAndPagesBackwardWithExclusiveStableCursor()
    {
        using var folder = new TemporaryFolder();
        using var store = new LoungeStore(Path.Combine(folder.Path, "paging.db"));
        for (int i = 1; i <= 95; i++) await store.AppendAsync("message:" + i, "", "sender", "玩家", "艾欧尼亚", "chat", "消息 " + i);
        var latest = await store.ReadAsync();
        Assert.Equal(40, latest.Length); Assert.Equal(56, latest[0].Id); Assert.Equal(95, latest[^1].Id);
        // A new live message cannot shift an earlier-page cursor.
        await store.AppendAsync("message:96", "", "sender", "玩家", "艾欧尼亚", "chat", "实时消息");
        var older = await store.ReadBeforeAsync(latest[0].Id);
        Assert.Equal(Enumerable.Range(16, 40).Select(x => (long)x), older.Messages.Select(x => x.Id)); Assert.True(older.HasMore);
        await store.DeleteAsync(5);
        var first = await store.ReadBeforeAsync(older.Messages[0].Id);
        Assert.Equal(15, first.Messages.Length); Assert.False(first.HasMore); Assert.Contains(first.Messages, m => m.Id == 5 && m.Kind == "deleted");
        Assert.Empty((await store.ReadBeforeAsync(1)).Messages);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ReadBeforeAsync(0));
    }

    [Fact]
    public async Task StoreKeepsMessageOrderRetryIdsAndVictoryDedupAcrossRestart()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "room.db");
        long firstId;
        using (var store = new LoungeStore(path))
        {
            var first = await store.AppendAsync("chat:a:id", "id", "a", "Alice", "艾欧尼亚", "chat", "hello");
            firstId = first!.Id;
            var retry = await store.AppendAsync("chat:a:id", "id", "a", "Alice", "艾欧尼亚", "chat", "changed");
            Assert.Equal(first, retry);
            await store.AppendAsync("win:player:1", "", "a", "Alice", "艾欧尼亚", "victory", "win");
        }
        using (var store = new LoungeStore(path))
        {
            Assert.Null(await store.AppendAsync("win:player:1", "", "a", "Alice", "艾欧尼亚", "victory", "win"));
            var all = await store.ReadAsync();
            Assert.Equal(2, all.Length);
            Assert.Equal(firstId, all[0].Id);
            Assert.Single(await store.ReadAsync(firstId));
            await store.DeleteAsync(firstId);
            Assert.Equal("deleted", (await store.ReadAsync())[0].Kind);
            Assert.Equal(new[] { firstId }, await store.ReadDeletedIdsAsync());
        }
    }

    [Fact]
    public async Task PublicHubAuthenticatesBroadcastsMergesPresenceAndRejectsInvalidReports()
    {
        using var folder = new TemporaryFolder();
        string aliceToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string bobToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string bobSession = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bobToken)));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lounge:DatabasePath"] = Path.Combine(folder.Path, "integration.db"),
            ["Lounge:MutedSessions:0"] = bobSession
        });
        builder.AddLounge();
        builder.Services.AddRateLimiter(o => o.AddPolicy("lounge-connect", _ =>
            System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapLounge();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var anonymous = Connect(address, "");
        await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync());
        await using var alice = Connect(address, aliceToken);
        await using var bob = Connect(address, bobToken);
        await alice.StartAsync();
        await bob.StartAsync();
        var online = new TaskCompletionSource<LoungeOnlineNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        int onlineCount = 0;
        bob.On<LoungeOnlineNotice>("Online", notice => { Interlocked.Increment(ref onlineCount); online.TrySetResult(notice); });
        var player = new LoungePresence("puuid", "Alice#123", "HN1", "艾欧尼亚", "游戏中", Rank: "单双排 黄金II 50点");
        await alice.InvokeAsync("UpdatePresence", player);
        var notice = await online.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(player.Name, notice.Name);
        Assert.Equal(player.Region, notice.Region);
        Assert.Equal(player.Rank, notice.Rank);
        await bob.InvokeAsync("UpdatePresence", player); // Same account, two installations.
        Assert.Single((await alice.InvokeAsync<LoungeSnapshot>("Sync", 0L)).Members);
        await alice.InvokeAsync("UpdatePresence", player);
        var joined = await alice.InvokeAsync<LoungeSnapshot>("Sync", 0L);
        Assert.Equal(LoungeRules.ProtocolVersion, joined.ProtocolVersion);
        Assert.Equal(player.Rank, joined.Members.Single().Rank);
        Assert.Empty(joined.Messages);
        Assert.Equal(1, onlineCount);

        var received = new TaskCompletionSource<LoungeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        bob.On<LoungeMessage>("Message", m => { if (m.Kind == "chat") received.TrySetResult(m); });
        var sent = await alice.InvokeAsync<LoungeMessage>("SendMessage", new LoungeSend(Guid.NewGuid().ToString("N"), "hello"));
        Assert.Equal(sent, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync<LoungeMessage>("SendMessage", new LoungeSend(Guid.NewGuid().ToString("N"), "flood")));
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync<LoungeMessage>("SendMessage", new LoungeSend(Guid.NewGuid().ToString("N"), "muted")));

        var victory = new LoungeVictory(123, "HN1", "puuid", 450, DateTimeOffset.UtcNow, ["Bob#456", "Bob#456", "Alice#123"]);
        await alice.InvokeAsync("ReportVictory", victory);
        await alice.InvokeAsync("ReportVictory", victory);
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("ReportVictory", victory with { Puuid = "someone-else" }));
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("ReportVictory", victory with { GameId = 124, FinishedAt = DateTimeOffset.UtcNow.AddHours(-1) }));
        var snapshot = await bob.InvokeAsync<LoungeSnapshot>("Sync", 0L);
        Assert.Equal(2, snapshot.Messages.Length);
        Assert.Contains("极地大乱斗", snapshot.Messages.Single(m => m.Kind == "victory").Text);
        Assert.Contains("与【Bob#456】组队开黑", snapshot.Messages.Single(m => m.Kind == "victory").Text);
        Assert.Single((await bob.InvokeAsync<LoungeSnapshot>("Sync", sent.Id)).Messages);
        var earlier = await bob.InvokeAsync<LoungeHistoryPage>("History", sent.Id);
        Assert.All(earlier.Messages, m => Assert.True(m.Id < sent.Id));
        Assert.Empty(earlier.Messages);
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync<LoungeHistoryPage>("History", 0L));

        await alice.StopAsync();
        Assert.Single((await bob.InvokeAsync<LoungeSnapshot>("Sync", 0L)).Members);
        await alice.StartAsync();
        await alice.InvokeAsync("UpdatePresence", player);
        Assert.DoesNotContain((await alice.InvokeAsync<LoungeSnapshot>("Sync", 0L)).Messages, m => m.Kind == "online");
        Assert.Single((await alice.InvokeAsync<LoungeSnapshot>("Sync", sent.Id)).Messages);
        await bob.InvokeAsync("UpdatePresence", new LoungePresence("", "未登录玩家", "", "未知大区", "未登录 LOL"));
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync("ReportVictory", victory));
        await app.StopAsync();
    }

    [Fact]
    public void PresencePrivacyAndVictoryFreshnessHaveExplicitBoundaries()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(LoungeRules.IsRecent(now.AddMinutes(-9), now));
        Assert.False(LoungeRules.IsRecent(now.AddMinutes(-11), now));
        Assert.False(LoungeRules.IsRecent(now.AddMinutes(2), now));
        Assert.False(LoungeRules.Modes.ContainsKey(0));
        Assert.False(LoungeRules.Modes.ContainsKey(1700)); // Arena placements need separate semantics.
        Assert.NotEqual(LoungeRules.PlayerId(new("p", "n", "HN1", "", "")),
            LoungeRules.PlayerId(new("p", "n", "HN2", "", "")));
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("data:image/png;base64,abcd")]
    [InlineData("../local.png")]
    [InlineData("unknown")]
    public void StickerAllowlistRejectsArbitraryImages(string image) =>
        Assert.Throws<ArgumentException>(() => LoungeContent.ValidateSend(new(Guid.NewGuid().ToString("N"), image, "sticker")));

    [Fact]
    public void StructuredPostsValidateAndRemainReadableAfterPersistence()
    {
        string id = Guid.NewGuid().ToString("N");
        foreach (var sticker in LoungeContent.Stickers)
            Assert.Equal(("sticker", sticker.Id), LoungeContent.ValidateSend(new(id, sticker.Id, "sticker")));
        Assert.Throws<ArgumentException>(() => LoungeContent.ValidateSend(new(id, "image bytes", "image")));
        Assert.Throws<ArgumentException>(() => LoungeContent.ValidateSend(new(id, "hi", Mentions: ["bad-id"])));
        var recruit = new LoungeRecruitment("艾欧尼亚", 420, "翡翠", "辅助", 1, "一起上分");
        var card = LoungeContent.ValidateSend(new(id, "", "recruit", Recruitment: recruit));
        var message = new LoungeMessage(1, id, "session", "Alice", "艾欧尼亚", card.Kind, card.Text, DateTimeOffset.UtcNow);
        Assert.Contains("缺1人", LoungeContent.Display(message));
        Assert.Contains("辅助", LoungeContent.Display(message));
        Assert.Throws<ArgumentException>(() => LoungeContent.ValidateSend(new(id, "", "recruit", Recruitment: recruit with { Needed = 5 })));
        Assert.Throws<ArgumentException>(() => LoungeContent.ValidateSend(new(id, "", "recruit", Recruitment: recruit with { QueueId = 0 })));
        var mention = LoungeContent.ValidateSend(new(id, "@Bob 来一把", Mentions: [new string('A', 64)]));
        var mentioned = message with { Kind = mention.Kind, Text = mention.Text };
        Assert.Equal(new[] { new string('A', 64) }, LoungeContent.Mentions(mentioned));
        Assert.Equal("@Bob 来一把", LoungeContent.Display(mentioned));
    }

    [Fact]
    public async Task HighlightDedupAndPresetMessagesSurviveStoreRestart()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "features.db");
        using (var store = new LoungeStore(path))
        {
            await store.AppendAsync("h", "", "a", "Alice", "艾欧尼亚", "highlight", "五杀");
            await store.AppendAsync("s", "id", "a", "Alice", "艾欧尼亚", "sticker", "nice");
        }
        using (var store = new LoungeStore(path))
        {
            Assert.Null(await store.AppendAsync("h", "", "a", "Alice", "艾欧尼亚", "highlight", "五杀"));
            Assert.Equal("nice", (await store.ReadAsync()).Single(m => m.Kind == "sticker").Text);
        }
    }

    [Fact]
    public async Task PublicHubBroadcastsRecruitmentMentionsAndOnlyPresetStickersAndDeduplicatesHighlights()
    {
        using var folder = new TemporaryFolder();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Lounge:DatabasePath"] = Path.Combine(folder.Path, "features-hub.db") });
        builder.AddLounge();
        builder.Services.AddRateLimiter(o => o.AddPolicy("lounge-connect", _ =>
            System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapLounge();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var alice = Connect(address, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await using var bob = Connect(address, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await using var carol = Connect(address, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await using var invalid = Connect(address, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        await alice.StartAsync(); await bob.StartAsync(); await carol.StartAsync(); await invalid.StartAsync();
        var player = new LoungePresence("alice", "Alice#123", "HN1", "艾欧尼亚", "在线");
        var bobPlayer = player with { Puuid = "bob", Name = "Bob#456" };
        await alice.InvokeAsync("UpdatePresence", player);
        await bob.InvokeAsync("UpdatePresence", bobPlayer);
        await carol.InvokeAsync("UpdatePresence", player with { Puuid = "carol", Name = "Carol#789" });
        await invalid.InvokeAsync("UpdatePresence", player with { Puuid = "invalid", Name = "Invalid" });

        var broadcast = new TaskCompletionSource<LoungeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.On<LoungeMessage>("Message", m => { if (m.Kind == "sticker") broadcast.TrySetResult(m); });
        var recruit = new LoungeRecruitment("艾欧尼亚", 420, "不限段位", "辅助", 1, "友好上分");
        var recruitmentBroadcast = new TaskCompletionSource<LoungeRecruitmentState>(TaskCreationOptions.RunContinuationsAsynchronously);
        bob.On<LoungeRecruitmentState>("Recruitment", s => recruitmentBroadcast.TrySetResult(s));
        var card = await alice.InvokeAsync<LoungeMessage>("PublishRecruitment", new LoungeSend(Guid.NewGuid().ToString("N"), "", "recruit", Recruitment: recruit));
        Assert.Equal(recruit, LoungeContent.Parse<LoungeRecruitment>(card.Text));
        Assert.Equal(card.Id, (await recruitmentBroadcast.Task.WaitAsync(TimeSpan.FromSeconds(5))).MessageId);
        var initialRecruitment = Assert.Single((await bob.InvokeAsync<LoungeSnapshot>("Sync", 0L)).Recruitments!);
        Assert.Equal(LoungeRules.PlayerId(player), initialRecruitment.OwnerId);
        var joined = await bob.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "join");
        Assert.Equal("已满员", joined.Status);
        Assert.Equal(LoungeRules.PlayerId(bobPlayer), Assert.Single(joined.Participants).Id);
        Assert.Equal(joined, await bob.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "join"), RecruitmentStateComparer.Instance);
        await Assert.ThrowsAsync<HubException>(() => carol.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "join"));
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "close"));
        await bob.StopAsync(); await bob.StartAsync(); await bob.InvokeAsync("UpdatePresence", bobPlayer);
        Assert.Single(Assert.Single((await bob.InvokeAsync<LoungeSnapshot>("Sync", card.Id)).Recruitments!).Participants);
        Assert.Equal("招募中", (await bob.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "leave")).Status);
        Assert.Equal("已结束", (await alice.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "close")).Status);
        await Assert.ThrowsAsync<HubException>(() => carol.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", card.Id, "join"));
        var sticker = await bob.InvokeAsync<LoungeMessage>("SendMessage", new LoungeSend(Guid.NewGuid().ToString("N"), "nice", "sticker"));
        Assert.Equal(sticker, await broadcast.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var mention = await carol.InvokeAsync<LoungeMessage>("SendMessage", new LoungeSend(Guid.NewGuid().ToString("N"), "@Bob#456 一起来", Mentions: [LoungeRules.PlayerId(bobPlayer)]));
        Assert.Contains(LoungeRules.PlayerId(bobPlayer), LoungeContent.Mentions(mention));
        await Assert.ThrowsAsync<HubException>(() => invalid.InvokeAsync<LoungeMessage>("SendMessage",
            new LoungeSend(Guid.NewGuid().ToString("N"), "https://example.com/upload.png", "sticker")));

        var highlight = new LoungeHighlight(42, "HN1", "alice", 420, DateTimeOffset.UtcNow, "pentakill");
        await alice.InvokeAsync("ReportHighlight", highlight);
        await alice.InvokeAsync("ReportHighlight", highlight);
        await alice.InvokeAsync("ReportHighlight", highlight with { Kind = "legendary" });
        await alice.InvokeAsync("ReportHighlight", highlight with { Kind = "promotion", Detail = "单双排 翡翠IV" });
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("ReportHighlight", highlight with { Puuid = "someone-else" }));
        await alice.InvokeAsync("UpdatePresence", player with { ShareActivity = false });
        await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("ReportHighlight", highlight));
        var snapshot = await bob.InvokeAsync<LoungeSnapshot>("Sync", 0L);
        Assert.Equal(3, snapshot.Messages.Count(m => m.Kind == "highlight"));
        Assert.Single(snapshot.Messages, m => m.Kind == "sticker");
        Assert.Single(snapshot.Messages, m => m.Kind == "recruit");
        Assert.Single(snapshot.Messages, m => m.Kind == "mention");
        await app.StopAsync();
    }

    private static HubConnection Connect(string address, string token) => new HubConnectionBuilder()
        .WithUrl(address + "/hubs/lounge", o => o.AccessTokenProvider = () => Task.FromResult<string?>(token)).Build();

    private sealed class RecruitmentStateComparer : IEqualityComparer<LoungeRecruitmentState>
    {
        public static readonly RecruitmentStateComparer Instance = new();
        public bool Equals(LoungeRecruitmentState? x, LoungeRecruitmentState? y) => LoungeContent.Serialize(x) == LoungeContent.Serialize(y);
        public int GetHashCode(LoungeRecruitmentState obj) => obj.MessageId.GetHashCode();
    }

    [Fact]
    public async Task RecruitmentCapacityIsAtomicAndRetryLeaveCloseAndRestartPreserveState()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "recruitment.db");
        long id;
        var bob = new LoungeMember("bob", "Bob#456", "艾欧尼亚", "在线");
        var carol = bob with { Id = "carol", Name = "Carol#789" };
        var recruitment = new LoungeRecruitment("艾欧尼亚", 420, "不限段位", "任意位置", 1, "");
        using (var store = new LoungeStore(path))
        {
            var post = await store.AppendAsync("recruit", "client", "session", "Alice#123", "艾欧尼亚", "recruit", LoungeContent.Serialize(recruitment), ownerId: "alice");
            id = post!.Id;
            await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(id, bob with { Region = "黑色玫瑰" }, "join"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(id, bob with { Id = "alice" }, "join"));
            var attempts = await Task.WhenAll(new[] { bob, carol }.Select(async member =>
            {
                try { return await store.ChangeRecruitmentAsync(id, member, "join"); }
                catch (ArgumentException) { return null; }
            }));
            Assert.Single(attempts, state => state != null);
            var full = Assert.Single(await store.ReadRecruitmentsAsync());
            var winner = Assert.Single(full.Participants);
            Assert.Equal(full.Revision, (await store.ChangeRecruitmentAsync(id, winner, "join")).Revision);
            await store.AppendAsync("recruit", "client", "session", "Alice#123", "艾欧尼亚", "recruit", LoungeContent.Serialize(recruitment), ownerId: "alice");
            Assert.Single(Assert.Single(await store.ReadRecruitmentsAsync()).Participants);
            var reopened = await store.ChangeRecruitmentAsync(id, winner, "leave");
            Assert.Equal("招募中", reopened.Status);
            Assert.Equal(reopened.Revision, (await store.ChangeRecruitmentAsync(id, winner, "leave")).Revision);
            await store.ChangeRecruitmentAsync(id, bob, "join");
        }
        using (var store = new LoungeStore(path))
        {
            Assert.Equal(bob, Assert.Single(Assert.Single(await store.ReadRecruitmentsAsync()).Participants));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(id, bob, "close"));
            var closed = await store.ChangeRecruitmentAsync(id, bob with { Id = "alice" }, "close");
            Assert.Equal("已结束", closed.Status);
            Assert.Equal(closed.Revision, (await store.ChangeRecruitmentAsync(id, bob with { Id = "alice" }, "close")).Revision);
            await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(id, carol, "join"));
            await store.DeleteAsync(id);
            Assert.Empty(await store.ReadRecruitmentsAsync());
            await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(id, bob, "join"));
        }
    }

    [Fact]
    public async Task ExpiredRecruitmentCannotBeJoinedOrChanged()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "expired.db");
        using var store = new LoungeStore(path);
        var recruitment = new LoungeRecruitment("艾欧尼亚", 400, "不限段位", "任意位置", 1);
        var post = await store.AppendAsync("old", "client", "a", "Alice", "艾欧尼亚", "recruit", LoungeContent.Serialize(recruitment), ownerId: "alice");
        var state = Assert.Single(await store.ReadRecruitmentsAsync());
        using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path);
        await db.OpenAsync();
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE recruitments SET state=$state WHERE messageId=$id";
        command.Parameters.AddWithValue("$state", LoungeContent.Serialize(state with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        command.Parameters.AddWithValue("$id", post!.Id);
        await command.ExecuteNonQueryAsync();
        Assert.Equal("已过期", Assert.Single(await store.ReadRecruitmentsAsync()).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeRecruitmentAsync(post.Id, new("bob", "Bob", "艾欧尼亚", "在线"), "join"));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lounge-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryFolder() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path, true);
        }
    }
}
