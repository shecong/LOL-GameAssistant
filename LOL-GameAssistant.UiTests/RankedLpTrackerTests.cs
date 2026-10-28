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

    [Fact]
    public void CalculatesOnlyOneConfirmedMatchInSameDivision()
    {
        var before = Rank(10, 8, 40);
        Assert.True(RankedLpTracker.TryCalculate(before, Rank(11, 8, 61), Game(), "player", out int win));
        Assert.Equal(21, win);
        Assert.True(RankedLpTracker.TryCalculate(before, Rank(10, 9, 23), Game(false), "player", out int loss));
        Assert.Equal(-17, loss);
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(12, 8, 80), Game(), "player", out _));
        Assert.False(RankedLpTracker.TryCalculate(before, Rank(11, 8, 2) with { Division = "I" }, Game(), "player", out _));
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
            var tracker = new RankedLpTracker(lobby, profiles, ranks, history, store);
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
