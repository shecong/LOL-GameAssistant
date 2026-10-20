using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class StartupReliabilityTests
{
    [Fact]
    public async Task StuckCredentialLookupTimesOutAndRepeatedRequestsDoNotCreateMoreScanners()
    {
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var probe = new LcuCredentialProbe(() =>
        {
            Interlocked.Increment(ref calls);
            release.Wait();
            return ("12345", "private-value");
        }, TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(10));
        try
        {
            var watch = Stopwatch.StartNew();
            var first = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => probe.GetAsync()));
            Assert.All(first, value => Assert.Null(value.Port));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
            Assert.Equal(1, calls);
            Assert.Null((await probe.GetAsync()).Port);
            Assert.Equal(1, calls);
            release.Set();
            var result = await probe.GetAsync();
            Assert.Equal("12345", result.Port);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task CancellingOneCredentialWaitDoesNotCancelOtherWaiters()
    {
        using var release = new ManualResetEventSlim();
        var probe = new LcuCredentialProbe(() => { release.Wait(); return ("12345", "private-value"); }, TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var cancelled = probe.GetAsync(cancellation.Token);
            var waiting = probe.GetAsync();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            release.Set();
            Assert.Equal("12345", (await waiting).Port);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void PendingExternalResourceVersionDoesNotBlockHomeProfileAndHistory() => MatchListScrollingTests.OnUiThread(() =>
    {
        var version = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int historyRequests = 0;
        using var home = new HomeForm(Service<InfoMsgForm.IInfoMsgForm>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => Task.FromResult(Player("startup-self"))),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
            Service<ILeagueClientConnection>((_, _) => true),
            Service<IMatchHistoryService>((_, _) => { historyRequests++; return Task.FromResult<MatchHistoryResponse?>(null); }),
            Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)),
            Service<IGameDataVersionService>((_, _) => version.Task));
        try
        {
            var loading = home.LoadPlayerAsync(null);
            Pump(loading);
            Assert.False(version.Task.IsCompleted);
            Assert.Equal("startup-self", home.userinfo!.Puuid);
            Assert.Equal(1, historyRequests);
        }
        finally { version.TrySetResult(); }
    });

    [Fact]
    public async Task PublicResourceRequestDoesNotRequireLcuCredentials()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            string? header;
            while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(timeout.Token)))
                Assert.False(header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]"), timeout.Token);
        });
        using var http = new HttpClientHelper();
        using var result = await http.GetAsync($"http://127.0.0.1:{port}/versions.json", cancellationToken: timeout.Token);
        Assert.NotNull(result);
        Assert.Equal("[]", await new StreamReader(result!).ReadToEndAsync(timeout.Token));
        await serving;
    }

    [Fact]
    public void StartupLogRecordsBothEntryAndElapsedCompletion()
    {
        using (new StartupDiagnostics("启动诊断测试"))
            Assert.Equal("开始", Assert.Single(RuntimeDiagnostics.Snapshot(), e => e.Component == "启动 · 启动诊断测试").Status);
        var complete = Assert.Single(RuntimeDiagnostics.Snapshot(), e => e.Component == "启动 · 启动诊断测试");
        Assert.Equal("结束", complete.Status);
        Assert.Contains("ms", complete.Detail);
    }
}
