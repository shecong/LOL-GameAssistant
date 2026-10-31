using System.Net;
using System.Text;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class NetworkTrafficTests
{
    [Fact]
    public async Task DisabledModuleNeverReachesTransportAndCanBeEnabledAgain()
    {
        var service = new NetworkTrafficService();
        int calls = 0;
        using var client = Client(service, NetworkModule.Ai, (_, _) => { calls++; return Task.FromResult(Reply("ok")); });
        service.SetEnabled(NetworkModule.Ai, false);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test"));
        Assert.Equal(0, calls);
        Assert.Equal(1, Row(service, NetworkModule.Ai).Blocked);
        Assert.True(service.IsEnabled(NetworkModule.Builds));
        service.SetEnabled(NetworkModule.Ai, true);
        Assert.Equal("ok", await client.GetStringAsync("https://example.test"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CountsActualUtf8PayloadOnceIncludingUploadsAndErrorBodies()
    {
        var service = new NetworkTrafficService();
        using var client = Client(service, NetworkModule.Ai, async (request, token) =>
        {
            await request.Content!.CopyToAsync(Stream.Null, token);
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("错误", Encoding.UTF8) };
        });
        using var response = await client.PostAsync("https://example.test", new StringContent("你好", Encoding.UTF8));
        Assert.Equal("错误", await response.Content.ReadAsStringAsync());
        Assert.Equal("错误", await response.Content.ReadAsStringAsync());
        var row = Row(service, NetworkModule.Ai);
        Assert.Equal(6, row.Uploaded);
        Assert.Equal(6, row.Downloaded);
        Assert.Equal(1, row.Requests);
    }

    [Fact]
    public async Task StreamingDownloadsAreNotBufferedAndCountOnlyReadBytes()
    {
        var service = new NetworkTrafficService();
        using var client = Client(service, NetworkModule.Updates, (_, _) => Task.FromResult(Reply("1234567890")));
        using var response = await client.GetAsync("https://example.test", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(0, Row(service, NetworkModule.Updates).Downloaded);
        using var stream = await response.Content.ReadAsStreamAsync();
        Assert.Equal(3, await stream.ReadAsync(new byte[3]));
        Assert.Equal(3, Row(service, NetworkModule.Updates).Downloaded);
        service.SetEnabled(NetworkModule.Updates, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.ReadExactlyAsync(new byte[3]));
        Assert.Equal(3, Row(service, NetworkModule.Updates).Downloaded);
    }

    [Fact]
    public async Task DisablingCancelsAnInFlightRequest()
    {
        var service = new NetworkTrafficService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(service, NetworkModule.Builds, async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Reply("unreachable");
        });
        var pending = client.GetAsync("https://example.test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.SetEnabled(NetworkModule.Builds, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void PersistenceRestoresOnlyAccessPolicyAndResetKeepsPolicy()
    {
        string folder = Path.Combine(Path.GetTempPath(), "traffic-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "network.json");
        try
        {
            var service = new NetworkTrafficService(path);
            service.SetEnabled(NetworkModule.Lounge, false);
            service.ResetCounters();
            var restored = new NetworkTrafficService(path);
            Assert.False(restored.IsEnabled(NetworkModule.Lounge));
            Assert.True(restored.IsEnabled(NetworkModule.Lcu));
            Assert.All(restored.Snapshot(), s => Assert.Equal(0, s.Downloaded));
            restored.SetEnabled(NetworkModule.Lounge, true);
            Assert.True(new NetworkTrafficService(path).IsEnabled(NetworkModule.Lounge));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void FailedSaveDoesNotApplyPolicy()
    {
        string path = Path.GetTempFileName();
        try
        {
            var service = new NetworkTrafficService(Path.Combine(path, "network.json"));
            Assert.ThrowsAny<IOException>(() => service.SetEnabled(NetworkModule.Ai, false));
            Assert.True(service.IsEnabled(NetworkModule.Ai));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("https://127.0.0.1:2999/liveclientdata/allgamedata", NetworkModule.LiveGame)]
    [InlineData("https://127.0.0.1:1234/lol-match-history/v1/games", NetworkModule.Matches)]
    [InlineData("https://127.0.0.1:1234/lol-game-data/assets/v1/items.json", NetworkModule.LocalAssets)]
    [InlineData("https://127.0.0.1:1234/lol-lobby/v2/lobby", NetworkModule.Lcu)]
    [InlineData("https://ddragon.leagueoflegends.com/api/versions.json", NetworkModule.Patch)]
    [InlineData("https://raw.communitydragon.org/latest/image.png", NetworkModule.Assets)]
    [InlineData("https://example.test/resource", NetworkModule.Other)]
    public void SharedRequestsClassifyPublicAndLocalAccess(string url, NetworkModule expected) => Assert.Equal(expected, NetworkTrafficService.Classify(new Uri(url)));

    [Fact]
    public async Task ConcurrentRequestsProduceConsistentTotals()
    {
        var service = new NetworkTrafficService();
        using var client = Client(service, NetworkModule.Ranked, (_, _) => Task.FromResult(Reply("12345")));
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => client.GetStringAsync("https://example.test")));
        Assert.Equal(250, Row(service, NetworkModule.Ranked).Downloaded);
        Assert.Equal(50, Row(service, NetworkModule.Ranked).Requests);
        service.ResetCounters();
        Assert.Equal(0, Row(service, NetworkModule.Ranked).Requests);
        Assert.Equal(0, Row(service, NetworkModule.Ranked).Downloaded);
    }
    private static TrafficSnapshot Row(NetworkTrafficService service, NetworkModule module) => service.Snapshot().Single(s => s.Info.Module == module);
    private static HttpResponseMessage Reply(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpClient Client(NetworkTrafficService service, NetworkModule module, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new NetworkTrafficService.TrafficHandler(service, module, new StubHandler(send)));
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
