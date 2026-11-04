using System.Diagnostics;
using System.Reflection;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Skins;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using Xunit;
using Xunit.Abstractions;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UiResponsivenessTests(ITestOutputHelper output) : IDisposable
{
    private readonly bool _previousDiagnosticsEnabled = RuntimeDiagnostics.Enabled;
    public void Dispose() => RuntimeDiagnostics.SetEnabled(_previousDiagnosticsEnabled);
    [Fact]
    public async Task SlowOverlayLoaderDoesNotBlockUiBridgeCallsOrDisposal()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var bridge = new NativeGameOverlayBridge(false) { StartProcess = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            throw new InvalidOperationException("simulated loader failure");
        }};
        var startup = Task.Run(() => bridge.StartLoader(new ProcessStartInfo()));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var uiWork = Task.Run(() =>
            {
                bridge.PublishPanel(new(GameOverlayPanelKind.Camera));
                bridge.ReadCameraWheel();
                bridge.HidePanel(GameOverlayPanelKind.Camera);
                bridge.Dispose();
            });
            await uiWork.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => startup);
    }

    [Fact]
    public void HiddenDiagnosticsWaitsUntilPageIsSelectedBeforeRebindingTable() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ShowInTaskbar = false };
        using var tabs = new AntdUI.Tabs { Dock = DockStyle.Fill };
        var first = new AntdUI.TabPage { Text = "首页" }; var second = new AntdUI.TabPage { Text = "诊断" };
        var settings = new AssistantSettings { RuntimeDiagnosticsEnabled = true };
        var store = SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Settings.IApplicationSettingsStore>((method, _) =>
            method.Name == "Load" ? settings : null);
        using var diagnostics = new DiagnosticsForm(store);
        second.Controls.Add(diagnostics); tabs.Controls.Add(first); tabs.Pages.Add(first);
        tabs.Controls.Add(second); tabs.Pages.Add(second); host.Controls.Add(tabs); host.Show();
        tabs.SelectedIndex = 0;
        var table = diagnostics.Controls.OfType<AntdUI.Table>().Single();
        var before = table.DataSource;
        RuntimeDiagnostics.Report("隐藏诊断性能测试", "已变化", Guid.NewGuid().ToString());
        System.Windows.Forms.Application.DoEvents();
        Assert.Same(before, table.DataSource);
        tabs.SelectedIndex = 1;
        SecondPhaseRegressionTests.PumpUntil(() => !ReferenceEquals(before, table.DataSource));
    });

    [Fact]
    public void ForegroundPollingReusesIdentityButFocusChangesImmediately()
    {
        long now = 100;
        int reads = 0;
        var cache = new ForegroundProcessNameCache(pid => { reads++; return pid == 1 ? "League of Legends" : "other"; }, () => now);
        for (int tick = 0; tick < 100; tick++) Assert.Equal("League of Legends", cache.Get(1));
        Assert.Equal(1, reads);
        Assert.Equal("other", cache.Get(2));
        Assert.Equal(2, reads);
        Assert.Equal("League of Legends", cache.Get(1));
        now += 1000;
        Assert.Equal("League of Legends", cache.Get(1));
        Assert.Equal(4, reads);
    }

    [Fact]
    public void FailedProcessLookupDoesNotReusePreviousGameIdentityAndCanRecover()
    {
        long now = 0;
        bool fail = false;
        var cache = new ForegroundProcessNameCache(_ => fail ? throw new InvalidOperationException() : "League of Legends", () => now);
        Assert.Equal("League of Legends", cache.Get(1));
        fail = true;
        Assert.Null(cache.Get(2));
        fail = false;
        now = 250;
        Assert.Equal("League of Legends", cache.Get(2));
    }

    [Fact]
    public void ThemePaletteIsReusedAcrossPaintsAndThemeChangesStillApply() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode("Light"); var light = UiTheme.Palette;
            Assert.Same(light, UiTheme.Palette);
            UiTheme.SetMode("Dark"); Assert.True(UiTheme.Palette.IsDark);
            Assert.NotSame(light, UiTheme.Palette);
            UiTheme.SetMode("Light"); Assert.Same(light, UiTheme.Palette);
            UiTheme.SetMode("System"); var system = UiTheme.Palette;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++) _ = UiTheme.Palette;
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            output.WriteLine($"10,000 cached system palette reads: {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated} allocated bytes");
            Assert.True(allocated < 4096);
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void RefreshingNewPageDoesNotWalkUnrelatedControls() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var root = new Panel(); using var page = new Panel();
        var current = new PropertyReadProbe(); var other = new PropertyReadProbe();
        page.Controls.Add(current); root.Controls.Add(page); root.Controls.Add(other);
        UiLanguage.Refresh(root);
        current.Reads = other.Reads = 0;
        UiLanguage.Refresh(page);
        Assert.True(current.Reads > 0);
        Assert.Equal(0, other.Reads);
    });

    [Fact]
    public void SkinCoreStartupAndDiagnosticsRunOutsideUiThread() => MatchListScrollingTests.OnUiThread(() =>
    {
        int uiThread = Environment.CurrentManagedThreadId, diagnosticThread = 0;
        var transport = new NativeSkinCoreTransport(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            diagnostic: _ => diagnosticThread = Environment.CurrentManagedThreadId);
        var error = Assert.ThrowsAsync<SkinCoreException>(() => transport.InvokeAsync(["catalog"], default)).GetAwaiter().GetResult();
        Assert.Equal("core_files_missing", error.Code);
        Assert.NotEqual(0, diagnosticThread);
        Assert.NotEqual(uiThread, diagnosticThread);
    });

    private sealed class PropertyReadProbe : Control
    {
        internal int Reads;
        private string _placeholder = "查询玩家";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string PlaceholderText { get { Reads++; return _placeholder; } set => _placeholder = value; }
    }
}
