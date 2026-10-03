using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LocalDataCacheTests
{
    [Fact]
    public async Task NewCacheInstanceReadsDiskWithoutDownloadingAgain()
    {
        using var folder = new CacheFolder();
        var first = Strings(folder.Path);
        Assert.Equal("cached", await first.GetAsync("key", () => Task.FromResult<string?>("cached")));
        var restarted = Strings(folder.Path);
        Assert.Equal("cached", await restarted.GetAsync("key", () => throw new InvalidOperationException("Network must not be used.")));
    }

    [Fact]
    public async Task ExpiredAndCorruptFilesAreFetchedAgain()
    {
        using var folder = new CacheFolder();
        var clock = new FakeClock();
        var first = Strings(folder.Path, clock);
        await first.GetAsync("key", () => Task.FromResult<string?>("old"));
        clock.Now += TimeSpan.FromHours(7);
        Assert.Equal("new", await first.GetAsync("key", () => Task.FromResult<string?>("new")));
        await File.WriteAllTextAsync(Directory.GetFiles(folder.Path).Single(), "broken json");
        Assert.Equal("recovered", await Strings(folder.Path, clock).GetAsync("key", () => Task.FromResult<string?>("recovered")));
    }

    [Fact]
    public async Task ConcurrentCallersShareDownloadAndOneCancellationDoesNotCancelOthers()
    {
        using var folder = new CacheFolder();
        var cache = Strings(folder.Path);
        var response = new TaskCompletionSource<string?>();
        int requests = 0;
        Task<string?> Fetch() { Interlocked.Increment(ref requests); return response.Task; }
        using var cancellation = new CancellationTokenSource();
        Task<string?> canceled = cache.GetAsync("key", Fetch, cancellation.Token);
        Task<string?> kept = cache.GetAsync("key", Fetch);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        response.SetResult("shared");
        Assert.Equal("shared", await kept);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task MissingDataIsNotCachedAndFailuresCanRetry()
    {
        using var folder = new CacheFolder();
        var cache = Strings(folder.Path);
        Assert.Null(await cache.GetAsync("key", () => Task.FromResult<string?>(null)));
        await Assert.ThrowsAsync<IOException>(() => cache.GetAsync("key", () => throw new IOException("offline")));
        Assert.Equal("recovered", await cache.GetAsync("key", () => Task.FromResult<string?>("recovered")));
    }

    [Fact]
    public async Task BuildCacheSeparatesLanesModesAndVersionsAndRoundTripsFullOptions()
    {
        using var folder = new CacheFolder();
        int calls = 0;
        string version = "1";
        var inner = Service<IOpggBuildApplyService>((method, args) =>
        {
            Assert.Equal("GetBuildChoicesAsync", method.Name);
            calls++;
            string mode = ((OpggBuildRequest)args[2]!).GameMode == "ARAM" ? "aram" : "ranked";
            return Task.FromResult(new OpggBuildChoices(true, "ok", "德莱厄斯", (string)args[1]!,
                [new OpggBuildOption(1, [1054], [3078, 3053, 6333], [3026], 8000, 8200,
                    [8010, 9111, 9104, 8299, 8234, 8236, 5008, 5008, 5001], 100, 55, [4, 12], mode)],
                Mode: mode, ChampionId: 122));
        });
        var first = new CachedOpggBuildApplyService(inner, () => version, folder.Path);
        await first.GetBuildChoicesAsync(122, "TOP");
        var restarted = new CachedOpggBuildApplyService(inner, () => version, folder.Path);
        var restored = await restarted.GetBuildChoicesAsync(122, "top");
        Assert.Equal(1, calls);
        Assert.Equal(new[] { 3078, 3053, 6333 }, restored.Options[0].CoreItemIds);
        Assert.Equal(55, restored.Options[0].WinRate);
        await restarted.GetBuildChoicesAsync(122, "BOTTOM");
        await restarted.GetBuildChoicesAsync(122, "TOP", new OpggBuildRequest("ARAM"));
        Assert.Equal(3, calls);
        version = "2";
        await restarted.GetBuildChoicesAsync(122, "TOP");
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task AssetCacheReusesIconsAcrossRestartAndInvalidatesOnPatchChange()
    {
        using var folder = new CacheFolder();
        int calls = 0;
        string version = "1";
        var inner = Service<IGameAssetService>((_, _) =>
        {
            calls++;
            return Task.FromResult<GameAsset?>(new GameAsset([1, 2, 3]));
        });
        var first = new CachedGameAssetService(inner, () => version, folder.Path);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => first.GetItemIconAsync(3078)));
        Assert.Equal(1, calls);
        var restarted = new CachedGameAssetService(inner, () => version, folder.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await restarted.GetItemIconAsync(3078))!.Content);
        Assert.Equal(1, calls);
        version = "2";
        await restarted.GetItemIconAsync(3078);
        Assert.Equal(2, calls);
    }

    private static LocalDataCache<string> Strings(string path, TimeProvider? clock = null) =>
        new(path, TimeSpan.FromHours(6), value => !string.IsNullOrEmpty(value), clock);
    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CacheFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lol-cache-test-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            if (Directory.Exists(Path) && System.IO.Path.GetFullPath(Path).StartsWith(
                System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(Path, recursive: true);
        }
    }
}
