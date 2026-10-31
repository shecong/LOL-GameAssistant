using System.Reflection;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class RankedLpTrackerTests
{
    private static RankedQueue Rank(int wins, int losses, int lp) => new()
        { QueueType = RankedQueues.Solo5x5, Tier = "GOLD", Division = "II", Wins = wins, Losses = losses, LeaguePoints = lp };
    private static MatchHistoryGame Game(bool win = true) => new()
    {
        PlatformId = "HN1", GameId = 42, QueueId = 420, GameDuration = 1800,
        ParticipantIdentities = [new() { ParticipantId = 1, Player = new() { Puuid = "player" } }],
        Participants = [new() { participantId = 1, stats = new() { Win = win, HasWinResult = true } }]
    };

    [Theory]
    [InlineData("GOLD", "II", 95, "GOLD", "I", 15, true, 20)]
    [InlineData("GOLD", "II", 5, "GOLD", "III", 85, false, -20)]
    [InlineData("GOLD", "I", 95, "PLATINUM", "IV", 15, true, 20)]
    [InlineData("DIAMOND", "I", 90, "MASTER", "", 20, true, 30)]
    [InlineData("MASTER", "", 100, "GRANDMASTER", "", 125, true, 25)]
    [InlineData("CHALLENGER", "I", 1500, "MASTER", "I", 1470, false, -30)]
    public void NormalizesDivisionTierAndApexMovement(string tier, string division, int lp,
        string nextTier, string nextDivision, int nextLp, bool won, int expected)
    {
        var before = Rank(10, 8, lp) with { Tier = tier, Division = division };
        var after = Rank(won ? 11 : 10, won ? 8 : 9, nextLp) with { Tier = nextTier, Division = nextDivision };
        Assert.True(RankedLpTracker.TryCalculate(before, after, Game(won), "player", out int delta));
        Assert.Equal(expected, delta);
        Assert.False(RankedLpTracker.TryCalculate(before, after with { QueueType = RankedQueues.Flex5x5 }, Game(won), "player", out _));
    }

    [Fact]
    public async Task ResumesPersistedGameAndWaitsForStableSettlement()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ranked-resume-" + Guid.NewGuid());
        try
        {
            string path = Path.Combine(directory, "lp.json");
            new RankedLpStore(path).SavePending(new(42, 420, "player", Rank(10, 8, 40), DateTimeOffset.UtcNow));
            var store = new RankedLpStore(path); int reads = 0;
            var tracker = new RankedLpTracker(Service<ILobbyService>((_, _) => null),
                Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new("player", "a", "b", 0, 1, 0, 0))),
                Service<IRankedStatsService>((_, _) =>
                {
                    var overview = new RankedOverview();
                    overview.Queues[RankedQueues.Solo5x5] = Rank(11, 8, ++reads == 1 ? 61 : 62);
                    return Task.FromResult<RankedOverview?>(overview);
                }), Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [Game()] } })),
                store, (_, _) => Task.CompletedTask);
            await tracker.CaptureEndAsync();
            Assert.Equal(4, reads);
            Assert.Equal(22, store.Get("HN1", "player", 42));
            Assert.Null(new RankedLpStore(path).LoadPending());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void IgnoresExpiredOrCorruptPendingSnapshots()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ranked-expired-" + Guid.NewGuid());
        try
        {
            string path = Path.Combine(directory, "lp.json"); var store = new RankedLpStore(path);
            store.SavePending(new(42, 420, "player", Rank(10, 8, 40), DateTimeOffset.UtcNow.AddHours(-7)));
            Assert.Null(store.LoadPending());
            File.WriteAllText(path + ".pending", "broken"); Assert.Null(store.LoadPending());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CalculatesOnlyOneConfirmedMatchInSameDivision()
    {
        var before = Rank(10, 8, 40);
        Assert.True(RankedLpTracker.TryCalculate(before, Rank(11, 8, 61), Game(), "player", out int win));
        Assert.Equal(21, win);
        Assert.True(RankedLpTracker.TryCalculate(before, Rank(10, 9, 23), Game(false), "player", out int loss));
        Assert.Equal(-17, loss);
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(12, 8, 80), Game(), "player", out _));
        Assert.True(RankedLpTracker.TryCalculate(before, Rank(11, 8, 2) with { Division = "I" }, Game(), "player", out int promoted));
        Assert.Equal(62, promoted);
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(11, 8, 61) with { IsProvisional = true }, Game(), "player", out _));
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(11, 8, 61), Game(), "other", out _));
        var remake = Game(); remake.GameDuration = 100;
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(11, 8, 61), remake, "player", out _));
    }

    [Fact]
    public async Task RecordsOnlyMatchingPlayerGameAndRegion()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ranked-lp-" + Guid.NewGuid());
        try
        {
            var store = new RankedLpStore(Path.Combine(directory, "lp.json"));
            bool ended = false;
            var lobby = Service<ILobbyService>((method, _) => method.Name == "GetCurrentSessionAsync"
                ? Task.FromResult<ActiveGameSnapshot?>(new() { GameId = 42, QueueId = 420, TeamOne = [new() { Puuid = "player" }] })
                : Task.FromResult<string?>("InProgress"));
            var profiles = Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new("player", "a", "b", 0, 1, 0, 0)));
            var ranks = Service<IRankedStatsService>((_, _) => { var overview = new RankedOverview(); overview.Queues[RankedQueues.Solo5x5] = ended ? Rank(11, 8, 61) : Rank(10, 8, 40); return Task.FromResult<RankedOverview?>(overview); });
            var history = Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [Game()] } }));
            var tracker = new RankedLpTracker(lobby, profiles, ranks, history, store, (_, _) => Task.CompletedTask);
            await tracker.CaptureStartAsync(); ended = true; await tracker.CaptureEndAsync();
            Assert.Equal(21, store.Get("HN1", "player", 42));
            Assert.Null(store.Get("HN2", "player", 42));
            Assert.Null(store.Get("HN1", "other", 42));
            Assert.Equal(21, new RankedLpStore(Path.Combine(directory, "lp.json")).Get("hn1", "player", 42));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RankedRowsShowOnlyTheirOwnRecordedPoints() => MatchListScrollingTests.OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "ranked-lp-ui-" + Guid.NewGuid());
        try
        {
            var store = new RankedLpStore(Path.Combine(directory, "lp.json"));
            store.Save("HN1", "player", 42, 21);
            var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
            var champions = Service<IChampionCatalog>((_, _) => "英雄");
            using var row = new MatchHistoryListRow(Game(), "player", assets, champions, store) { Width = 1200 };
            Assert.Equal("+21 LP", row.LpText);
            using var other = new MatchHistoryListRow(Game(), "other", assets, champions, store);
            Assert.Equal("胜点 —", other.LpText);
            var normal = Game(); normal.QueueId = 430;
            using var unranked = new MatchHistoryListRow(normal, "player", assets, champions, store);
            Assert.Null(unranked.LpText);
            Assert.Contains("+21 LP", row.AccessibleName);
            _ = row.Handle;
            store.Save("HN1", "player", 42, 22);
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal("+22 LP", row.LpText);
            Assert.Contains("+22 LP", row.AccessibleName);
            if (Environment.GetEnvironmentVariable("RANKED_LP_PREVIEW") is string path)
            {
                using var image = new Bitmap(row.Width, row.Height);
                row.DrawToBitmap(image, row.ClientRectangle); image.Save(path);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });
    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
