using System.Reflection;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class BattleOverlayRefactorTests
{
    private static readonly GameTeamMember Member = new() { Puuid = "me", ChampionId = 22, SummonerName = "me" };
    private static readonly ActiveGameSnapshot Session = new() { QueueId = 450, GameMode = "ARAM", TeamOne = [Member] };

    [Fact]
    public void AnalysisCountsActualWinsAndAggregatesKdaInsteadOfAveragingRatios()
    {
        var stats = BattleHistoryStatistics.Analyze([Sample(1, true, 10, 2, 8), Sample(2, false, 2, 6, 4)], "me", 22, 450, "ARAM");
        Assert.Equal(2, stats.ChampionGames);
        Assert.Equal(1, stats.ChampionWins);
        Assert.Equal(1, stats.ChampionLosses);
        Assert.Equal(50d, stats.ChampionWinRate);
        Assert.Equal(50d, stats.RecentWinRate);
        Assert.Equal(3d, stats.RecentKda);
        Assert.Equal(6d, stats.AverageKills);
        Assert.Equal(4d, stats.AverageDeaths);
        Assert.Equal(6d, stats.AverageAssists);
    }

    [Fact]
    public void UnknownResultInterruptsStreakAndIsNotCountedAsALoss()
    {
        var unknown = Sample(2, false); unknown.Participants[0].stats!.HasWinResult = false;
        var stats = BattleHistoryStatistics.Analyze([Sample(1, true), unknown, Sample(3, true)], "me", 22, 450, "ARAM");
        Assert.Equal(3, stats.ModeGames);
        Assert.Equal(1, stats.UnknownResults);
        Assert.Equal(2, stats.RecentWins);
        Assert.Equal(0, stats.RecentLosses);
        Assert.Equal(1, stats.RecentStreak);
    }

    [Fact]
    public void HistoryWindowAndRecentLimitHaveDifferentExplicitCounts()
    {
        var history = Enumerable.Range(1, 110).Select(id => Sample(id, id % 2 == 0)).ToList();
        history[0].QueueId = 430;
        var stats = BattleHistoryStatistics.Analyze(history, "me", 22, 450, "ARAM");
        Assert.Equal(100, stats.WindowGames);
        Assert.Equal(99, stats.ModeGames);
        Assert.Equal(99, stats.ChampionGames);
        Assert.Equal(10, stats.RecentGames);
    }

    [Fact]
    public void MissingKdaKeepsWinsButDoesNotInventAnAverage()
    {
        var missing = Sample(1, true); missing.Participants[0].stats!.HasKdaResult = false;
        var stats = BattleHistoryStatistics.Analyze([missing, Sample(2, false)], "me", 22, 450, "ARAM");
        Assert.Null(stats.RecentKda);
        Assert.Null(stats.AverageKills);
        Assert.Equal(1, stats.RecentWins);
        Assert.Equal(1, stats.RecentLosses);
    }

    [Fact]
    public async Task ProfileAndRankPublishBeforePendingHistoryCompletes()
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service((method, _) => method.Name == "GetPageAsync" ? pending.Task : Task.FromResult<MatchDetail?>(null));
        var updates = new List<BattleOverlayPlayer>();
        Task<BattleOverlayPlayer> task = service.GetPlayerAsync(Member, Session, CancellationToken.None, updates.Add);
        Assert.False(task.IsCompleted);
        Assert.Contains(updates, player => player.ProfileLoaded && player.RankLoaded && !player.HistoryLoaded);
        pending.SetResult(Page());
        var final = await task;
        Assert.True(final.HistoryLoaded);
        Assert.True(final.HistoryComplete);
        Assert.Equal("me#A", final.Name);
    }

    [Fact]
    public async Task PlayersSharingAMatchUseOneDetailRequestAndTheirOwnStatistics()
    {
        var pending = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        var service = Service((method, _) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(Sample(1, true)));
            Interlocked.Increment(ref requests);
            return pending.Task;
        });
        var other = new GameTeamMember { Puuid = "other", ChampionId = 22 };
        Task<BattleOverlayPlayer> first = service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Task<BattleOverlayPlayer> second = service.GetPlayerAsync(other, Session, CancellationToken.None);
        Assert.Equal(1, requests);
        pending.SetResult(Detail());
        var players = await Task.WhenAll(first, second);
        Assert.Equal(100d, players[0].ChampionWinRate);
        Assert.Equal(0d, players[1].ChampionWinRate);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task CancellingOnePlayerDoesNotCancelSharedDetailForAnother()
    {
        var pending = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        int requests = 0;
        var service = Service((method, args) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(Sample(1, true)));
            requests++;
            requestToken = (CancellationToken)args[2]!;
            return pending.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var first = service.GetPlayerAsync(Member, Session, cancellation.Token);
        var second = service.GetPlayerAsync(new() { Puuid = "other", ChampionId = 22 }, Session, CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(requestToken.IsCancellationRequested);
        pending.SetResult(Detail());
        Assert.True((await second).HistoryComplete);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task FailedDetailIsRetriedAndSuccessfulDetailIsReused()
    {
        int requests = 0;
        var service = Service((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(Sample(1, true)))
            : Task.FromResult<MatchDetail?>(++requests == 1 ? null : Detail()));
        Assert.False((await service.GetPlayerAsync(Member, Session, CancellationToken.None)).HistoryComplete);
        Assert.True((await service.GetPlayerAsync(Member, Session, CancellationToken.None)).HistoryComplete);
        Assert.True((await service.GetPlayerAsync(Member, Session, CancellationToken.None)).HistoryComplete);
        Assert.Equal(2, requests);
    }

    [Fact]
    public void PresentationDistinguishesLoadingEmptyAndUnverifiedResults()
    {
        var player = BattleOverlayPlayer.Create(Member, "me", null, [], 450, "ARAM");
        Assert.Equal("战绩读取中…", BattleOverlayForm.RecentRecordText(player with { HistoryLoaded = false }, false));
        Assert.Equal("暂无样本", BattleOverlayForm.RecentRecordText(player, false));
        Assert.Equal("暂无样本 · 待核验", BattleOverlayForm.RecentRecordText(player with { HistoryComplete = false }, false));
        var partial = BattleOverlayPlayer.Create(Member, "me", null, [Sample(1, true)], 450, "ARAM")
            with { HistoryComplete = false };
        Assert.Equal("待核验 · 1胜0负", BattleOverlayForm.RecentRecordText(partial, false));
        Assert.Equal("钻石一 55LP", BattleOverlayForm.RankText(new() { Tier = "DIAMOND", Division = "I", LeaguePoints = 55 }, false));
    }

    [Theory]
    [InlineData(50, 2)]
    [InlineData(20, 5)]
    public async Task HistoryPagesThroughServerCapsToTheHundredGameWindow(int cap, int expectedRequests)
    {
        int requests = 0;
        var loader = new BattleOverlayHistoryLoader(Proxy<IMatchHistoryService>((method, args) =>
        {
            if (method.Name != "GetPageAsync") return Task.FromResult<MatchDetail?>(Detail());
            requests++;
            int offset = (int)args[1]!;
            Assert.Equal(Math.Min(offset + 50, 100) - 1, args[2]);
            var page = Page(Enumerable.Range(offset + 1, Math.Min(cap, 100 - offset))
                .Select(id => Sample(id, true)).ToArray());
            page.Games!.GameCount = 180;
            return Task.FromResult<MatchHistoryResponse?>(page);
        }));
        var result = await loader.LoadAsync(Member, Session, CancellationToken.None);
        Assert.True(result.Complete);
        Assert.Equal(100, result.WindowGames);
        Assert.Equal(expectedRequests, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrRepeatedSecondPagePreservesPartialHistoryAndRequestsRetry(bool repeated)
    {
        var first = Page(Enumerable.Range(1, 50).Select(id => Sample(id, true)).ToArray());
        first.Games!.GameCount = 100;
        int requests = 0;
        var loader = new BattleOverlayHistoryLoader(Proxy<IMatchHistoryService>((method, _) =>
        {
            if (method.Name != "GetPageAsync") return Task.FromResult<MatchDetail?>(Detail());
            return Task.FromResult<MatchHistoryResponse?>(++requests == 1 || repeated ? first : null);
        }));
        var result = await loader.LoadAsync(Member, Session, CancellationToken.None);
        Assert.True(result.Loaded);
        Assert.False(result.Complete);
        Assert.Equal(50, result.WindowGames);
        Assert.Equal(2, requests);
    }

    private static MatchHistoryGame Sample(long id, bool win, int kills = 3, int deaths = 1, int assists = 2) => new()
    {
        GameId = id, GameCreation = 1000 - id, QueueId = 450, GameMode = "ARAM", GameDuration = 1200,
        Participants = [new() { participantId = 1, championId = 22,
            stats = new() { kills = kills, deaths = deaths, assists = assists, Win = win } }]
    };
    private static MatchHistoryResponse Page(params MatchHistoryGame[] games) => new() { Games = new() { Games = games.ToList() } };
    private static MatchDetail Detail() => new()
    {
        participantIdentities = [new() { participantId = 1, player = new() { Puuid = "me" } },
            new() { participantId = 2, player = new() { Puuid = "other" } }],
        participants = [new() { participantId = 1, championId = 22, stats = new() { kills = 8, deaths = 2, assists = 4, Win = true } },
            new() { participantId = 2, championId = 22, stats = new() { kills = 2, deaths = 8, assists = 4, Win = false } }]
    };
    private static BattleOverlayService Service(Func<MethodInfo, object?[], object?> history) => new(
        Proxy<IPlayerProfileService>((_, args) => Task.FromResult<PlayerProfile?>(new((string)args[0]!, "me", "A", 0, 1, 0, 0))),
        Proxy<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(new())), Proxy<IMatchHistoryService>(history));
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, BattleOverlayTests.ServiceProxy>();
        ((BattleOverlayTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
