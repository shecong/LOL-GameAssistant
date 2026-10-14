using System.Reflection;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ChampSelectCompanionTests
{
    [Fact]
    public void WindowEnumerationFindsVisibleClientWithoutUsingMainWindowHandle() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var hidden = new Form { Size = new Size(1400, 900), ShowInTaskbar = false, Opacity = 0 };
        using var client = new Form { Size = new Size(900, 600), ShowInTaskbar = false, Opacity = 0 };
        using var auxiliary = new Form { Size = new Size(100, 100), ShowInTaskbar = false, Opacity = 0 };
        hidden.Show(); hidden.Hide(); client.Show(); auxiliary.Show();
        var ids = new HashSet<int> { Environment.ProcessId };
        Assert.True(ChampSelectCompanionForm.TryFindClientWindow(ids, client.Handle, out var handle, out var bounds));
        Assert.Equal(client.Handle, handle);
        Assert.Equal(client.Bounds, bounds);
        client.Hide();
        // 全套测试可能同时存在本进程的其他窗体；隐藏窗口和小型辅助窗口均不能被选中。
        if (ChampSelectCompanionForm.TryFindClientWindow(ids, auxiliary.Handle, out handle, out _))
        {
            Assert.NotEqual(client.Handle, handle);
            Assert.NotEqual(hidden.Handle, handle);
            Assert.NotEqual(auxiliary.Handle, handle);
        }
        client.Show();
        Assert.True(ChampSelectCompanionForm.TryFindClientWindow(ids, client.Handle, out handle, out _));
        Assert.Equal(client.Handle, handle);
        Assert.False(ChampSelectCompanionForm.TryFindClientWindow(new HashSet<int>(), client.Handle, out _, out _));
    });

    [Fact]
    public void WindowEnumerationPrefersForegroundClientAndRejectsMinimizedClient() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var large = new Form { Size = new Size(1200, 800), ShowInTaskbar = false, Opacity = 0 };
        using var active = new Form { Size = new Size(800, 500), ShowInTaskbar = false, Opacity = 0 };
        large.Show(); active.Show();
        var ids = new HashSet<int> { Environment.ProcessId };
        Assert.True(ChampSelectCompanionForm.TryFindClientWindow(ids, active.Handle, out var handle, out _));
        Assert.Equal(active.Handle, handle);
        active.WindowState = FormWindowState.Minimized;
        Assert.True(ChampSelectCompanionForm.TryFindClientWindow(ids, active.Handle, out handle, out _));
        Assert.NotEqual(active.Handle, handle);
        Assert.True(ChampSelectCompanionForm.TryGetVisibleClientBounds(handle, out _));
    });

    [Theory]
    [InlineData(450, "")]
    [InlineData(0, "ARAM")]
    [InlineData(2400, "KIWI")]
    public void AramBenchRemainsAvailableAfterModeResolvesAndSupportsSwap(int queueId, string mode) => MatchListScrollingTests.OnUiThread(() =>
    {
        int swappedChampion = 0;
        using var fixture = new Fixture(featureHandler: (_, args) =>
        {
            swappedChampion = (int)args[0]!;
            return Task.FromResult(new ClientFeatureResult(true, "已交换"));
        });
        fixture.Lobby.SetResult(new LobbySnapshot { QueueId = queueId, GameMode = mode });
        fixture.Game.SetResult(null);
        fixture.Selection.SetResult(Snapshot(22, 99));
        fixture.Form.StartTracking();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal([22, 99], BenchIds(fixture.Form));
        var button = Children(fixture.Form).OfType<Button>().Single(button => button.Tag is 22);
        // PerformClick requires a visible form; invoke the same click event without needing the game client.
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
        Assert.Equal(22, swappedChampion);
        fixture.Form.ObserveChampionSelection(Snapshot(99));
        Assert.Equal([99], BenchIds(fixture.Form));
    });

    [Fact]
    public void ClassicModeDoesNotShowBenchChoices() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Lobby.SetResult(new LobbySnapshot { QueueId = 430, GameMode = "CLASSIC" });
        fixture.Game.SetResult(null);
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(22, 99));
        System.Windows.Forms.Application.DoEvents();
        Assert.Empty(BenchIds(fixture.Form));
    });

    [Fact]
    public void BenchEventsRenderWhileModeAndPlayerRequestsRemainPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(777));
        Assert.Equal([777], BenchIds(fixture.Form));
        Assert.False(fixture.Lobby.Task.IsCompleted);
        Assert.False(fixture.Player.Task.IsCompleted);

        fixture.Form.ObserveChampionSelection(Snapshot(22, 99));
        Assert.Equal([22, 99], BenchIds(fixture.Form));
        fixture.Form.ObserveChampionSelection(Snapshot());
        Assert.Empty(BenchIds(fixture.Form));
    });

    [Fact]
    public void CompanionHidesOnClientMinimizeAndReturnsOnRestoreWhileDataPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new Form { Size = new Size(900, 600), Opacity = 0, ShowInTaskbar = false };
        client.Show();
        using var fixture = new Fixture(() => ChampSelectCompanionForm.TryGetVisibleClientBounds(client.Handle, out var bounds)
            ? bounds : null);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.SyncClientVisibility());
        Assert.True(fixture.Form.Visible);
        client.WindowState = FormWindowState.Minimized;
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
        client.WindowState = FormWindowState.Normal;
        Assert.True(fixture.Form.SyncClientVisibility());
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Lobby.Task.IsCompleted);
        Assert.False(fixture.Player.Task.IsCompleted);
    });

    [Fact]
    public void HiddenClientHidesCompanionAndStoppedTrackingDoesNotReshowIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new Form { Size = new Size(900, 600), Opacity = 0, ShowInTaskbar = false };
        client.Show();
        using var fixture = new Fixture(() => ChampSelectCompanionForm.TryGetVisibleClientBounds(client.Handle, out var bounds)
            ? bounds : null);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.Visible);
        client.Hide();
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
        fixture.Form.StopTracking();
        client.Show();
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
    });

    [Fact]
    public void OlderHttpResponseDoesNotOverwriteNewerBenchEvent() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(777));
        fixture.Selection.SetResult(Snapshot(22));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal([777], BenchIds(fixture.Form));
        fixture.Form.StopTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(99));
        Assert.Empty(BenchIds(fixture.Form));
    });

    [Fact]
    public void CompanionIsTopMostOnlyWhileClientIsForegroundEvenWhenDataPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        bool focused = false;
        using var fixture = new Fixture(() => new Rectangle(100, 100, 900, 600), clientForeground: () => focused);
        Assert.False(fixture.Form.TopMost);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Form.TopMost);
        focused = true;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.TopMost);
        focused = false;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Player.Task.IsCompleted);
        focused = true;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.TopMost);
        fixture.Form.StopTracking();
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Form.Visible);
    });

    [Fact]
    public void HiddenClientAndOpenBuildPickerSuppressTopMost() => MatchListScrollingTests.OnUiThread(() =>
    {
        Rectangle? bounds = new Rectangle(100, 100, 900, 600);
        var pending = new TaskCompletionSource<string>();
        using var fixture = new Fixture(() => bounds, clientForeground: () => true,
            openBuildPicker: _ => pending.Task);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.TopMost);
        Task opened = fixture.Form.ChooseBuildAsync();
        fixture.Form.SyncClientVisibility();
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Form.Visible);
        pending.SetResult("完成");
        PumpUntil(() => opened.IsCompleted);
        Assert.True(fixture.Form.TopMost);
        bounds = null;
        fixture.Form.SyncClientVisibility();
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Form.Visible);
    });

    [Fact]
    public void BuildEntryCoalescesClicksAndDoesNotFollowClientWhileOpen() => MatchListScrollingTests.OnUiThread(() =>
    {
        int opens = 0;
        Rectangle bounds = new(100, 100, 900, 600);
        var pending = new TaskCompletionSource<string>();
        using var fixture = new Fixture(() => bounds, openBuildPicker: _ => { opens++; return pending.Task; });
        fixture.Form.StartTracking();
        Rectangle initial = fixture.Form.Bounds;
        Task first = fixture.Form.ChooseBuildAsync();
        Assert.Same(first, fixture.Form.ChooseBuildAsync());
        Assert.Equal(1, opens);
        bounds = new Rectangle(400, 200, 700, 500);
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.Equal(initial, fixture.Form.Bounds);
        Assert.False(fixture.Form.Visible);
        pending.SetResult("已应用");
        PumpUntil(() => first.IsCompleted);
        Assert.True(fixture.Form.Visible);
        Assert.NotEqual(initial, fixture.Form.Bounds);
    });

    [Fact]
    public void PreviousSelectionCannotReleaseOrOverwriteNewBuildEntry() => MatchListScrollingTests.OnUiThread(() =>
    {
        var requests = new Queue<TaskCompletionSource<string>>();
        var old = new TaskCompletionSource<string>();
        var current = new TaskCompletionSource<string>();
        requests.Enqueue(old);
        requests.Enqueue(current);
        CancellationToken oldToken = default;
        using var fixture = new Fixture(() => new Rectangle(100, 100, 900, 600),
            openBuildPicker: token => { if (oldToken == default) oldToken = token; return requests.Dequeue().Task; });
        fixture.Form.StartTracking();
        Task first = fixture.Form.ChooseBuildAsync();
        fixture.Form.StopTracking();
        Assert.True(oldToken.IsCancellationRequested);
        fixture.Form.StartTracking();
        Task second = fixture.Form.ChooseBuildAsync();
        old.SetResult("旧结果");
        PumpUntil(() => first.IsCompleted);
        Assert.Same(second, fixture.Form.ChooseBuildAsync());
        Assert.False(fixture.Form.Visible);
        current.SetResult("新结果");
        PumpUntil(() => second.IsCompleted);
        Assert.True(fixture.Form.Visible);
        Assert.Equal("新结果", Children(fixture.Form).OfType<Label>().Single(label => label.Text == "新结果").Text);
    });

    [Fact]
    public void FailedBuildEntryCanBeRetriedAndStoppedEntryStaysHidden() => MatchListScrollingTests.OnUiThread(() =>
    {
        int opens = 0;
        var pending = new TaskCompletionSource<string>();
        using var fixture = new Fixture(() => new Rectangle(100, 100, 900, 600),
            openBuildPicker: _ => ++opens == 1 ? Task.FromException<string>(new InvalidOperationException("读取失败")) : pending.Task);
        fixture.Form.StartTracking();
        fixture.Form.ChooseBuildAsync().GetAwaiter().GetResult();
        Assert.True(fixture.Form.Visible);
        Task second = fixture.Form.ChooseBuildAsync();
        Assert.Equal(2, opens);
        fixture.Form.StopTracking();
        pending.SetResult("已取消");
        PumpUntil(() => second.IsCompleted);
        Assert.False(fixture.Form.Visible);
    });

    private static void PumpUntil(Func<bool> complete)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!complete() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(complete());
    }

    private static ChampionSelectionSnapshot Snapshot(params int[] ids) => new()
    {
        BenchChampionIds = ids,
        LocalPlayerCellId = 0,
        MyTeam = [new ChampionSelectionMember { CellId = 1, Puuid = "ally" }]
    };

    private static int[] BenchIds(Control control) => Children(control)
        .OfType<Button>().Where(button => button.Tag is int).Select(button => (int)button.Tag!).ToArray();

    private static IEnumerable<Control> Children(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));

    internal sealed class Fixture : IDisposable
    {
        public TaskCompletionSource<ChampionSelectionSnapshot?> Selection { get; } = new();
        public TaskCompletionSource<LobbySnapshot?> Lobby { get; } = new();
        public TaskCompletionSource<ActiveGameSnapshot?> Game { get; } = new();
        public TaskCompletionSource<PlayerProfile?> Player { get; } = new();
        public ChampSelectCompanionForm Form { get; }

        public Fixture(Func<Rectangle?>? clientBounds = null, Func<MethodInfo, object?[], object?>? featureHandler = null,
            Func<bool>? clientForeground = null, Func<CancellationToken, Task<string>>? openBuildPicker = null)
        {
            Form = new ChampSelectCompanionForm(
                Service<IChampionSelectService>((_, args) => Selection.Task.WaitAsync(Token(args))),
                Service<ILobbyService>((method, args) => method.Name == "GetLobbyAsync"
                    ? Lobby.Task.WaitAsync(Token(args)) : Game.Task.WaitAsync(Token(args))),
                Service<IClientFeatureService>(featureHandler ?? ((_, _) => null)),
                Service<IChampionCatalog>((_, args) => $"英雄 {args[0]}"),
                Service<IPlayerProfileService>((_, _) => Player.Task),
                Service<IRankedStatsService>((_, _) => null),
                Service<IMatchHistoryService>((_, _) => null),
                Service<IApplicationSettingsStore>((_, _) => new AssistantSettings()), clientBounds, clientForeground, openBuildPicker);
        }

        public void Dispose()
        {
            Form.Dispose();
            Player.TrySetResult(null);
            System.Windows.Forms.Application.DoEvents();
        }

        private static CancellationToken Token(object?[] args) => args.OfType<CancellationToken>().FirstOrDefault();
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
