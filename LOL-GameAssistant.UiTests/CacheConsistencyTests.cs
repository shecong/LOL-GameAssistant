using System.Reflection;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CacheConsistencyTests
{
    [Fact]
    public async Task OlderRefreshCannotOverwriteNewerMemoryOrDisk()
    {
        using var folder = new Folder();
        var cache = Strings(folder.Path);
        var oldResponse = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> older = cache.GetLatestOrCachedAsync("same", () => oldResponse.Task);
        Assert.Equal("new", await cache.GetLatestOrCachedAsync("same", () => Task.FromResult<string?>("new")));
        oldResponse.SetResult("old");
        Assert.Equal("old", await older);
        Assert.Equal("new", await cache.GetAsync("same", () => throw new InvalidOperationException()));
        Assert.Equal("new", await Strings(folder.Path).GetAsync("same", () => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task SharedDetailDownloadSurvivesFirstWindowCancellation()
    {
        using var folder = new Folder();
        var response = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        CancellationToken networkToken = default;
        var service = new CachedMatchHistoryService(Service<IMatchHistoryService>((method, args) =>
        {
            Assert.Equal("GetDetailAsync", method.Name);
            calls++;
            networkToken = (CancellationToken)args[2]!;
            started.TrySetResult();
            return response.Task.WaitAsync(networkToken);
        }), folder.Path);
        using var cancellation = new CancellationTokenSource();
        var first = service.GetDetailAsync(42, cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = service.GetDetailAsync(42);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(networkToken.IsCancellationRequested);
        response.SetResult(Detail(42, 100));
        Assert.Equal(100, (await second)!.gameDuration);
        Assert.Equal(100, (await service.GetDetailAsync(42))!.gameDuration);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClearDetailCacheClearsEveryInstanceAndDiskWithoutClearingProfiles()
    {
        using var folder = new Folder();
        int calls = 0, clears = 0;
        var inner = Service<IMatchHistoryService>((method, _) => method.Name switch
        {
            "GetDetailAsync" => Task.FromResult<MatchDetail?>(Detail(42, ++calls)),
            "ClearDetailCache" => Clear(),
            _ => throw new InvalidOperationException(method.Name)
        });
        object? Clear() { clears++; return null; }
        var first = new CachedMatchHistoryService(inner, folder.Path);
        var second = new CachedMatchHistoryService(inner, folder.Path);
        Assert.Equal(1, (await first.GetDetailAsync(42))!.gameDuration);
        Assert.Equal(1, (await second.GetDetailAsync(42))!.gameDuration);
        var profiles = Strings(Path.Combine(folder.Path, "player-data", "profiles"));
        await profiles.GetAsync("profile", () => Task.FromResult<string?>("keep"));
        first.ClearDetailCache();
        Assert.Equal(1, clears);
        Assert.Empty(Directory.GetFiles(Path.Combine(folder.Path, "player-data", "details"), "*.json"));
        Assert.Equal(2, (await second.GetDetailAsync(42))!.gameDuration);
        Assert.Equal("keep", await profiles.GetAsync("profile", () => throw new InvalidOperationException()));
        Assert.Equal(2, (await new CachedMatchHistoryService(inner, folder.Path).GetDetailAsync(42))!.gameDuration);
    }

    [Fact]
    public async Task ClearDetailCachePreventsLateDownloadsFromRestoringDeletedData()
    {
        using var folder = new Folder();
        var response = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var inner = Service<IMatchHistoryService>((method, _) =>
        {
            if (method.Name == "ClearDetailCache") return null;
            if (++calls == 1) { started.TrySetResult(); return response.Task; }
            return Task.FromResult<MatchDetail?>(Detail(42, 200));
        });
        var service = new CachedMatchHistoryService(inner, folder.Path);
        var old = service.GetDetailAsync(42);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.ClearDetailCache();
        Assert.Equal(200, (await service.GetDetailAsync(42))!.gameDuration);
        response.SetResult(Detail(42, 100));
        Assert.Equal(100, (await old)!.gameDuration);
        Assert.Equal(200, (await service.GetDetailAsync(42))!.gameDuration);
        Assert.Equal(200, (await new CachedMatchHistoryService(inner, folder.Path).GetDetailAsync(42))!.gameDuration);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PlayerAliasesCannotFallBackToAnotherRegion()
    {
        using var session = new Session();
        using var folder = new Folder();
        string platform = "HN1";
        var scope = Scope(() => platform);
        PlayerProfile? response = new("region-a-player", "same-name", "tag", 1, 1, 0, 0);
        var profiles = new CachedPlayerProfileService(Service<IPlayerProfileService>((method, _) =>
            method.Name == "ResetClientContext" ? null : Task.FromResult(response)), folder.Path, scope.GetAsync);
        PlayerHistorySession.SetClient("region-A");
        await profiles.FindByNameAsync("same-name");
        await profiles.FindByRiotIdAsync("same-name", "tag");
        profiles.ResetClientContext();
        PlayerHistorySession.SetClient("region-B");
        platform = "HN10";
        response = null;
        Assert.Null(await profiles.FindByNameAsync("same-name"));
        Assert.Null(await profiles.FindByRiotIdAsync("same-name", "tag"));
        PlayerHistorySession.SetClient("region-A-restarted");
        platform = "HN1";
        Assert.Equal("region-a-player", (await profiles.FindByNameAsync("same-name"))!.Puuid);
    }

    [Fact]
    public async Task HistoryDiskCacheSurvivesClientRestartButSeparatesRegions()
    {
        using var session = new Session();
        using var folder = new Folder();
        string platform = "HN1";
        bool online = true;
        int detailCalls = 0;
        var inner = Service<IMatchHistoryService>((method, _) => method.Name switch
        {
            "GetPageAsync" => Task.FromResult<MatchHistoryResponse?>(online ? Page(42) : null),
            "GetDetailAsync" => ReadDetail(),
            _ => null
        });
        Task<MatchDetail?> ReadDetail() { detailCalls++; return Task.FromResult<MatchDetail?>(online ? Detail(42, 100) : null); }
        PlayerHistorySession.SetClient("boot-1"); PlayerHistorySession.SetAccount("account");
        var first = new CachedMatchHistoryService(inner, folder.Path, Scope(() => platform).GetAsync);
        await first.ReadSnapshotAsync("player", default);
        await first.GetPageAsync("player", 0, 499);
        await first.GetDetailAsync(42);
        online = false;
        PlayerHistorySession.SetClient("boot-2"); PlayerHistorySession.SetAccount("account");
        var restarted = new CachedMatchHistoryService(inner, folder.Path, Scope(() => platform).GetAsync);
        Assert.Equal(HistoryDataState.Cached, (await restarted.ReadSnapshotAsync("player", default)).State);
        Assert.Equal(42, (await restarted.GetPageAsync("player", 0, 499))!.Games!.Games[0].GameId);
        Assert.Equal(100, (await restarted.GetDetailAsync(42))!.gameDuration);
        Assert.Equal(1, detailCalls);
        PlayerHistorySession.SetClient("other-region");
        platform = "HN10";
        Assert.Null(await restarted.GetPageAsync("player", 0, 499));
        Assert.Null(await restarted.GetDetailAsync(42));
        await Assert.ThrowsAsync<IOException>(() => restarted.ReadSnapshotAsync("player", default));
    }

    [Fact]
    public async Task ReconnectedClientDoesNotJoinAnOldSessionDetailDownload()
    {
        using var session = new Session();
        using var folder = new Folder();
        PlayerHistorySession.SetClient("old-client");
        var oldResponse = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var service = new CachedMatchHistoryService(Service<IMatchHistoryService>((_, _) =>
        {
            if (++calls == 1) { started.TrySetResult(); return oldResponse.Task; }
            return Task.FromResult<MatchDetail?>(Detail(42, 200));
        }), folder.Path, Scope(() => "HN1").GetAsync);
        var old = service.GetDetailAsync(42);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        PlayerHistorySession.SetClient("new-client");
        Assert.Equal(200, (await service.GetDetailAsync(42))!.gameDuration);
        oldResponse.SetResult(Detail(42, 100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.Equal(200, (await service.GetDetailAsync(42))!.gameDuration);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnknownRegionUsesSessionIsolationAndRetriesMetadata()
    {
        using var session = new Session();
        string? platform = null;
        var scope = new PlayerDataCacheScope(Service<ILcuRequestSender>((_, args) =>
            Task.FromResult((string)args[0]! == "/lol-platform-config/v1/namespaces/LoginDataPacket/platformId"
                ? platform : null)));
        PlayerHistorySession.SetClient("unknown-A");
        string first = await scope.GetAsync(default);
        PlayerHistorySession.SetClient("unknown-B");
        Assert.NotEqual(first, await scope.GetAsync(default));
        platform = "\"HN1\"";
        Assert.Equal("sgp-v1:platform:HN1", await scope.GetAsync(default));
    }

    private static PlayerDataCacheScope Scope(Func<string> platform) => new(Service<ILcuRequestSender>((_, args) =>
    {
        Assert.Equal("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", args[0]);
        return Task.FromResult<string?>(System.Text.Json.JsonSerializer.Serialize(platform()));
    }));
    private static MatchDetail Detail(long id, int duration) => new() { GameId = id, gameDuration = duration, participants = [new() { participantId = 1 }] };
    private static MatchHistoryResponse Page(long id) => new() { Games = new() { Games = [new() { GameId = id }] } };
    private static LocalDataCache<string> Strings(string path) => new(path, TimeSpan.FromDays(1), value => value.Length > 0);
    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private sealed class Session : IDisposable
    {
        private readonly string _client = (string)typeof(PlayerHistorySession).GetField("_client", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        private readonly string _account = (string)typeof(PlayerHistorySession).GetField("_account", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        public void Dispose() { PlayerHistorySession.SetClient(_client); PlayerHistorySession.SetAccount(_account); }
    }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cache-consistency-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            string full = System.IO.Path.GetFullPath(Path);
            if (Directory.Exists(full) && full.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(full, recursive: true);
        }
    }
}
