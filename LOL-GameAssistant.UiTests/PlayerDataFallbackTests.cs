using System.Reflection;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.Files;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PlayerDataFallbackTests
{
    [Fact]
    public void EmptyRostersReuseOnlyCurrentPhaseAndResetDropsPreviousMatch()
    {
        var cache = new CurrentRosterSnapshotCache();
        var session = new ActiveGameSnapshot { TeamOne = [new GameTeamMember { Puuid = "alice" }] };
        cache.ObservePhase(GameFlowPhase.InProgress);
        Assert.Same(session, cache.ResolveSession(session));
        Assert.Same(session, cache.ResolveSession(null));
        Assert.Same(session, cache.ResolveSession(new ActiveGameSnapshot()));
        cache.ObservePhase(GameFlowPhase.EndOfGame);
        Assert.Null(cache.ResolveSession(null));
        cache.ObservePhase(GameFlowPhase.ChampSelect);
        var selection = new ChampionSelectionSnapshot { MyTeam = [new ChampionSelectionMember { Puuid = "bob" }] };
        Assert.Same(selection, cache.ResolveSelection(selection));
        Assert.Same(selection, cache.ResolveSelection(new ChampionSelectionSnapshot()));
        cache.Clear();
        Assert.Null(cache.ResolveSelection(null));
        var lobby = new LobbySnapshot { Team100 = [new GameTeamMember { Puuid = "bob" }] };
        Assert.Same(lobby, cache.ResolveLobby(lobby));
        Assert.Same(lobby, cache.ResolveLobby(new LobbySnapshot()));
    }

    [Fact]
    public async Task SettingsMeasureAndClearPersistedPlayerDataAndMemoryFallback()
    {
        using var folder = new Folder();
        PlayerProfile? response = new("alice", "Alice", "CN", 1, 100, 2, 3);
        var cache = new CachedPlayerProfileService(Service<IPlayerProfileService>((_, _) => Task.FromResult(response)), folder.Path);
        await cache.GetByPuuidAsync("alice");
        var management = new LocalCacheManagementService(folder.Path);
        var usage = (await management.GetUsageAsync()).Single(item => item.Category == LocalCacheCategory.MatchDetails);
        Assert.Equal(1, usage.FileCount);
        Assert.True(usage.DiskBytes > 0);
        response = null;
        var cleared = await management.ClearAsync(LocalCacheCategory.MatchDetails);
        Assert.Equal(1, cleared.DeletedFiles);
        Assert.Null(await cache.GetByPuuidAsync("alice"));
    }

    [Fact]
    public async Task LatestDataWinsAndEmptyResponsesPreserveDiskFallback()
    {
        using var folder = new Folder();
        var cache = new LocalDataCache<string>(folder.Path, TimeSpan.FromDays(30), value => value.Length > 0);
        Assert.Equal("old", await cache.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>("old")));
        Assert.Equal("new", await cache.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>("new")));
        Assert.Equal("new", await cache.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>("")));
        var restarted = new LocalDataCache<string>(folder.Path, TimeSpan.FromDays(30), value => value.Length > 0);
        Assert.Equal("new", await restarted.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>(null)));
        Assert.Null(await restarted.GetLatestOrCachedAsync("b", () => Task.FromResult<string?>(null)));
        restarted.Clear();
        Assert.Null(await restarted.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>(null)));
    }

    [Fact]
    public async Task CancellationDoesNotReturnCachedDataOrSaveCanceledResponse()
    {
        using var folder = new Folder();
        var cache = new LocalDataCache<string>(folder.Path, TimeSpan.FromDays(30), value => value.Length > 0);
        await cache.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>("saved"));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetLatestOrCachedAsync("a", () =>
        {
            cancellation.Cancel();
            return Task.FromResult<string?>("canceled");
        }, cancellation.Token));
        Assert.Equal("saved", await cache.GetLatestOrCachedAsync("a", () => Task.FromResult<string?>(null)));
    }

    [Fact]
    public async Task ProfileFallbackSurvivesRestartAndSeparatesPlayers()
    {
        using var folder = new Folder();
        PlayerProfile? response = new("alice", "Alice", "CN", 1, 100, 2, 3);
        int calls = 0;
        var inner = Service<IPlayerProfileService>((_, _) => { calls++; return Task.FromResult(response); });
        var cache = new CachedPlayerProfileService(inner, folder.Path);
        await cache.GetByPuuidAsync("alice");
        await cache.GetCurrentAsync();
        response = null;
        Assert.Equal("alice", (await cache.GetCurrentAsync())?.Puuid);
        var restarted = new CachedPlayerProfileService(inner, folder.Path);
        Assert.Equal("Alice", (await restarted.GetByPuuidAsync("alice"))?.GameName);
        Assert.Null(await restarted.GetByPuuidAsync("bob"));
        Assert.Null(await restarted.GetCurrentAsync());
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task RankedFallbackRoundTripsQueuesAndAcceptsFreshUnrankedQueue()
    {
        using var folder = new Folder();
        RankedOverview? response = new();
        response.Queues[RankedQueues.Solo5x5] = new RankedQueue { Tier = "GOLD", Wins = 15, Losses = 9 };
        response.SeasonEndsAt[RankedQueues.Solo5x5] = new DateTime(2026, 12, 1);
        var inner = Service<IRankedStatsService>((_, _) => Task.FromResult(response));
        await new CachedRankedStatsService(inner, folder.Path).GetAsync("alice");
        response = new RankedOverview();
        var restarted = new CachedRankedStatsService(inner, folder.Path);
        var restored = await restarted.GetAsync("alice");
        Assert.Equal(15, restored?.GetQueue(RankedQueues.Solo5x5)?.Wins);
        Assert.Equal(new DateTime(2026, 12, 1), restored!.SeasonEndsAt[RankedQueues.Solo5x5]);
        response = null;
        Assert.Null(await restarted.GetAsync("bob"));
        response = new RankedOverview();
        response.Queues[RankedQueues.Solo5x5] = new RankedQueue { Tier = "UNRANKED" };
        Assert.Equal("UNRANKED", (await restarted.GetAsync("alice"))?.GetQueue(RankedQueues.Solo5x5)?.Tier);
    }

    [Fact]
    public async Task HistoryFallbackSeparatesPagesAndPreservesParticipantStats()
    {
        using var folder = new Folder();
        MatchHistoryResponse? response = new() { Games = new MatchHistoryGames
        {
            GameCount = 1, Games = [new MatchHistoryGame { GameId = 42, Participants =
                [new MatchParticipant { championId = 22, stats = new MatchParticipantStats { kills = 8 } }] }]
        } };
        var inner = Service<IMatchHistoryService>((_, _) => Task.FromResult(response));
        await new CachedMatchHistoryService(inner, folder.Path).GetPageAsync("alice", 0, 199);
        response = new MatchHistoryResponse { Games = new MatchHistoryGames() };
        var restarted = new CachedMatchHistoryService(inner, folder.Path);
        var restored = await restarted.GetPageAsync("alice", 0, 199);
        Assert.Equal(8, restored!.Games!.Games[0].Participants[0].stats!.kills);
        response.Games.Games = null!;
        Assert.Equal(42, (await restarted.GetPageAsync("alice", 0, 199))!.Games!.Games[0].GameId);
        response = null;
        Assert.Null(await restarted.GetPageAsync("bob", 0, 199));
        Assert.Null(await restarted.GetPageAsync("alice", 200, 399));
        Assert.Null(await restarted.GetAllAsync("alice"));
    }

    [Fact]
    public async Task DetailFallbackSurvivesRestartAndEmptyDetailDoesNotOverwriteIt()
    {
        using var folder = new Folder();
        MatchDetail? response = new() { GameId = 42, participants =
            [new MatchParticipant { participantId = 1, stats = new MatchParticipantStats { kills = 8, RuneIds = [8005] } }] };
        var inner = Service<IMatchHistoryService>((_, _) => Task.FromResult(response));
        await new CachedMatchHistoryService(inner, folder.Path).GetDetailAsync(42);
        response = new MatchDetail { GameId = 42 };
        var restarted = new CachedMatchHistoryService(inner, folder.Path);
        Assert.Equal(8, (await restarted.GetDetailAsync(42))!.participants[0].stats!.kills);
        response.participants = null!;
        Assert.Equal(8, (await restarted.GetDetailAsync(42))!.participants[0].stats!.kills);
        response = null;
        var restored = await restarted.GetDetailAsync(42, useCache: false);
        Assert.Equal(new[] { 8005 }, restored!.participants[0].stats!.RuneIds);
        Assert.Null(await restarted.GetDetailAsync(43));
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "player-cache-test-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            string full = System.IO.Path.GetFullPath(Path);
            if (Directory.Exists(full) && full.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(full, recursive: true);
        }
    }
}
