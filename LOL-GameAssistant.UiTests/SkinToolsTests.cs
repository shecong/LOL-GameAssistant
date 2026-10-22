using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SkinToolsTests
{
    [Fact]
    public void SkinQualityLabelsAndColorsFollowThemeWithoutApplyingAnotherSkin() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previousMode = UiTheme.Mode;
        try
        {
            UiTheme.SetMode("Light");
            var assets = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
                ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([new(0, "拉克丝", []), new(7, "大元素使 拉克丝", [], SkinQuality.Ultimate)])
                : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
            var core = new Core(); var settings = new Settings(); settings.Load().SkinCoreEnabled = true;
            using var host = new Form { ClientSize = new Size(1100, 850), ShowInTaskbar = false };
            using var page = new SkinToolsForm(core, settings, assets); host.Controls.Add(page); host.Show(); page.OnGamePhaseChanged(true);
            var select = Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries");
            SecondPhaseRegressionTests.PumpUntil(() => select.Items.Count == 2 && select.Items[1] is AntdUI.SelectItem item && item.Text.Contains("终极"));
            Assert.Contains("【基础】", ((AntdUI.SelectItem)select.Items[0]!).Text);
            var ultimate = (AntdUI.SelectItem)select.Items[1]!;
            Assert.Equal("【终极】", ultimate.Text);
            Assert.Contains("大元素使 拉克丝", ultimate.SubText);
            Assert.Contains("烈焰", ultimate.SubText);
            Assert.Equal(SkinQualityStyle.Foreground(SkinQuality.Ultimate, UiTheme.Palette), ultimate.Fore);
            Assert.Equal(UiTheme.Palette.TextPrimary, ultimate.ForeSub);
            select.SelectedIndex = 1;
            Assert.Equal(ultimate.Fore, select.PrefixFore);
            Assert.Equal("【终极】", select.PrefixText);
            Assert.Equal(ultimate.SubText, select.Text);
            Assert.Equal(UiTheme.Palette.TextPrimary, select.ForeColor);
            Assert.Single(core.AppliedEntries);
            UiTheme.SetMode("Dark"); UiTheme.Apply(page);
            Assert.Equal(SkinQualityStyle.Foreground(SkinQuality.Ultimate, UiTheme.Palette), ultimate.Fore);
            Assert.Equal(ultimate.Fore, select.PrefixFore);
            Assert.Equal(UiTheme.Palette.TextPrimary, ultimate.ForeSub);
            Assert.Equal(UiTheme.Palette.TextPrimary, select.ForeColor);
            Assert.Equal(1, select.SelectedIndex); Assert.Single(core.AppliedEntries);
            var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinSearch");
            search.Text = "终极";
            Assert.Single(select.Items); Assert.Contains("大元素使", ((AntdUI.SelectItem)select.Items[0]!).SubText);
            Assert.Single(core.AppliedEntries);
        }
        finally { UiTheme.SetMode(previousMode); }
    });

    private static IChampionSkinResourceService EmptySkinResources() => SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) =>
        method.Name == "GetCatalogAsync" ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([]) : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));

    [Fact]
    public void OverlaySelectionWorksWithMainPageHiddenAndStopsWithCoreOrGame() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings();
        settings.Load().SkinCoreEnabled = true;
        settings.Load().SkinHotkeysEnabled = false;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true); page.Visible = false;
        page.SelectOverlaySkinAsync("1:-1").GetAwaiter().GetResult();
        Assert.Equal("1:-1", Assert.Single(core.AppliedEntries));
        Assert.Equal("1:-1", settings.Load().RememberedHeroSkins["Lux"].EntryId);
        page.OnGamePhaseChanged(false);
        page.SelectOverlaySkinAsync("0:-1").GetAwaiter().GetResult();
        Assert.Single(core.AppliedEntries);
        page.OnGamePhaseChanged(true);
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = false;
        page.SelectOverlaySkinAsync("0:-1").GetAwaiter().GetResult();
        Assert.Single(core.AppliedEntries);
    });

    [Fact]
    public void PickerCommandsApplyOnceAndIgnoreBusyOrStaleSelections() => MatchListScrollingTests.OnUiThread(() =>
    {
        var selected = new List<string>();
        using var bridge = new RecordingGameOverlay();
        using var overlay = new SkinPickerOverlayController(() => true, selected.Add, () => { }, () => { }, bridge);
        overlay.SetState([("a", "基础皮肤"), ("b", "大元素使")], "a", "请选择皮肤", true, true);
        overlay.ShowOverGame(new Rectangle(100,100,1280,720));
        int version = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker].Version;
        bridge.Command = new(1,1,version); overlay.PollCommands(); overlay.PollCommands();
        Assert.Equal("b", Assert.Single(selected));
        overlay.SetState([("a", "基础皮肤"), ("b", "大元素使")], "b", "请求执行中", false, false);
        bridge.Command = new(1,0,version); overlay.PollCommands(); Assert.Single(selected);
        overlay.SetState([("c", "新列表")], "c", "列表已更新", true, true);
        bridge.Command = new(1,0,version); overlay.PollCommands(); Assert.Single(selected);
        overlay.Hide(); Assert.Empty(bridge.Panels);
    });

    [Fact]
    public void HomeCameraSwitchUsesSharedStateAndRejectsBusyAndStaleCommands() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingGameOverlay();
        var changes = new List<bool>();
        using var picker = new SkinPickerOverlayController(() => true, _ => { }, () => { }, () => { }, bridge, changeCamera: changes.Add);
        picker.SetState([], null, "", false, true);
        picker.SetCameraState(false, true); picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        bridge.Command = new(7, 1, panel.Version); picker.PollCommands();
        Assert.True(Assert.Single(changes));
        picker.SetCameraState(true, false);
        bridge.Command = new(7, 0, panel.Version); picker.PollCommands(); Assert.Single(changes);
        picker.SetCameraState(true, true);
        picker.SetState([], null, "", false, true, scope: "new-session");
        bridge.Command = new(7, 0, panel.Version); picker.PollCommands(); Assert.Single(changes);
        var updated = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.True(updated.CameraEnabled);
        bridge.Command = new(7, 0, updated.Version); picker.PollCommands(); Assert.False(changes.Last());
    });

    [Fact]
    public void PickerCategoryAndPagingCommandsRejectOldVersions() => MatchListScrollingTests.OnUiThread(() =>
    {
        var selected = new List<string>(); var categories = new List<SkinPickerCategory>();
        using var bridge = new RecordingGameOverlay();
        using var picker = new SkinPickerOverlayController(() => true, selected.Add, () => { }, () => { }, bridge, categories.Add);
        var entries = Enumerable.Range(0, 530).Select(index => (index.ToString(), "眼位 " + index)).ToArray();
        picker.SetState(entries, null, "", true, true, SkinPickerCategory.Ward, "session"); picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.Equal(1, panel.Category); Assert.Equal(3, panel.PageCount); Assert.Equal(240, panel.Items!.Count);
        bridge.Command = new(6, 1, panel.Version); picker.PollCommands();
        var second = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.Equal(1, second.Page);
        bridge.Command = new(1, 0, panel.Version); picker.PollCommands(); Assert.Empty(selected);
        bridge.Command = new(1, 0, second.Version); picker.PollCommands(); Assert.Equal("240", Assert.Single(selected));
        bridge.Command = new(5, 2, second.Version); picker.PollCommands(); Assert.Equal(SkinPickerCategory.Minion, Assert.Single(categories));
        picker.SetState(entries, null, "", true, true, SkinPickerCategory.Minion, "session");
        bridge.Command = new(1, 0, second.Version); picker.PollCommands(); Assert.Single(selected);
        var minions = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.Equal(0, minions.Page); Assert.NotEqual(second.Version, minions.Version);
        picker.SetState(entries, null, "忙碌", false, false, SkinPickerCategory.Minion, "session");
        bridge.Command = new(5, 0, minions.Version); picker.PollCommands(); Assert.Single(categories);
        picker.SetState(entries, null, "", true, true, SkinPickerCategory.Minion, "another-session");
        bridge.Command = new(1, 0, minions.Version); picker.PollCommands(); Assert.Single(selected);
    });

    [Fact]
    public void PickerQualityStylesFollowPaginationAndClearForObjectCategories() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingGameOverlay();
        using var picker = new SkinPickerOverlayController(() => true, _ => { }, () => { }, () => { }, bridge);
        var items = Enumerable.Range(0, 241).Select(i => (i.ToString(), "【终极】皮肤" + i)).ToArray();
        var styles = items.Select(_ => new LOL_GameAssistant.Application.GameData.GameOverlayItemStyle("【终极】", Color.Gold)).ToArray();
        picker.SetState(items, null, "", true, true, styles: styles); picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.Equal(240, panel.ItemStyles!.Count);
        bridge.Command = new(6, 1, panel.Version); picker.PollCommands();
        panel = bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker];
        Assert.Single(panel.Items!); Assert.Equal(styles[^1], Assert.Single(panel.ItemStyles!));
        picker.SetState([("1", "眼位")], null, "", true, true, SkinPickerCategory.Ward);
        Assert.Empty(bridge.Panels[LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.SkinPicker].ItemStyles!);
    });

    [Fact]
    public void HomeObjectSelectionWorksWhenPageIsHiddenAndPreservesOtherAppliedCategory() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); settings.Load().SkinCoreEnabled = true;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources()); page.OnGamePhaseChanged(true); page.Visible = false;
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Ward).GetAwaiter().GetResult();
        Assert.Equal(1, core.ObjectReads);
        page.SelectOverlayEntryAsync("19").GetAwaiter().GetResult(); Assert.Equal((-1, 19), core.AppliedObjects.Last());
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Minion).GetAwaiter().GetResult();
        page.SelectOverlayEntryAsync("3").GetAwaiter().GetResult(); Assert.Equal((3, 19), core.AppliedObjects.Last());
        page.RefreshOverlayCategoryAsync().GetAwaiter().GetResult();
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Ward).GetAwaiter().GetResult();
        page.SelectOverlayEntryAsync("5").GetAwaiter().GetResult(); Assert.Equal((3, 5), core.AppliedObjects.Last());
        Assert.Empty(core.AppliedEntries);
        page.SelectOverlayEntryAsync("999").GetAwaiter().GetResult(); Assert.Equal(3, core.AppliedObjects.Count);
        core.ObjectRestoreTargets = 0;
        page.RestoreOverlayCategoryAsync().GetAwaiter().GetResult(); Assert.Equal(1, core.ObjectRestores);
        page.SelectOverlayEntryAsync("19").GetAwaiter().GetResult(); Assert.Equal((-1, 19), core.AppliedObjects.Last());
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Hero).GetAwaiter().GetResult();
        page.SelectOverlayEntryAsync("1:-1").GetAwaiter().GetResult(); Assert.Single(core.AppliedEntries);
        page.OnGamePhaseChanged(false);
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Minion).GetAwaiter().GetResult();
        page.SelectOverlayEntryAsync("2").GetAwaiter().GetResult(); Assert.Equal(4, core.AppliedObjects.Count);
    });

    [Fact]
    public void LateObjectApplyCannotCarrySelectionsIntoNextGame() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); settings.Load().SkinCoreEnabled = true;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources()); page.OnGamePhaseChanged(true);
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Minion).GetAwaiter().GetResult();
        core.PendingObjects = new(); var pending = page.SelectOverlayEntryAsync("3");
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Ward).GetAwaiter().GetResult();
        Assert.Equal(1, core.ObjectReads);
        page.OnGamePhaseChanged(false);
        core.PendingObjects.SetResult(new SkinCoreReply { StateVerified = true }); SecondPhaseRegressionTests.Pump(pending);
        core.PendingObjects = null; core.Session = "abcdef1234567890"; page.OnGamePhaseChanged(true);
        page.ChangeOverlayCategoryAsync(SkinPickerCategory.Ward).GetAwaiter().GetResult();
        page.SelectOverlayEntryAsync("5").GetAwaiter().GetResult(); Assert.Equal((-1, 5), core.AppliedObjects.Last());
    });

    [Fact]
    public void SkinTabsAndPosterFollowAppliedSkinAndRestore() => MatchListScrollingTests.OnUiThread(() =>
    {
        var calls = new List<(string Model, int Skin)>();
        byte[] bytes;
        using (var bitmap = new Bitmap(4, 4))
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            bytes = stream.ToArray();
        }
        var assets = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, args) =>
        {
            if (method.Name == "GetCatalogAsync") return Task.FromResult<IReadOnlyList<ChampionSkinResource>>([
                new(0, "拉克丝", []), new(7, "大元素使 拉克丝", [])]);
            Assert.Equal("GetPosterAsync", method.Name);
            calls.Add(("Lux", ((ChampionSkinResource)args[0]!).SkinNumber));
            return Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(new(bytes));
        });
        var core = new Core();
        var settings = new Settings();
        settings.Load().SkinCoreEnabled = true;
        using var host = new Form { ClientSize = new Size(1500, 850), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, settings, assets);
        host.Controls.Add(page); host.Show();
        page.OnGamePhaseChanged(true);
        var poster = Descendants(page).OfType<PictureBox>().Single(picture => picture.Name == "SkinPoster");
        SecondPhaseRegressionTests.PumpUntil(() => poster.Image != null);
        Assert.Equal(("Lux", 0), calls.Last());
        var selector = Descendants(page).OfType<AntdUI.Select>().Single(select => select.Name == "HeroSkinEntries");
        selector.SelectedIndex = 1;
        SecondPhaseRegressionTests.PumpUntil(() => calls.Last().Skin == 7 && poster.Image != null);
        Assert.Contains("大元素使 拉克丝", ((AntdUI.SelectItem)selector.Items[1]!).SubText);
        Assert.Single(core.AppliedEntries); // Updating client names must not apply another skin.
        var tabs = Descendants(page).OfType<AntdUI.Button>();
        var heroTab = tabs.Single(b => b.Name == "HeroSkinTab");
        var objectTab = tabs.Single(b => b.Name == "ObjectSkinTab");
        Assert.True(objectTab.Left - heroTab.Right >= 16);
        objectTab.PerformClick();
        Assert.Equal(AntdUI.TTypeMini.Primary, objectTab.Type);
        Assert.Equal(AntdUI.TTypeMini.Default, heroTab.Type);
        heroTab.PerformClick();
        Assert.True(selector.Visible);
        Assert.Equal(AntdUI.TTypeMini.Primary, heroTab.Type);
        var restore = (Task)typeof(SkinToolsForm).GetMethod("ChangeAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, [true])!;
        SecondPhaseRegressionTests.Pump(restore);
        SecondPhaseRegressionTests.PumpUntil(() => calls.Last().Skin == 0 && poster.Image != null);
        page.OnGamePhaseChanged(false);
        Assert.Null(poster.Image);
    });
    private sealed class Settings : IApplicationSettingsStore
    {
        private AssistantSettings _settings = new();
        public AssistantSettings Load() => _settings;
        public void Save(AssistantSettings settings) => _settings = settings;
        public string GetStoragePath() => "unused";
    }
    private sealed class Core : ISkinCoreService
    {
        public bool Enabled { get; private set; }
        public int Reads { get; private set; }
        public TaskCompletionSource<SkinCoreReply>? PendingRead { get; set; }
        public SkinCoreReply? LastCatalog { get; private set; }
        public CancellationToken LastReadToken { get; private set; }
        public string Session { get; set; } = "1234567890abcdef";
        public string Model { get; set; } = "Lux";
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void InvalidateSession() { }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken ct = default)
        {
            Reads++;
            LastReadToken = ct;
            if (PendingRead != null) return PendingRead.Task;
            LastCatalog = new SkinCoreReply { Ok = true, Independent = true, OriginalRequired = false,
                Session = Session, Model = Model, ActiveModel = Model, Entries = [
                    new() { Index = 0, EntryId = "0:-1", SkinNum = 0, Name = "基础皮肤", Model = "Lux" },
                    new() { Index = 1, EntryId = "1:-1", SkinNum = 7, Name = "大元素使 烈焰", Model = "LuxFire" }] };
            return Task.FromResult(LastCatalog);
        }
        public List<string> AppliedEntries { get; } = [];
        public List<(int Minion, int Ward)> AppliedObjects { get; } = [];
        public int ObjectReads { get; private set; }
        public int ObjectRestores { get; private set; }
        public int ObjectRestoreTargets { get; set; } = 7;
        public TaskCompletionSource<SkinCoreReply>? PendingObjects { get; set; }
        public Task<SkinCoreReply> GetObjectCatalogAsync(CancellationToken token = default)
        {
            ObjectReads++;
            return Task.FromResult(new SkinCoreReply { Session = Session, LaneCount = 6, OwnWardCount = 1,
                WardEntries = [new() { SkinNum = 5, Name = "测试眼位" }, new() { SkinNum = 19, Name = "另一眼位" }] });
        }
        public Task<SkinCoreReply> ApplyObjectsAsync(string session, int minionIndex, int wardSkin, CancellationToken token = default)
        {
            AppliedObjects.Add((minionIndex, wardSkin));
            return PendingObjects?.Task ?? Task.FromResult(new SkinCoreReply { Session = Session, StateVerified = true, ObjectTargetCount = 7 });
        }
        public Task<SkinCoreReply> RestoreObjectsAsync(string session, CancellationToken token = default)
        { ObjectRestores++; return Task.FromResult(new SkinCoreReply { Session = Session, StateVerified = ObjectRestoreTargets > 0, ObjectTargetCount = ObjectRestoreTargets }); }
        public bool VerifyApply { get; set; } = true;
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken ct = default)
        {
            AppliedEntries.Add(entry); var target = LastCatalog!.Entries.Single(e => e.EntryId == entry);
            return Task.FromResult(LastCatalog with { Invoked = true, StateVerified = VerifyApply, Skin = target.SkinNum, ActiveModel = target.Model, Gear = target.Gear });
        }
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken ct = default) => Task.FromResult(LastCatalog! with { StateVerified = true });
        public bool StopFails { get; set; }
        public int Stops { get; private set; }
        public Task<SkinCoreReply> StopAsync(string session, CancellationToken ct = default) {
            Stops++; Enabled = false;
            if (StopFails) throw new SkinCoreException("request_outcome_unknown", true);
            return Task.FromResult(LastCatalog! with { StateVerified = true, TransportHookReleased = true, ModulePinned = true });
        }
    }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private sealed class RenderHost : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000; return parameters; }
        }
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void StopClearsSelectionAndPersistsDisabledEvenOnFailure(bool fails) => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core { StopFails = fails }; var settings = new Settings(); using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true);
        var toggle = Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled"); toggle.Checked = true;
        var stop = (Task)typeof(SkinToolsForm).GetMethod("StopAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null)!;
        stop.GetAwaiter().GetResult(); Assert.Equal(1, core.Stops);
        Assert.False(core.Enabled); Assert.False(toggle.Checked); Assert.False(settings.Load().SkinCoreEnabled);
        Assert.Empty(Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries").Items);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")), button => Assert.False(button.Enabled));
    });
    [Fact]
    public void DefaultsOffAndDoesNotConnectUntilUserEnables() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        Assert.False(core.Enabled); Assert.Equal(0, core.Reads);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")), button => Assert.False(button.Enabled));
        var toggle = Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled"); toggle.Checked = true;
        Assert.True(core.Enabled); Assert.True(settings.Load().SkinCoreEnabled); Assert.Equal(0, core.Reads);
        page.OnGamePhaseChanged(true);
        Assert.Equal(1, core.Reads);
        var select = Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries"); Assert.Equal(2, select.Items.Count);
        var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinSearch"); search.Text = "烈焰"; Assert.Single(select.Items);
        toggle.Checked = false; Assert.False(core.Enabled); Assert.False(settings.Load().SkinCoreEnabled); Assert.Empty(select.Items);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")), button => Assert.False(button.Enabled));
    });
    [Fact]
    public void DisposingDuringRefreshCancelsOnceAndIgnoresLateVisibilityAndReply() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(820, 980), ShowInTaskbar = false };
        var page = new SkinToolsForm(core, new Settings(), EmptySkinResources()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        page.OnGamePhaseChanged(true);
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
        core.PendingRead = new TaskCompletionSource<SkinCoreReply>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var refresh = (Task)typeof(SkinToolsForm).GetMethod("RefreshAsync", flags)!.Invoke(page, null)!;
        Assert.False(refresh.IsCompleted); CancellationToken token = core.LastReadToken;
        page.Dispose(); Assert.True(token.IsCancellationRequested);
        // Window teardown can deliver visibility notifications after child cleanup.
        typeof(SkinToolsForm).GetMethod("UpdateActivity", flags)!.Invoke(page, null);
        page.Dispose(); host.Hide();
        core.PendingRead.SetResult(core.LastCatalog!);
        Assert.True(SpinWait.SpinUntil(() => { System.Windows.Forms.Application.DoEvents(); return refresh.IsCompleted; }, TimeSpan.FromSeconds(5)));
        refresh.GetAwaiter().GetResult(); Assert.Equal(2, core.Reads);
    });
    [Fact]
    public void BackgroundRefreshDoesNotDisableOrResetSkinSelector() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(820, 980), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, new Settings(), EmptySkinResources()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        page.OnGamePhaseChanged(true);
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
        var select = Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries"); select.SelectedIndex = 1;
        Assert.Equal("1:-1", Assert.Single(core.AppliedEntries));
        Assert.DoesNotContain(Descendants(page), control => control.Name is "SkinActionApply" or "SkinApplyKey");
        int disabled = 0; select.EnabledChanged += (_, _) => { if (!select.Enabled) disabled++; };
        object second = select.Items[1]!;
        core.PendingRead = new TaskCompletionSource<SkinCoreReply>();
        var refresh = (Task)typeof(SkinToolsForm).GetMethod("RefreshAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null)!;
        Assert.False(refresh.IsCompleted); Assert.True(select.Enabled); Assert.Equal(1, select.SelectedIndex);
        core.PendingRead.SetResult(core.LastCatalog!);
        Assert.True(SpinWait.SpinUntil(() => { System.Windows.Forms.Application.DoEvents(); return refresh.IsCompleted; }, TimeSpan.FromSeconds(5)));
        refresh.GetAwaiter().GetResult();
        Assert.Equal(0, disabled); Assert.True(select.Enabled); Assert.Equal(1, select.SelectedIndex); Assert.Same(second, select.Items[1]);
        var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinSearch");
        search.Text = "烈焰";
        search.Text = "";
        Assert.Single(core.AppliedEntries);
    });
    [Fact]
    public void UnverifiedHotkeyDoesNotAnnounceSuccess() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core { VerifyApply = false };
        var settings = new Settings(); settings.Load().SkinCoreEnabled = true; settings.Load().SkinHotkeysEnabled = true;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true);
        page.Visible = false;
        var notices = new List<string>(); page.HotkeySkinApplied += notices.Add;
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        Assert.Single(core.AppliedEntries);
        Assert.Empty(notices);
    });
    [Fact]
    public void HotkeyCanCycleWhilePageHiddenAndDropsRapidRepeats() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); settings.Load().SkinCoreEnabled = true; settings.Load().SkinHotkeysEnabled = true;
        using var host = new RenderHost { ClientSize = new Size(820, 620), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, settings, EmptySkinResources()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        page.OnGamePhaseChanged(true);
        page.Visible = false;
        var notices = new List<string>();
        page.HotkeySkinApplied += notices.Add;
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        Assert.Equal("1:-1", Assert.Single(core.AppliedEntries));
        Assert.Equal("大元素使 烈焰", Assert.Single(notices));
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult(); Assert.Single(core.AppliedEntries);
        Assert.Single(notices);
        core.SetEnabled(false); page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinPrevious).GetAwaiter().GetResult(); Assert.Single(core.AppliedEntries);
    });
    [Fact]
    public void ConflictingHotkeyIsRejectedWithoutSavingEnabledState() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); using var page = new SkinToolsForm(new Core(), settings, EmptySkinResources());
        int notifications = 0; page.HotkeySettingsChanged += () => notifications++;
        var next = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinNextKey");
        var enabled = Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinHotkeysEnabled");
        next.Text = "F9"; enabled.Checked = true;
        Assert.False(enabled.Checked); Assert.False(settings.Load().SkinHotkeysEnabled);
        Assert.False(settings.Load().SkinCoreEnabled); Assert.Equal("Ctrl+Alt+Right", settings.Load().SkinNextHotkey); Assert.Equal(0, notifications);
        next.Text = "Ctrl+Alt+Right"; enabled.Checked = true;
        Assert.True(settings.Load().SkinHotkeysEnabled); Assert.False(settings.Load().SkinCoreEnabled); Assert.Equal(1, notifications);
    });
    [Fact]
    public void PickerHotkeyPersistsAndRejectsConflictsWithCyclingDisabled() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings();
        using var page = new SkinToolsForm(new Core(), settings, EmptySkinResources());
        var picker = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinPickerKey");
        Assert.Equal("Home", picker.Text);
        void Save() => typeof(SkinToolsForm).GetMethod("SaveHotkeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null);
        picker.Text = "Ctrl+F10"; Save();
        Assert.Equal("Ctrl+F10", settings.Load().SkinPickerHotkey);
        Assert.False(settings.Load().SkinCoreEnabled);
        Assert.False(settings.Load().SkinHotkeysEnabled);
        var dto = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToLegacy(settings.Load());
        var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<LOL_GameAssistant.Entity.SettingConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(dto))!;
        Assert.Equal("Ctrl+F10", LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToDomain(saved).SkinPickerHotkey);
        foreach (var conflict in new[] { "F9", "Oem3", "bad-key" })
        {
            picker.Text = conflict; Save();
            Assert.Equal("Ctrl+F10", settings.Load().SkinPickerHotkey);
        }
        using var reopened = new SkinToolsForm(new Core(), settings, EmptySkinResources());
        Assert.Equal("Ctrl+F10", Descendants(reopened).OfType<AntdUI.Input>().Single(c => c.Name == "SkinPickerKey").Text);
    });
    [Fact]
    public void ThemeAndLayoutRenderWithoutGameConnection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(1060, 1480), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, new Settings(), EmptySkinResources()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        string mode = UiTheme.Mode; bool wasDark = AntdUI.Config.IsDark;
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                UiTheme.SetMode(theme); AntdUI.Config.IsDark = theme == "Dark"; UiTheme.Apply(host); host.PerformLayout(); page.PerformLayout();
                Assert.Equal(UiTheme.Palette.Surface, page.BackColor); Assert.Equal(0, core.Reads);
                using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
                host.DrawToBitmap(bitmap, host.ClientRectangle);
                string directory = Path.Combine(AppContext.BaseDirectory, "skin-ui-preview"); Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, theme + ".png"));
            }
        }
        finally { UiTheme.SetMode(mode); AntdUI.Config.IsDark = wasDark; }
    });

    [Fact]
    public void RememberedSkinSurvivesSettingsRoundTripAndAppliesInNextGameWhileHidden() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); var core = new Core();
        using (var page = new SkinToolsForm(core, settings, EmptySkinResources()))
        {
            page.OnGamePhaseChanged(true);
            Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
            Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries").SelectedIndex = 1;
            Assert.Equal("LuxFire", settings.Load().RememberedHeroSkins["Lux"].Model);
        }
        var dto = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToLegacy(settings.Load());
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);
        settings.Save(LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToDomain(
            Newtonsoft.Json.JsonConvert.DeserializeObject<LOL_GameAssistant.Entity.SettingConfig>(json)!));
        core = new Core { Session = "abcdef1234567890" };
        using var nextPage = new SkinToolsForm(core, settings, EmptySkinResources());
        nextPage.Visible = false;
        nextPage.OnGamePhaseChanged(true);
        Assert.Equal("1:-1", Assert.Single(core.AppliedEntries));
        Refresh(nextPage);
        Assert.Single(core.AppliedEntries);
        nextPage.OnGamePhaseChanged(false);
        core.Session = "abcdef1234567891"; core.Model = "Ahri";
        nextPage.OnGamePhaseChanged(true);
        Assert.Single(core.AppliedEntries);
    });

    [Fact]
    public void RestoreClearsMemoryAndPollingDoesNotReapply() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); var core = new Core();
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true);
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
        Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries").SelectedIndex = 1;
        var restore = (Task)typeof(SkinToolsForm).GetMethod("ChangeAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, [true])!;
        restore.GetAwaiter().GetResult();
        Assert.Empty(settings.Load().RememberedHeroSkins);
        Refresh(page);
        Assert.Single(core.AppliedEntries);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnverifiedApplyDoesNotOverwriteMemoryOrAutomaticallyRetry(bool savedSkin) => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); var core = new Core { VerifyApply = false };
        if (savedSkin) settings.Load().RememberedHeroSkins["Lux"] = new SkinEntry { Model = "LuxFire", SkinNum = 7, Gear = -1 };
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true);
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
        if (!savedSkin) Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries").SelectedIndex = 1;
        Refresh(page);
        Assert.Single(core.AppliedEntries);
        Assert.Equal(savedSkin ? 1 : 0, settings.Load().RememberedHeroSkins.Count);
    });

    private static void Refresh(SkinToolsForm page) => ((Task)typeof(SkinToolsForm).GetMethod("RefreshAsync",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null)!).GetAwaiter().GetResult();

    [Theory]
    [InlineData(false, true, false, "已确认卸载")]
    [InlineData(true, false, false, "仍在游戏进程内")]
    [InlineData(true, false, true, "固定驻留")]
    [InlineData(null, null, false, "卸载尚未核验")]
    public void StopMessageDistinguishesModuleState(bool? loaded, bool? verified, bool pinned, string expected)
    {
        var reply = new SkinCoreReply { StateVerified = true, TransportHookReleased = true,
            ModuleLoaded = loaded, ModuleUnloadVerified = verified, ModulePinned = pinned };
        Assert.Contains(expected, SkinToolsForm.BuildStopMessage(reply));
        Assert.Contains("本局原皮", SkinToolsForm.BuildStopMessage(reply));
    }

    [Fact]
    public void OnlyGamePhaseStartsReadingAndVisibilityDoesNotProbe() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core();
        var settings = new Settings();
        settings.Load().SkinCoreEnabled = settings.Load().SkinHotkeysEnabled = true;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.Visible = false;
        page.Visible = true;
        Refresh(page);
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        Assert.Equal(0, core.Reads);

        page.OnGamePhaseChanged(true);
        Assert.Equal(1, core.Reads);
        page.OnGamePhaseChanged(true);
        page.Visible = false;
        page.Visible = true;
        Assert.Equal(1, core.Reads);

        page.OnGamePhaseChanged(false);
        page.Visible = false;
        page.Visible = true;
        Refresh(page);
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        ((Task)typeof(SkinToolsForm).GetMethod("RunObjectsAsync", flags)!.Invoke(page, [0])!).GetAwaiter().GetResult();
        Assert.Equal(1, core.Reads);
        Assert.Empty(core.AppliedEntries);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")),
            button => Assert.False(button.Enabled));

        page.OnGamePhaseChanged(true);
        Assert.Equal(2, core.Reads);
    });

    [Fact]
    public void LeavingGameCancelsReadAndDoesNotApplyLateRememberedSkin() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core();
        var settings = new Settings();
        settings.Load().SkinCoreEnabled = true;
        using var page = new SkinToolsForm(core, settings, EmptySkinResources());
        page.OnGamePhaseChanged(true);
        var catalog = core.LastCatalog!;
        settings.Load().RememberedHeroSkins["Lux"] = catalog.Entries[1];
        core.PendingRead = new TaskCompletionSource<SkinCoreReply>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var pending = (Task)typeof(SkinToolsForm).GetMethod("RefreshAsync", flags)!.Invoke(page, null)!;
        var token = core.LastReadToken;
        Assert.False(pending.IsCompleted);
        page.OnGamePhaseChanged(false);
        Assert.True(token.IsCancellationRequested);
        core.PendingRead.SetResult(catalog with { Session = "abcdef1234567890" });
        Assert.True(SpinWait.SpinUntil(() => { System.Windows.Forms.Application.DoEvents(); return pending.IsCompleted; }, TimeSpan.FromSeconds(5)));
        pending.GetAwaiter().GetResult();
        Assert.Empty(core.AppliedEntries);
        Assert.Empty(Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == "HeroSkinEntries").Items);
    });
}
