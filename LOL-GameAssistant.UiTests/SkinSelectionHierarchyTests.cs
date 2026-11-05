using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SkinSelectionHierarchyTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly SkinEntry[] Entries = [
        new() { EntryId = "base", SkinNum = 0, Model = "Lux", Name = "基础" },
        new() { EntryId = "parent", SkinNum = 7, Model = "Lux", Name = "皮肤" },
        new() { EntryId = "blue", SkinNum = 9, Model = "Lux", Name = "蓝色" },
        new() { EntryId = "green", SkinNum = 10, Model = "Lux", Name = "绿色" }];

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void CurrentChromaAndRememberedChromaSelectParentAndChildWithoutExtraWrites(bool remembered) => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(remembered ? 0 : 9); var settings = new Settings();
        if (remembered) settings.Value.RememberedHeroSkins["Lux"] = Entries[2];
        using var page = Create(core, settings);
        var primary = Select(page, "HeroSkinEntries"); var secondary = Select(page, "HeroSkinVariants");
        Assert.Equal(2, primary.Items.Count);
        Assert.Equal("parent", ((AntdUI.SelectItem)primary.Items[primary.SelectedIndex]!).Tag);
        Assert.Equal(3, secondary.Items.Count);
        Assert.Equal(new[] { "默认", "蓝色", "绿色" }, secondary.Items.Cast<AntdUI.SelectItem>().Select(item => item.Text));
        Assert.Equal("蓝色", secondary.Text);
        Assert.Equal("blue", ((AntdUI.SelectItem)secondary.Items[secondary.SelectedIndex]!).Tag);
        Assert.Equal(remembered ? 1 : 0, core.Applied.Count);
        secondary.SelectedIndex = 2;
        Assert.Equal("green", core.Applied.Last());
        Assert.Equal(10, core.State.ActiveSkin);
        Assert.Equal("绿色", secondary.Text);
        var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinSearch");
        search.Text = "绿色";
        Assert.Single(primary.Items);
        Assert.Equal("green", ((AntdUI.SelectItem)secondary.Items[secondary.SelectedIndex]!).Tag);
        Assert.Equal(remembered ? 2 : 1, core.Applied.Count);
        if (Environment.GetEnvironmentVariable("SKIN_HIERARCHY_SNAPSHOT") is string path && path.Length > 0)
        {
            using var host = new Form { ClientSize = new Size(1100, 1000), ShowInTaskbar = false };
            host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(host.Width, host.Height); host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    });

    [Fact]
    public void HotkeysKeepCyclingFlatCatalogAndSynchronizeBothSelectorsWhileHidden() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(0); using var page = Create(core, new Settings()); page.Visible = false;
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        typeof(SkinToolsForm).GetField("_lastHotkeyAt", Private)!.SetValue(page, 0L);
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        Assert.Equal(new[] { "parent", "blue" }, core.Applied);
        var primary = Select(page, "HeroSkinEntries"); var secondary = Select(page, "HeroSkinVariants");
        Assert.Equal("parent", ((AntdUI.SelectItem)primary.Items[primary.SelectedIndex]!).Tag);
        Assert.Equal("blue", ((AntdUI.SelectItem)secondary.Items[secondary.SelectedIndex]!).Tag);
    });

    [Fact]
    public void OverlayNavigatesChildrenAndReopensAtActualChromaWithoutApplyingOnNavigation() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(9); using var page = Create(core, new Settings());
        using var bridge = new RecordingGameOverlay();
        using var picker = new SkinPickerOverlayController(() => true, id => page.SelectOverlayEntryAsync(id).GetAwaiter().GetResult(), () => { }, () => { }, bridge);
        typeof(SkinToolsForm).GetField("_skinPicker", Private)!.SetValue(page, picker);
        void Update() => typeof(SkinToolsForm).GetMethod("UpdateSkinPicker", Private)!.Invoke(page, [true]);
        Update(); picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(2, panel.Items!.Count);
        Assert.Contains("蓝色", panel.Submenu!.Items[panel.Submenu.Selected]);
        Assert.Equal(panel.Selected, panel.Submenu.Parent);
        Assert.Equal(new[] { "默认", "蓝色", "绿色" }, panel.Submenu.Items);
        bridge.Command = new(9, 0, panel.Version); picker.PollCommands();
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(2, panel.Items!.Count); Assert.Contains("炫彩 / 形态", panel.Items[panel.Selected]);
        Assert.Null(panel.Submenu);
        Assert.Empty(core.Applied);
        bridge.Command = new(1, panel.Selected, panel.Version); picker.PollCommands();
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(2, panel.Items!.Count); Assert.Equal(3, panel.Submenu!.Items.Count);
        bridge.Command = new(8, 2, panel.Version); picker.PollCommands();
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Contains("绿色", panel.Submenu!.Items[panel.Submenu.Selected]); Assert.Equal("green", Assert.Single(core.Applied));
        int version = panel.Version; picker.Hide(); picker.ShowOverGame(Rectangle.Empty);
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.True(panel.Version > version); Assert.Contains("绿色", panel.Submenu!.Items[panel.Submenu.Selected]);
    });

    [Fact]
    public void HiddenMainPageAppliesChromaFromAnotherParentThroughSecondaryCommand() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(0); using var page = Create(core, new Settings()); page.Visible = false;
        using var bridge = new RecordingGameOverlay();
        using var picker = new SkinPickerOverlayController(() => true, id => page.SelectOverlayEntryAsync(id).GetAwaiter().GetResult(), () => { }, () => { }, bridge);
        typeof(SkinToolsForm).GetField("_skinPicker", Private)!.SetValue(page, picker);
        typeof(SkinToolsForm).GetMethod("UpdateSkinPicker", Private)!.Invoke(page, [true]);
        picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Null(panel.Submenu);
        bridge.Command = new(1, 1, panel.Version); picker.PollCommands();
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(3, panel.Submenu!.Items.Count); Assert.Empty(core.Applied);
        bridge.Command = new(8, 2, panel.Version); picker.PollCommands();
        Assert.Equal("green", Assert.Single(core.Applied));
        Assert.Equal(10, core.State.ActiveSkin);
        panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Contains("绿色", panel.Submenu!.Items[panel.Submenu.Selected]);
    });

    [Fact]
    public void SubmenuPagesIndependentlyAndRejectsClicksFromPreviousGroup() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingGameOverlay(); var selected = new List<string>();
        using var picker = new SkinPickerOverlayController(() => true, selected.Add, () => { }, () => { }, bridge);
        picker.SetState([("group:7", "皮肤")], "group:7", "", true, true);
        picker.SetSubmenu(Enumerable.Range(0, 70).Select(i => ($"child:{i}", $"炫彩 {i}")).ToArray(), "child:69", "group:7", "炫彩");
        picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Single(panel.Items!); Assert.Equal(2, panel.Submenu!.Page); Assert.Equal(5, panel.Submenu.Selected);
        bridge.Command = new(8, 5, panel.Version); picker.PollCommands();
        Assert.Equal("child:69", Assert.Single(selected));
        bridge.Command = new(10, -1, panel.Version); picker.PollCommands();
        var paged = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(0, paged.Page); Assert.Equal(1, paged.Submenu!.Page);
        bridge.Command = new(8, 0, panel.Version); picker.PollCommands(); Assert.Single(selected);
        picker.SetSubmenu([("new-child", "新的炫彩")], null, "group:8", "其他皮肤");
        bridge.Command = new(8, 0, paged.Version); picker.PollCommands(); Assert.Single(selected);
    });

    [Fact]
    public void ReopeningPickerAndSelectionChangesFocusTheSelectedPage() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingGameOverlay();
        using var picker = new SkinPickerOverlayController(() => true, _ => { }, () => { }, () => { }, bridge);
        var items = Enumerable.Range(0, 500).Select(i => (i.ToString(), $"Skin {i}")).ToArray();
        picker.SetState(items, "490", "", true, true); picker.ShowOverGame(Rectangle.Empty);
        var panel = bridge.Panels[GameOverlayPanelKind.SkinPicker];
        Assert.Equal(2, panel.Page); Assert.Equal(10, panel.Selected);
        bridge.Command = new(6, -1, panel.Version); picker.PollCommands();
        Assert.Equal(1, bridge.Panels[GameOverlayPanelKind.SkinPicker].Page);
        picker.Hide(); picker.ShowOverGame(Rectangle.Empty);
        Assert.Equal(2, bridge.Panels[GameOverlayPanelKind.SkinPicker].Page);
        picker.SetState(items, "1", "", true, true);
        Assert.Equal(0, bridge.Panels[GameOverlayPanelKind.SkinPicker].Page);
        Assert.Equal(1, bridge.Panels[GameOverlayPanelKind.SkinPicker].Selected);
    });

    private static SkinToolsForm Create(Core core, Settings settings)
    {
        var resources = SecondPhaseRegressionTests.Service<IChampionSkinResourceService>((method, _) => method.Name == "GetCatalogAsync"
            ? Task.FromResult<IReadOnlyList<ChampionSkinResource>>([new(0, "拉克丝", []), new(7, "皮肤", [], SkinQuality.Epic),
                new(9, "皮肤 · 蓝色", [], SkinQuality.Epic, true, 7), new(10, "皮肤（绿色）", [], SkinQuality.Epic, true, 7)])
            : Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null));
        var page = new SkinToolsForm(core, settings, resources); page.OnGamePhaseChanged(true); return page;
    }
    private static AntdUI.Select Select(Control page, string name) => Descendants(page).OfType<AntdUI.Select>().Single(c => c.Name == name);
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private sealed class Settings : IApplicationSettingsStore
    {
        public AssistantSettings Value = new() { SkinCoreEnabled = true, SkinHotkeysEnabled = true };
        public AssistantSettings Load() => Value;
        public void Save(AssistantSettings value) => Value = value;
        public string GetStoragePath() => "unused";
    }
    private sealed class Core(int skin) : ISkinCoreService
    {
        public bool Enabled { get; private set; }
        public SkinCoreReply State = new() { Session = "session", Model = "Lux", ActiveModel = "Lux", Skin = skin, ActiveSkin = skin, Gear = 4, Entries = Entries };
        public List<string> Applied = [];
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void InvalidateSession() { }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) => Task.FromResult(State);
        public Task<SkinCoreReply> ApplyAsync(string session, string entryId, CancellationToken token = default)
        {
            var entry = Entries.Single(e => e.EntryId == entryId); Applied.Add(entryId);
            State = State with { Skin = entry.SkinNum, ActiveSkin = entry.SkinNum, ActiveModel = entry.Model, StateVerified = true };
            return Task.FromResult(State);
        }
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) => ApplyAsync(session, "base", token);
        public Task<SkinCoreReply> StopAsync(string session, CancellationToken token = default) => RestoreAsync(session, token);
    }
}
