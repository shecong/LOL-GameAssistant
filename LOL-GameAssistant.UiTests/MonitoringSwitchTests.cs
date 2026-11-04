using System.Reflection;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class MonitoringSwitchTests : IDisposable
{
    private readonly bool _previousDiagnostics = RuntimeDiagnostics.Enabled;
    public void Dispose() => RuntimeDiagnostics.SetEnabled(_previousDiagnostics);

    [Fact]
    public void MissingSettingsDefaultBothMonitorsOffAndFlagsRoundTrip()
    {
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<SettingConfig>("{}")!;
        var defaults = LegacySettingsMapper.ToDomain(old);
        Assert.False(defaults.TrafficMonitoringEnabled); Assert.False(defaults.RuntimeDiagnosticsEnabled);
        Assert.False(AssistantDefaultSettings.Create().TrafficMonitoringEnabled);
        Assert.False(AssistantDefaultSettings.Create().RuntimeDiagnosticsEnabled);
        defaults.TrafficMonitoringEnabled = defaults.RuntimeDiagnosticsEnabled = true;
        var restored = LegacySettingsMapper.ToDomain(LegacySettingsMapper.ToLegacy(defaults));
        Assert.True(restored.TrafficMonitoringEnabled); Assert.True(restored.RuntimeDiagnosticsEnabled);
    }

    [Fact]
    public void PageSwitchesSaveIndependentlyAndStopTheirTimers() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); var traffic = new NetworkTrafficService();
        using var trafficPage = new TrafficMonitorForm(traffic, settings);
        using var diagnosticsPage = new DiagnosticsForm(settings);
        var trafficSwitch = Descendants(trafficPage).OfType<AntdUI.Switch>().Single();
        var diagnosticsSwitch = Descendants(diagnosticsPage).OfType<AntdUI.Switch>().Single();
        Assert.False(trafficSwitch.Checked); Assert.False(diagnosticsSwitch.Checked);
        Assert.False(Timer(trafficPage).Enabled); Assert.False(Timer(diagnosticsPage).Enabled);
        trafficSwitch.Checked = true;
        Assert.True(traffic.MonitoringEnabled); Assert.True(settings.Load().TrafficMonitoringEnabled);
        Assert.False(settings.Load().RuntimeDiagnosticsEnabled);
        diagnosticsSwitch.Checked = true;
        Assert.True(RuntimeDiagnostics.Enabled); Assert.True(settings.Load().RuntimeDiagnosticsEnabled);
        Assert.True(Timer(trafficPage).Enabled); Assert.True(Timer(diagnosticsPage).Enabled);
        trafficSwitch.Checked = false; diagnosticsSwitch.Checked = false;
        Assert.False(traffic.MonitoringEnabled); Assert.False(RuntimeDiagnostics.Enabled);
        Assert.False(Timer(trafficPage).Enabled); Assert.False(Timer(diagnosticsPage).Enabled);
        Assert.True(traffic.IsEnabled(NetworkModule.Lcu));
        using var reopenedTraffic = new TrafficMonitorForm(traffic, settings);
        using var reopenedDiagnostics = new DiagnosticsForm(settings);
        Assert.False(Descendants(reopenedTraffic).OfType<AntdUI.Switch>().Single().Checked);
        Assert.False(Descendants(reopenedDiagnostics).OfType<AntdUI.Switch>().Single().Checked);
    });

    [Fact]
    public void DisabledDiagnosticsDoesNotPublishCollectOrWriteLog()
    {
        RuntimeDiagnostics.SetEnabled(false);
        string path = RuntimeDiagnostics.GetLogPath();
        long before = File.Exists(path) ? new FileInfo(path).Length : -1;
        string rankedLog = Path.Combine(AppContext.BaseDirectory, "ranked_debug.log");
        long rankedBefore = File.Exists(rankedLog) ? new FileInfo(rankedLog).Length : -1;
        int reports = 0; void Reported(DiagnosticEntry _) => reports++;
        RuntimeDiagnostics.Reported += Reported;
        try
        {
            RuntimeDiagnostics.Report("disabled-test", "test", Guid.NewGuid().ToString());
            RuntimeDiagnostics.WriteException(new InvalidOperationException("disabled diagnostic"));
            typeof(LOL_GameAssistant.LoLApi.Game_Api).GetMethod("WriteRankedDebugLog", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, ["test-player", "{}"]);
            Assert.Empty(RuntimeDiagnostics.Snapshot()); Assert.Equal(0, reports);
            Assert.Equal(before, File.Exists(path) ? new FileInfo(path).Length : -1);
            Assert.Equal(rankedBefore, File.Exists(rankedLog) ? new FileInfo(rankedLog).Length : -1);
        }
        finally { RuntimeDiagnostics.Reported -= Reported; }
    }

    [Fact]
    public void RenderMonitoringPagesWhenRequested() => MatchListScrollingTests.OnUiThread(() =>
    {
        string? directory = Environment.GetEnvironmentVariable("MONITORING_UI_PREVIEWS");
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        string previous = UiTheme.Mode;
        using var host = new Form { ClientSize = new Size(1100, 760), ShowInTaskbar = false };
        try
        {
            foreach (string theme in new[] { "Light", "Dark" })
            foreach (bool diagnostics in new[] { false, true })
            {
                UiTheme.SetMode(theme);
                using Control page = diagnostics ? new DiagnosticsForm(new Settings())
                    : new TrafficMonitorForm(new NetworkTrafficService(), new Settings());
                page.Dock = DockStyle.Fill; host.Controls.Add(page); host.Show();
                UiTheme.Apply(page); System.Windows.Forms.Application.DoEvents();
                var toggle = Descendants(page).OfType<AntdUI.Switch>().Single();
                Assert.False(toggle.Checked);
                Assert.True(toggle.Bounds.Bottom <= toggle.Parent!.ClientSize.Height);
                using var bitmap = new Bitmap(page.Width, page.Height);
                page.DrawToBitmap(bitmap, page.ClientRectangle);
                bitmap.Save(Path.Combine(directory, $"{(diagnostics ? "diagnostics" : "traffic")}-{theme}.png"));
                host.Controls.Remove(page);
            }
        }
        finally { UiTheme.SetMode(previous); }
    });

    private static System.Windows.Forms.Timer Timer(Control page) => (System.Windows.Forms.Timer)page.GetType()
        .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private sealed class Settings : IApplicationSettingsStore
    {
        private AssistantSettings _value = new();
        public AssistantSettings Load() => Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(_value))!;
        public void Save(AssistantSettings settings) => _value = settings;
        public string GetStoragePath() => "test-settings.json";
    }
}
