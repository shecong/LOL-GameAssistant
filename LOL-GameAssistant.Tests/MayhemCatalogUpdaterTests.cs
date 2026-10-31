using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MayhemCatalogUpdaterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mayhem-update-tests-" + Guid.NewGuid().ToString("N"));
    private string CacheFile => Path.Combine(_directory, "catalog.json");
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-31T00:00:00Z");
    private static string Payload(string name = "新增幅") => JsonSerializer.Serialize(
        Enumerable.Range(3000, 200).ToDictionary(id => id.ToString(), id => new
        { id, displayName = name + id, rarity = id % 3, description = "作用说明" + id }));
    private static HttpResponseMessage Ok(string payload, string? etag = "\"v1\"")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        if (etag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request); }
    }
    private MayhemCatalogUpdater Create(HttpClient client) => new(CacheFile, client, () => _now);

    [Fact]
    public async Task NewVersionUpdatesNamesRarityDescriptionsAndPersistsForOfflineRestart()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Ok(Payload()))));
        var updater = Create(client);
        await updater.RefreshAsync();
        Assert.Equal("新增幅3004", updater.Entries[3004].Name);
        Assert.Equal(1, updater.Entries[3004].Rarity);
        Assert.Equal("作用说明3004", updater.Entries[3004].Description);
        using var offline = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));
        var restarted = Create(offline);
        Assert.Equal(updater.Version, restarted.Version);
        await restarted.RefreshAsync();
        Assert.Equal("新增幅3004", restarted.Entries[3004].Name);
    }

    [Fact]
    public async Task UsesEtagAfterRestartAndDoesNotRewriteOn304()
    {
        string? sentEtag = null;
        var handler = new Handler(request =>
        {
            sentEtag = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            return Task.FromResult(sentEtag == null ? Ok(Payload()) : new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        using var client = new HttpClient(handler);
        var updater = Create(client);
        await updater.RefreshAsync();
        byte[] cache = File.ReadAllBytes(CacheFile);
        updater = Create(client);
        await updater.RefreshAsync();
        Assert.Equal("\"v1\"", sentEtag);
        Assert.Equal(cache, File.ReadAllBytes(CacheFile));
        Assert.Equal(200, updater.Entries.Count);
    }

    [Fact]
    public async Task HashDetectsChangesWithoutEtagAndChecksAtMostEverySixHours()
    {
        string payload = Payload();
        var handler = new Handler(_ => Task.FromResult(Ok(payload, null)));
        using var client = new HttpClient(handler);
        var updater = Create(client);
        await updater.RefreshAsync();
        string? firstVersion = updater.Version;
        payload = Payload("更新名称");
        _now += TimeSpan.FromHours(5);
        await updater.RefreshAsync();
        Assert.Equal(1, handler.Calls);
        _now += TimeSpan.FromHours(1);
        await updater.RefreshAsync();
        Assert.NotEqual(firstVersion, updater.Version);
        Assert.Equal("更新名称3004", updater.Entries[3004].Name);
        string? secondVersion = updater.Version;
        byte[] cache = File.ReadAllBytes(CacheFile);
        _now += TimeSpan.FromHours(6);
        await updater.RefreshAsync();
        Assert.Equal(secondVersion, updater.Version);
        Assert.Equal(cache, File.ReadAllBytes(CacheFile));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("invalid rarity")]
    [InlineData("oversized")]
    public async Task InvalidUpdateKeepsLastGoodSnapshotAndRetriesAfterFiveMinutes(string invalid)
    {
        string payload = Payload();
        var handler = new Handler(_ => Task.FromResult(Ok(payload)));
        using var client = new HttpClient(handler);
        var updater = Create(client);
        await updater.RefreshAsync();
        string? version = updater.Version;
        byte[] cache = File.ReadAllBytes(CacheFile);
        payload = invalid switch
        {
            "invalid rarity" => Payload().Replace("\"rarity\":1", "\"rarity\":9"),
            "oversized" => new string(' ', 4 * 1024 * 1024 + 1),
            _ => invalid
        };
        _now += TimeSpan.FromHours(6);
        await updater.RefreshAsync();
        Assert.Equal(version, updater.Version);
        Assert.Equal(cache, File.ReadAllBytes(CacheFile));
        payload = Payload("重试成功");
        _now += TimeSpan.FromMinutes(4);
        await updater.RefreshAsync();
        Assert.Equal(2, handler.Calls);
        _now += TimeSpan.FromMinutes(1);
        await updater.RefreshAsync();
        Assert.Equal("重试成功3004", updater.Entries[3004].Name);
    }

    [Theory]
    [InlineData("broken JSON")]
    [InlineData("{\"Version\":\"wrong hash\",\"Payload\":\"{}\"}")]
    public async Task CorruptCacheIsIgnoredAndNextRequestIsUnconditional(string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(CacheFile, contents);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Empty(request.Headers.IfNoneMatch);
            return Task.FromResult(Ok(Payload()));
        }));
        var updater = Create(client);
        Assert.Empty(updater.Entries);
        await updater.RefreshAsync();
        Assert.Equal(200, updater.Entries.Count);
    }

    [Fact]
    public async Task ConcurrentCallersShareRequestAndCancelledWaiterDoesNotCancelUpdate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(_ => { entered.SetResult(); return response.Task; });
        using var client = new HttpClient(handler);
        var updater = Create(client);
        using var cancelled = new CancellationTokenSource();
        var first = updater.RefreshAsync(cancelled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = updater.RefreshAsync();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        response.SetResult(Ok(Payload()));
        await second;
        Assert.Equal(1, handler.Calls);
        Assert.Equal(200, updater.Entries.Count);
    }

    [Fact]
    public async Task UnwritableCacheStillPublishesValidatedUpdateInMemory()
    {
        Directory.CreateDirectory(CacheFile);
        using var client = new HttpClient(new Handler(_ => Task.FromResult(Ok(Payload()))));
        var updater = Create(client);
        await updater.RefreshAsync();
        Assert.Equal(200, updater.Entries.Count);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task FirstRunOfflineKeepsFallbackAvailableAndRecoversOnRetry()
    {
        bool offline = true;
        var handler = new Handler(_ => offline
            ? throw new HttpRequestException("offline")
            : Task.FromResult(Ok(Payload())));
        using var client = new HttpClient(handler);
        var updater = Create(client);
        await updater.RefreshAsync();
        Assert.Empty(updater.Entries);
        Assert.Null(updater.Version);
        Assert.False(File.Exists(CacheFile));
        offline = false;
        _now += TimeSpan.FromMinutes(5);
        await updater.RefreshAsync();
        Assert.Equal(200, updater.Entries.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
