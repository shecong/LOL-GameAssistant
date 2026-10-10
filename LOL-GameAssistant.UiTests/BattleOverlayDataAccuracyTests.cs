using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.LiveGame;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class BattleOverlayDataAccuracyTests
{
    private static readonly GameTeamMember Member = new() { Puuid = "me", SummonerName = "same", ChampionId = 22 };
    private static readonly ActiveGameSnapshot Session = new() { QueueId = 450, GameMode = "ARAM", TeamOne = [Member] };

    [Fact]
    public void FullIdentityConflictCannotFallBackToAnotherPlayersLegacyName()
    {
        var score = new LiveScoreboardPlayer("same#B", "same", "ORDER", 9, 2, 4, 100, null);
        Assert.Null(BattleOverlayForm.FindScore([score], "same#A", "same", "ORDER"));
        Assert.Same(score, BattleOverlayForm.FindScore([score], "same#B", "same", "ORDER"));
        Assert.Same(score, BattleOverlayForm.FindScore([score], "same", "same#B", "ORDER"));
        Assert.Null(BattleOverlayForm.FindScore([score, score], "same#B", "same", "ORDER"));
        Assert.NotNull(BattleOverlayForm.FindScore([score with { RiotId = "" }], "same#A", "same", "ORDER"));
    }

    [Fact]
    public void MissingLiveValuesRemainUnknownWhileReportedZeroRemainsZero()
    {
        var scores = LocalLiveClientDataReader.ParseScoreboard("""
            [{"scores":{}},{"scores":{"kills":0,"deaths":0,"assists":0,"creepScore":0}},
             {"scores":{"kills":3,"deaths":null}}]
            """);
        Assert.Equal("— / — / —", BattleOverlayForm.LiveKdaText(scores[0]));
        Assert.Null(scores[0].CreepScore);
        Assert.Equal("0 / 0 / 0", BattleOverlayForm.LiveKdaText(scores[1]));
        Assert.Equal(0, scores[1].CreepScore);
        Assert.Equal("3 / — / —", BattleOverlayForm.LiveKdaText(scores[2]));
    }

    [Fact]
    public void EmptyRiotIdUsesSplitIdentityFields()
    {
        var score = Assert.Single(LocalLiveClientDataReader.ParseScoreboard("""
            [{"riotId":"","riotIdGameName":"same","riotIdTagLine":"A"}]
            """));
        Assert.Equal("same#A", score.RiotId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnconfirmedKdaFetchesActualPlayersDetail(bool partialNonzero)
    {
        var sample = Sample();
        sample.Participants[0].stats!.kills = partialNonzero ? 3 : 0;
        sample.Participants[0].stats!.HasKdaResult = false;
        int details = 0;
        var service = Service((method, args) =>
        {
            if (method.Name == "GetPageAsync")
            {
                Assert.Equal(99, args[2]);
                return Task.FromResult<MatchHistoryResponse?>(Page(sample));
            }
            details++;
            return Task.FromResult<MatchDetail?>(Detail(new() { kills = 8, deaths = 2, assists = 10, Win = true }));
        });
        var player = await service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.Equal(1, details);
        Assert.Equal(9d, player.RecentKda);
        Assert.Equal(100d, player.ChampionWinRate);
        Assert.True(player.HistoryComplete);
        Assert.False(sample.Participants[0].stats!.HasKdaResult); // Cached source was not mutated.
    }

    [Fact]
    public async Task FailedDetailLeavesKdaUnknownButKeepsConfirmedWin()
    {
        var sample = Sample();
        sample.Participants[0].stats!.kills = 0;
        sample.Participants[0].stats!.assists = 0;
        sample.Participants[0].stats!.deaths = 0;
        var service = Service((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(sample)) : Task.FromResult<MatchDetail?>(null));
        var player = await service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.Null(player.RecentKda);
        Assert.Equal(new[] { true }, player.RecentResults);
        Assert.Equal(1, player.ChampionWins);
        Assert.False(player.HistoryComplete);
        Assert.True(player.NeedsRetry);
        Assert.True(sample.Participants[0].stats!.HasKdaResult);
    }

    [Fact]
    public async Task DetailConfirmedZeroKdaIsValid()
    {
        var sample = Sample();
        sample.Participants[0].stats = new() { Win = true };
        var service = Service((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(sample))
            : Task.FromResult<MatchDetail?>(Detail(new() { Win = true })));
        var player = await service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.Equal(0d, player.RecentKda);
        Assert.True(player.HistoryComplete);
    }

    [Fact]
    public async Task CompleteEmptySourcesDoNotSchedulePermanentRetries()
    {
        var player = await Service((_, _) => Task.FromResult<MatchHistoryResponse?>(Page()))
            .GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.False(player.NeedsRetry);
        Assert.Null(player.Rank);
        Assert.Empty(player.RecentResults);
    }

    [Fact]
    public async Task DetailKdaAndConfirmedSummaryWinCanBeCombined()
    {
        var sample = Sample(); sample.Participants[0].stats!.HasKdaResult = false;
        var service = Service((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(sample))
            : Task.FromResult<MatchDetail?>(Detail(new() { kills = 5, deaths = 2, assists = 7, HasWinResult = false })));
        var player = await service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.Equal(6d, player.RecentKda);
        Assert.Equal(1, player.ChampionWins);
        Assert.True(player.HistoryComplete);
    }

    [Fact]
    public async Task ConflictingProfileIdentityCannotReplaceRosterName()
    {
        var service = new BattleOverlayService(
            Proxy<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new("other", "wrong", "B", 0, 1, 0, 0))),
            Proxy<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(new())),
            Proxy<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(Page())));
        var player = await service.GetPlayerAsync(Member, Session, CancellationToken.None);
        Assert.Equal(Member.SummonerName, player.Name);
        Assert.False(player.ProfileLoaded);
        Assert.True(player.NeedsRetry);
    }

    [Fact]
    public void MissingHistoricalFieldsAreTrackedForSummaryAndDetail()
    {
        var summary = JsonConvert.DeserializeObject<GameHeadModel.MatchHistoryResponse>("""
            {"games":{"games":[{"participants":[{"stats":{"win":true,"kills":3}}]}]}}
            """);
        var stats = LegacyMatchReadModelMapper.ToDomain(summary)!.Games!.Games[0].Participants[0].stats!;
        Assert.False(stats.HasKdaResult);
        Assert.True(stats.HasWinResult);
        var detail = JsonConvert.DeserializeObject<GameDetailModel.GameInfo>("""
            {"participants":[{"stats":{"kills":0,"deaths":0,"assists":0}}]}
            """);
        var detailStats = LegacyMatchReadModelMapper.ToDomain(detail)!.participants[0].stats!;
        Assert.True(detailStats.HasKdaResult);
        Assert.False(detailStats.HasWinResult);
    }

    [Fact]
    public void FailedSourceMergePreservesPriorValuesAndKnownEmptyReplacesThem()
    {
        var previous = BattleOverlayPlayer.Create(Member, "same#A", new() { Tier = "DIAMOND" }, [Sample()], 450, "ARAM");
        var update = BattleOverlayPlayer.Create(Member, "same", null, [], 450, "ARAM") with
        { ProfileLoaded = false, RankLoaded = false, HistoryLoaded = false, HistoryComplete = false };
        var merged = update.Merge(previous);
        Assert.Equal(previous.Name, merged.Name);
        Assert.Same(previous.Rank, merged.Rank);
        Assert.Equal(previous.RecentKda, merged.RecentKda);
        Assert.Equal(previous.RecentResults, merged.RecentResults);
        var empty = update with { RankLoaded = true, HistoryLoaded = true, HistoryComplete = true };
        var cleared = empty.Merge(previous);
        Assert.Null(cleared.Rank);
        Assert.Empty(cleared.RecentResults);
        var other = update with { Member = new() { Puuid = "other", ChampionId = 22 } };
        Assert.Same(other, other.Merge(previous));
    }

    [Fact]
    public void UnknownChampionCannotProduceAChampionWinRate()
    {
        var sample = Sample(); sample.Participants[0].championId = 0;
        var player = BattleOverlayPlayer.Create(new() { Puuid = "me" }, "same", null, [sample], 450, "ARAM");
        Assert.Equal(0, player.ChampionGames);
        Assert.Null(player.ChampionWinRate);
    }

    [Fact]
    public void FailedLiveSourceDoesNotHideAvailableLcuRoster() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var overlay = Overlay(Service((_, _) => Task.FromResult<MatchHistoryResponse?>(Page())),
            Proxy<ILiveClientGameStateService>((_, _) => Task.FromException<IReadOnlyList<LiveScoreboardPlayer>?>(new IOException("offline"))));
        Start(overlay);
        Assert.Same(Session, Field<ActiveGameSnapshot>(overlay, "_session"));
        Assert.Single(Field<Dictionary<string, BattleOverlayPlayer>>(overlay, "_players"));
        Assert.Null(Field<IReadOnlyList<LiveScoreboardPlayer>?>(overlay, "_scores"));
    });

    [Fact]
    public void PendingLiveSourceDoesNotDelayAvailableLcuRoster() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<LiveScoreboardPlayer>?>();
        using var overlay = Overlay(Service((_, _) => Task.FromResult<MatchHistoryResponse?>(Page())),
            Proxy<ILiveClientGameStateService>((_, _) => pending.Task));
        Start(overlay);
        Assert.Same(Session, Field<ActiveGameSnapshot>(overlay, "_session"));
        Assert.False(pending.Task.IsCompleted);
        overlay.StopTracking();
        pending.SetResult([]);
        System.Windows.Forms.Application.DoEvents();
        Assert.Null(Field<ActiveGameSnapshot?>(overlay, "_session"));
    });

    [Fact]
    public void OverlayRetryKeepsRankWhenHistoryRecoversAndRankFails() => MatchListScrollingTests.OnUiThread(() =>
    {
        int rankRequests = 0, historyRequests = 0;
        var overview = new RankedOverview(); overview.Queues[RankedQueues.Solo5x5] = new() { Tier = "DIAMOND" };
        var ranks = Proxy<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(++rankRequests == 1 ? overview : null));
        var service = Service((_, _) => Task.FromResult<MatchHistoryResponse?>(++historyRequests == 1 ? null : Page(Sample())), ranks);
        using var overlay = Overlay(service, Proxy<ILiveClientGameStateService>((_, _) => Task.FromResult<IReadOnlyList<LiveScoreboardPlayer>?>([])));
        Start(overlay);
        var players = Field<Dictionary<string, BattleOverlayPlayer>>(overlay, "_players");
        var initial = Assert.Single(players).Value;
        var retries = Field<Dictionary<string, DateTimeOffset>>(overlay, "_retryAt");
        retries[players.Keys.Single()] = DateTimeOffset.UtcNow.AddSeconds(-1);
        var task = (Task)typeof(BattleOverlayForm).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, null)!;
        Assert.True(task.IsCompletedSuccessfully);
        var refreshed = Assert.Single(players).Value;
        Assert.Same(initial.Rank, refreshed.Rank);
        Assert.Single(refreshed.RecentResults);
        Assert.Single(retries);
    });

    [Theory]
    [InlineData(450, "参考单双排")]
    [InlineData(430, "参考单双排")]
    [InlineData(420, "单双排")]
    [InlineData(440, "灵活组排")]
    public void RankLabelDescribesItsActualSource(int queue, string text) => Assert.Equal(text, BattleOverlayForm.RankSourceText(queue, false));

    private static MatchHistoryGame Sample() => new()
    {
        GameId = 1, QueueId = 450, GameMode = "ARAM", GameDuration = 1200,
        Participants = [new() { participantId = 1, championId = 22, stats = new() { kills = 3, deaths = 1, assists = 2, Win = true } }]
    };
    private static MatchHistoryResponse Page(params MatchHistoryGame[] games) => new() { Games = new() { Games = games.ToList() } };
    private static MatchDetail Detail(MatchParticipantStats stats) => new()
    {
        participantIdentities = [new() { participantId = 1, player = new() { Puuid = "me" } }],
        participants = [new() { participantId = 1, championId = 22, stats = stats }]
    };
    private static BattleOverlayService Service(Func<MethodInfo, object?[], object?> history, IRankedStatsService? ranks = null) => new(
        Proxy<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new("me", "same", "A", 0, 1, 0, 0))),
        ranks ?? Proxy<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(new())), Proxy<IMatchHistoryService>(history));
    private static BattleOverlayForm Overlay(BattleOverlayService details, ILiveClientGameStateService live) => new(
        Proxy<ILobbyService>((_, _) => Task.FromResult<ActiveGameSnapshot?>(Session)), details, live,
        Proxy<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)),
        Proxy<IChampionCatalog>((method, _) => method.Name == "GetDisplayName" ? "寒冰射手" : null));
    private static void Start(BattleOverlayForm overlay)
    {
        overlay.ShowForGame(new Rectangle(0, 0, 1280, 720), "F10");
        overlay.StartTracking();
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, BattleOverlayTests.ServiceProxy>();
        ((BattleOverlayTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
