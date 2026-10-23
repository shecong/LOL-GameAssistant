using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SkinCameraUiTests
{
    private sealed class Settings : IApplicationSettingsStore
    {
        private AssistantSettings _value = new() { SkinCoreEnabled = true };
        public AssistantSettings Load() => _value;
        public void Save(AssistantSettings value) => _value = value;
        public string GetStoragePath() => "unused";
    }
    private sealed class Core : ISkinCoreService
    {
        public bool Enabled { get; private set; }
        public int CameraReads;
        public float Distance = 600;
        public float OriginalDistance = 600;
        public bool RejectWrite;
        public float ViewDistance = 2250;
        public List<float> ViewMoves = [];
        public List<float> Writes = [];
        public void SetEnabled(bool value) => Enabled = value;
        public void InvalidateSession() { }
        private SkinCoreReply State(float distance = 600) => new() { Session = "1234567890abcdef", Model = "Lux", CameraDistance = distance, CameraDefaultViewDistance = 2250,
            StateVerified = true, Entries = [new() { EntryId = "0:-1", Name = "基础皮肤", Model = "Lux" }] };
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) => Task.FromResult(State());
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken token = default) => Task.FromResult(State());
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) => Task.FromResult(State());
        public Task<SkinCoreReply> StopAsync(string session, CancellationToken token = default) => Task.FromResult(State());
        public Task<SkinCoreReply> GetCameraAsync(CancellationToken token = default) { CameraReads++; return Task.FromResult(State(Distance)); }
        public Task<SkinCoreReply> SetCameraAsync(string session, float distance, CancellationToken token = default)
        {
            Writes.Add(distance);
            if (RejectWrite) return Task.FromException<SkinCoreReply>(new SkinCoreException("request_outcome_unknown", true));
            Distance = distance; return Task.FromResult(State(distance));
        }
        public Task<SkinCoreReply> RestoreCameraAsync(string session, CancellationToken token = default)
        { Writes.Add(OriginalDistance); Distance = OriginalDistance; ViewMoves.Add(2250); ViewDistance = 2250;
            return Task.FromResult(State(Distance) with { CameraViewDistance = 2250, CameraViewTarget = 2250 }); }
        public Task<SkinCoreReply> MoveCameraAsync(string session, float distance, CancellationToken token = default)
        {
            ViewMoves.Add(distance); ViewDistance = distance;
            return Task.FromResult(State(Distance) with { CameraViewDistance = distance, CameraViewTarget = distance });
        }
    }
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Theory]
    [InlineData(null)]
    [InlineData(600.25f)]
    [InlineData(1700f)]
    public void CameraPreferencesRoundTripThroughPersistedSettings(float? distance)
    {
        var original = new AssistantSettings { UnlimitedZoomEnabled = true, RememberedCameraDistance = distance };
        var legacy = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToLegacy(original);
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(legacy);
        var restored = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToDomain(
            Newtonsoft.Json.JsonConvert.DeserializeObject<LOL_GameAssistant.Entity.SettingConfig>(json)!);
        Assert.True(restored.UnlimitedZoomEnabled); Assert.Equal(distance, restored.RememberedCameraDistance);
    }

    [Fact]
    public void OtherSettingsFollowsObjectsAndSupportsReadApplyRestore() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            var resources = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
                ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([])
                : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
            var core = new Core();
            using var host = new Form { ClientSize = new Size(1100, 850), ShowInTaskbar = false };
            var settings = new Settings();
            using var overlay = new RecordingGameOverlay();
            using var page = new SkinToolsForm(core, settings, resources, overlay);
            host.Controls.Add(page); host.Show(); page.OnGamePhaseChanged(true);
            AntdUI.Button Button(string name) => Descendants(page).OfType<AntdUI.Button>().Single(c => c.Name == name);
            var navigation = Descendants(page).Single(c => c.Name == "SkinNavigation");
            Assert.Equal(new[] { "HeroSkinTab", "ObjectSkinTab", "RankBorderTab", "OtherSkinSettingsTab" }, navigation.Controls.Cast<Control>().Select(c => c.Name));
            Button("RankBorderTab").PerformClick();
            Assert.True(Descendants(page).Single(c => c.Name == "RankBorderPage").Visible);
            Assert.False(Button("RankBorderApply").Enabled);
            Assert.False(Button("RankBorderRestore").Enabled);
            Button("RankBorderRead").PerformClick();
            Assert.Contains("待适配", Descendants(page).OfType<AntdUI.Label>().Single(c => c.Name == "RankBorderStatus").Text);
            Assert.False(Button("RankBorderApply").Enabled); Assert.Empty(core.Writes);
            using (var borderPreview = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
            {
                host.DrawToBitmap(borderPreview, host.ClientRectangle);
                borderPreview.Save(Path.Combine(AppContext.BaseDirectory, "rank-border-preview.png"));
            }
            Button("OtherSkinSettingsTab").PerformClick();
            Assert.True(Descendants(page).Single(c => c.Name == "OtherSkinSettingsPage").Visible);
            Assert.Equal(0, core.CameraReads); Assert.Empty(core.Writes);
            var toggle = Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "UnlimitedZoomEnabled");
            Assert.True(toggle.Enabled);
            Button("CameraRead").PerformClick(); Assert.Equal(1, core.CameraReads); Assert.True(toggle.Enabled);
            toggle.Checked = true; Assert.Empty(core.Writes);
            var distance = Descendants(page).OfType<AntdUI.Label>().Single(c => c.Name == "UnlimitedZoomDistance");
            Assert.Contains("600", distance.Text);
            page.AdjustCameraWheelAsync(-1200).GetAwaiter().GetResult(); Assert.Equal(780, Assert.Single(core.Writes));
            Assert.Equal(780f, settings.Load().RememberedCameraDistance);
            toggle.Checked = false; Assert.Equal(600, core.Writes.Last());
            foreach (string theme in new[] { "Dark", "Light" })
            {
                UiTheme.SetMode(theme); UiTheme.Apply(host); host.PerformLayout();
                Assert.Equal(AntdUI.TTypeMini.Primary, Button("OtherSkinSettingsTab").Type);
                Assert.Equal(UiTheme.Palette.TextPrimary, Button("ObjectSkinTab").ForeColor);
                Assert.Equal(UiTheme.Palette.TextPrimary, distance.ForeColor);
                var output = Path.Combine(AppContext.BaseDirectory, "camera-preview-" + theme + ".png");
                using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
                host.DrawToBitmap(bitmap, host.ClientRectangle); bitmap.Save(output);
            }
            Assert.Equal(2, core.Writes.Count);
            page.OnGamePhaseChanged(false); Assert.True(toggle.Enabled); Assert.False(toggle.Checked);
            Assert.False(Button("CameraRead").Enabled);
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void PreGameSwitchKeepsDefaultThenWheelDistanceSurvivesNextGameAndRestart() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); settings.Load().SkinCoreEnabled = false;
        var core = new Core();
        var resources = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
            ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([])
            : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
        using var overlay = new RecordingGameOverlay();
        using (var page = new SkinToolsForm(core, settings, resources, overlay))
        {
            page.SetCameraEnabledAsync(true).GetAwaiter().GetResult();
            Assert.True(settings.Load().UnlimitedZoomEnabled); Assert.True(core.Enabled);
            Assert.Equal(0, core.CameraReads); Assert.Empty(core.Writes);
            page.OnGamePhaseChanged(true); page.Visible = false;
            page.PollCameraAsync().GetAwaiter().GetResult();
            Assert.Equal(1, core.CameraReads); Assert.Empty(core.Writes);
            Assert.Contains(LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.Camera, overlay.Panels.Keys);
            page.AdjustCameraWheelAsync(-60).GetAwaiter().GetResult(); Assert.Empty(core.Writes);
            page.AdjustCameraWheelAsync(-60).GetAwaiter().GetResult(); Assert.Equal(618, Assert.Single(core.Writes));
            page.AdjustCameraWheelAsync(120).GetAwaiter().GetResult(); Assert.Equal(600, core.Writes.Last());
            page.AdjustCameraWheelAsync(-12000).GetAwaiter().GetResult(); Assert.Equal(1700, core.Writes.Last());
            page.OnGamePhaseChanged(false);
            Assert.True(settings.Load().UnlimitedZoomEnabled); Assert.Equal(1700f, settings.Load().RememberedCameraDistance);
            Assert.DoesNotContain(LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.Camera, overlay.Panels.Keys);
            core.Distance = 600;
            page.OnGamePhaseChanged(true); page.PollCameraAsync().GetAwaiter().GetResult();
            Assert.Equal(1700, core.Distance);
            page.SetCameraEnabledAsync(false).GetAwaiter().GetResult(); Assert.Equal(600, core.Distance);
            Assert.Equal(1700f, settings.Load().RememberedCameraDistance);
            page.SetCameraEnabledAsync(true).GetAwaiter().GetResult(); Assert.Equal(1700, core.Distance);
        }
        core.Distance = 600;
        using var restarted = new SkinToolsForm(core, settings, resources, overlay);
        restarted.OnGamePhaseChanged(true); restarted.PollCameraAsync().GetAwaiter().GetResult();
        Assert.Equal(1700, core.Distance);
    });

    [Fact]
    public void MiddleButtonRestoresOriginalViewAndLimitWithoutChangingCacheOrSwitch() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings();
        settings.Load().UnlimitedZoomEnabled = true; settings.Load().RememberedCameraDistance = 1200;
        var core = new Core { Distance = 600.25f, OriginalDistance = 600.25f };
        var resources = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
            ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([])
            : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
        using var overlay = new RecordingGameOverlay();
        using var page = new SkinToolsForm(core, settings, resources, overlay);
        page.OnGamePhaseChanged(true); page.PollCameraAsync().GetAwaiter().GetResult();
        Assert.Equal(1200, core.Distance);
        page.AdjustCameraWheelAsync(-60).GetAwaiter().GetResult();
        overlay.CameraResetRequested = true;
        page.PollCameraAsync().GetAwaiter().GetResult();
        Assert.Equal(2250, core.ViewDistance); Assert.Equal(2250, Assert.Single(core.ViewMoves));
        Assert.Equal(600.25f, core.Distance); Assert.Equal(1200f, settings.Load().RememberedCameraDistance);
        Assert.True(settings.Load().UnlimitedZoomEnabled);
        int writes = core.Writes.Count;
        page.PollCameraAsync().GetAwaiter().GetResult();
        page.AdjustCameraWheelAsync(-60).GetAwaiter().GetResult();
        Assert.Equal(writes, core.Writes.Count); Assert.Single(core.ViewMoves);
        page.OnGamePhaseChanged(false); core.Distance = 600.25f;
        page.OnGamePhaseChanged(true); page.PollCameraAsync().GetAwaiter().GetResult();
        Assert.Equal(1200, core.Distance);
        page.SetCameraEnabledAsync(false).GetAwaiter().GetResult();
        Assert.Equal(2250, core.ViewDistance); Assert.Equal(600.25f, core.Distance);
        writes = core.Writes.Count;
        int resets = core.ViewMoves.Count;
        page.RestoreDefaultCameraAsync().GetAwaiter().GetResult(); Assert.Equal(writes, core.Writes.Count); Assert.Equal(resets, core.ViewMoves.Count);
    });

    [Fact]
    public void UnknownWheelWriteDoesNotReplaceCacheOrAutomaticallyRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); settings.Load().UnlimitedZoomEnabled = true;
        var core = new Core();
        var resources = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
            ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([])
            : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
        using var overlay = new RecordingGameOverlay();
        using var page = new SkinToolsForm(core, settings, resources, overlay);
        page.OnGamePhaseChanged(true); page.PollCameraAsync().GetAwaiter().GetResult();
        core.RejectWrite = true;
        page.AdjustCameraWheelAsync(-120).GetAwaiter().GetResult();
        page.PollCameraAsync().GetAwaiter().GetResult();
        page.AdjustCameraWheelAsync(-120).GetAwaiter().GetResult();
        Assert.Single(core.Writes); Assert.Null(settings.Load().RememberedCameraDistance);
        Assert.True(settings.Load().UnlimitedZoomEnabled);
        core.RejectWrite = false;
        page.SetCameraEnabledAsync(false).GetAwaiter().GetResult(); Assert.Equal(600, core.Distance);
    });
}
