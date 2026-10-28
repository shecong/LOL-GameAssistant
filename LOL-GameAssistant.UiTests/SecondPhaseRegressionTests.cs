using System.Net;
using System.Reflection;
using System.Text;
using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Friends;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Files;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class SecondPhaseRegressionTests
{
    [Fact]
    public async Task CancelledBatchWaitsForUncooperativeWorkerAndDisposesItsStream()
    {
        using var cancellation = new CancellationTokenSource();
        var completed = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new TrackedStream();
        bool queuedRan = false;
        var batch = HttpClientHelper.SendBatchCoreAsync([
            _ => completed.Task,
            _ => { queuedRan = true; return Task.FromResult<Stream?>(null); }
        ], 1, cancellation.Token);
        cancellation.Cancel();
        Assert.False(batch.IsCompleted);
        completed.SetResult(stream);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch);
        Assert.True(stream.WasDisposed);
        Assert.False(queuedRan);
    }

    [Fact]
    public async Task FailedBatchDisposesSuccessfulResultsAndWaitsForAllWorkers()
    {
        var late = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TrackedStream();
        var last = new TrackedStream();
        var batch = HttpClientHelper.SendBatchCoreAsync([
            _ => Task.FromResult<Stream?>(first), _ => failure.Task, _ => late.Task
        ], 3, default);
        failure.SetException(new IOException("test transport failure"));
        Assert.False(batch.IsCompleted);
        late.SetResult(last);
        await Assert.ThrowsAsync<IOException>(() => batch);
        Assert.True(first.WasDisposed);
        Assert.True(last.WasDisposed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task SuccessfulBatchPreservesOrderAndLeavesStreamOwnershipToCaller(int concurrency)
    {
        int active = 0, peak = 0;
        var operations = Enumerable.Range(0, 12).Select<int, Func<CancellationToken, Task<Stream?>>>(i => async token =>
        {
            int count = Interlocked.Increment(ref active);
            int seen;
            do { seen = peak; } while (count > seen && Interlocked.CompareExchange(ref peak, count, seen) != seen);
            await Task.Delay(10, token);
            Interlocked.Decrement(ref active);
            return new MemoryStream([(byte)i]);
        });
        var streams = await HttpClientHelper.SendBatchCoreAsync(operations, concurrency, default);
        Assert.InRange(peak, 1, concurrency);
        for (int i = 0; i < streams.Count; i++) { Assert.Equal(i, streams[i]!.ReadByte()); streams[i]!.Dispose(); }
    }

    [Theory]
    [InlineData(LcuHttpFailure.NoCredentials, LcuFailure.NoCredentials)]
    [InlineData(LcuHttpFailure.Timeout, LcuFailure.Timeout)]
    [InlineData(LcuHttpFailure.Network, LcuFailure.Network)]
    [InlineData(LcuHttpFailure.HttpStatus, LcuFailure.HttpStatus)]
    public async Task TypedTransportRetainsFailureAndReleasesBody(LcuHttpFailure transport, LcuFailure application)
    {
        var body = new TrackedStream();
        var sender = new LcuHttpRequestSender((_, _, _, _) => Task.FromResult(new LcuHttpResult(body, HttpStatusCode.BadGateway, transport)));
        var response = await sender.SendAsync("GET", "/test");
        Assert.False(response.Succeeded);
        Assert.Equal(application, response.Failure);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.True(body.WasDisposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{invalid")]
    public async Task SuccessfulTransportWithMalformedJsonIsClassified(string text)
    {
        var sender = new LcuHttpRequestSender((_, _, _, _) => Task.FromResult(new LcuHttpResult(new MemoryStream(Encoding.UTF8.GetBytes(text)), HttpStatusCode.OK, LcuHttpFailure.None)));
        var response = await sender.GetJsonAsync<PlayerProfile>("/test");
        Assert.Equal(LcuFailure.InvalidJson, response.Failure);
        Assert.False(response.Succeeded);
    }

    [Fact]
    public async Task EmptySuccessfulCommandRemainsSuccessful()
    {
        var sender = new LcuHttpRequestSender((_, _, _, _) => Task.FromResult(new LcuHttpResult(null, HttpStatusCode.NoContent, LcuHttpFailure.None)));
        Assert.True(await sender.PostAsync("/test", "{}"));
    }

    [Fact]
    public async Task FailedIconDownloadIsRetriedThenSuccessfulBytesAreShared()
    {
        int calls = 0;
        var cache = new AsyncByteCache(_ => Task.FromResult<byte[]?>(++calls == 1 ? null : [42]));
        Assert.Null(await cache.GetAsync("icon"));
        var bytes = await cache.GetAsync("icon");
        Assert.Same(bytes, await cache.GetAsync("icon"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancelledIconWaiterDoesNotCancelSharedDownload()
    {
        int calls = 0;
        var download = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new AsyncByteCache(_ => { calls++; return download.Task; });
        using var cts = new CancellationTokenSource();
        var cancelled = cache.GetAsync("icon", cts.Token);
        var waiting = cache.GetAsync("icon");
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        download.SetResult([1, 2]);
        Assert.Equal(new byte[] { 1, 2 }, await waiting);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void OlderRequestCannotClearNewRequestAndClosingCancelsBoth()
    {
        using var scope = new UiRequestScope();
        var first = scope.Begin();
        using var second = scope.Begin();
        Assert.True(first.Token.IsCancellationRequested);
        first.Dispose();
        Assert.True(second.IsCurrent);
        scope.Dispose();
        Assert.True(second.Token.IsCancellationRequested);
        Assert.False(second.IsCurrent);
        Assert.Throws<ObjectDisposedException>(() => scope.Begin());
    }

    [Fact]
    public void HomeIgnoresOldProfileEvenWhenProviderIgnoresCancellation() => MatchListScrollingTests.OnUiThread(() =>
    {
        var old = new TaskCompletionSource<PlayerProfile?>();
        var current = new TaskCompletionSource<PlayerProfile?>();
        CancellationToken oldToken = default;
        int calls = 0;
        var profiles = Service<IPlayerProfileService>((m, args) =>
        {
            if (++calls == 1) { oldToken = (CancellationToken)args[^1]!; return old.Task; }
            return current.Task;
        });
        using var home = CreateHome(profiles);
        home.CreateControl();
        var a = home.LoadPlayerAsync("old");
        PumpUntil(() => calls == 1);
        var b = home.LoadPlayerAsync("new");
        PumpUntil(() => calls == 2);
        Assert.True(oldToken.IsCancellationRequested);
        current.SetResult(Player("new"));
        Pump(b);
        old.SetResult(Player("old"));
        Pump(a);
        Assert.Equal("new", Field<AntdUI.Label>(home, "play_name").Text);
    });

    [Fact]
    public void HomeCloseCancelsPendingProfileAndDoesNotContinueAssetRequests() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<PlayerProfile?>();
        CancellationToken token = default;
        int assets = 0;
        using var home = CreateHome(Service<IPlayerProfileService>((_, args) => { token = (CancellationToken)args[^1]!; return pending.Task; }),
            Service<IProfileIconService>((_, _) => { assets++; return Task.FromResult<byte[]?>(null); }));
        var load = home.LoadPlayerAsync("pending");
        PumpUntil(() => token.CanBeCanceled);
        home.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(Player("pending"));
        Pump(load);
        Assert.Equal(0, assets);
    });

    [Fact]
    public void FriendsCloseCancelsPendingDirectoryRequest() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FriendProfile>>();
        CancellationToken token = default;
        using var friends = new FriendsForm(Service<IFriendDirectoryService>((_, args) => { token = (CancellationToken)args[0]!; return pending.Task; }),
            Service<IFriendSpectateService>((_, _) => throw new InvalidOperationException()), Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)));
        var refresh = friends.RefreshAsync();
        friends.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult([]);
        Pump(refresh);
    });

    [Fact]
    public async Task CancelledExportPreservesExistingFileAndSuccessfulExportUsesBom()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lol-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "test.csv");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var service = new TextExportService();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveUtf8WithBomAsync(path, "replacement", cts.Token));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            await service.SaveUtf8WithBomAsync(path, "中文");
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, (await File.ReadAllBytesAsync(path)).Take(3));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    [Fact]
    public void HomePagingDisposesReplacedCardsAndOnlyAttachesLatestPage() => MatchListScrollingTests.OnUiThread(() =>
    {
        var first = new TaskCompletionSource<MatchDetail?>();
        var second = new TaskCompletionSource<MatchDetail?>();
        var cards = new List<RecordForm>();
        var matches = Service<IMatchHistoryService>((_, args) => (long)args[0]! == 2 ? first.Task : second.Task);
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        using var home = CreateHome(matches: matches, recordFactory: () => { var card = new RecordForm(matches, assets); cards.Add(card); return card; });
        typeof(HomeForm).GetField("matchlists", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home,
            new MatchHistoryResponse { Games = new() { Games = [new() { GameId = 2, GameCreation = 2 }, new() { GameId = 1, GameCreation = 1 }] } });
        Field<AntdUI.InputNumber>(home, "game_count").Text = "1";
        Task Page(int page) => (Task)typeof(HomeForm).GetMethod("GetGameInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, [Player("player"), page])!;
        var old = Page(1);
        var current = Page(2);
        second.SetResult(new MatchDetail());
        Pump(current);
        first.SetResult(new MatchDetail());
        Pump(old);
        Assert.True(cards[0].IsDisposed);
        Assert.Same(cards[1], Assert.Single(Field<Control>(home, "stackPanel1").Controls.Cast<Control>()));
    });

    [Fact]
    public void SettingsCloseCancelsLauncherAndIgnoresLateCompletion() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<GameClientLaunchResult>();
        CancellationToken token = default;
        using var settings = new SettingForm(Service<IGameClientLauncher>((m, args) =>
            { if (m.Name != "StartAndVerifyAsync") return ""; token = (CancellationToken)args[^1]!; return pending.Task; }),
            Service<IApplicationSettingsStore>((m, _) => m.Name == "GetStoragePath" ? "test-settings.json" : new AssistantSettings()), Service<ISettingsSecretProtector>((_, _) => ""),
            Service<IClientFeatureService>((_, _) => null));
        var start = (Task)typeof(SettingForm).GetMethod("StartLeagueClientFromSettingsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null)!;
        settings.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(new(false, "late completion"));
        Pump(start);
    });

    [Fact]
    public void DetailCloseCancelsPendingAssetRequest() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<GameAsset?>();
        CancellationToken token = default;
        var assets = Service<IGameAssetService>((m, args) =>
        {
            if (m.Name == "GetItemIconAsync") { token = (CancellationToken)args[^1]!; return pending.Task; }
            return Task.FromResult<string?>(null);
        });
        using var form = new MatchDetailForm(new(), "player", assets);
        using var box = new AntdUI.Avatar();
        form.Controls.Add(box);
        var load = (Task)typeof(MatchDetailForm).GetMethod("LoadItemIconAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [box, 1])!;
        form.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(null);
        Pump(load);
    });

    internal static HomeForm CreateHome(IPlayerProfileService? profiles = null, IProfileIconService? icons = null,
        IMatchHistoryService? matches = null, Func<RecordForm>? recordFactory = null) => new(
        Service<InfoMsgForm.IInfoMsgForm>((_, _) => null),
        profiles ?? Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(null)),
        icons ?? Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
        Service<ILeagueClientConnection>((_, _) => true),
        matches ?? Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(null)),
        Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)),
        Service<IGameDataVersionService>((_, _) => Task.CompletedTask), recordFactory);
    internal static PlayerProfile Player(string name) => new(name, name, "test", 0, 30, 10, 100);
    internal static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    internal static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    internal static void Pump(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    internal static void PumpUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(() => { System.Windows.Forms.Application.DoEvents(); return condition(); }, TimeSpan.FromSeconds(10)), "UI operation timed out");
    private sealed class TrackedStream : MemoryStream
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
