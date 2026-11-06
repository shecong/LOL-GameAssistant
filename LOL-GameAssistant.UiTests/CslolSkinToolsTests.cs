using System.Reflection;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CslolSkinToolsTests
{
    [Fact]
    public void BrowserIsIsolatedAndSupportsChineseEnglishSearchAndSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var files = new Files(); var engines = new Engines(); var prefs = new Preferences();
        using var host = new Form { Width = 1280, Height = 900 };
        using var page = new FileSkinBrowserForm(files, engines, new Settings(), prefs, new Artwork(), () => false);
        host.Controls.Add(page); host.Show();
        SecondPhaseRegressionTests.PumpUntil(() => Find<FlowLayoutPanel>(page, "FileSkinCards").Controls.Count == 2);
        Assert.Empty(page.Controls.Find("HeroSkinEntries", true)); Assert.Empty(page.Controls.Find("SkinEngineMode", true));
        Assert.Equal(0, engines.Switches); Assert.Equal(0, files.Applies);
        var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
        Assert.Equal("哥特萝莉 安妮", cards.Controls[0].AccessibleName);
        var query = Find<AntdUI.Input>(page, "FileSkinSearch"); query.Text = "哥特"; Assert.Single(cards.Controls);
        query.Text = "goth"; Assert.Single(cards.Controls); query.Text = "";
        Key(cards.Controls[0], Keys.Space);
        Assert.Contains("1", Find<AntdUI.Label>(page, "FileSkinSelectionCount").Text);
        Key(cards.Controls[1], Keys.Space);
        Assert.Single(Find<FlowLayoutPanel>(page, "FileSkinSelectionTray").Controls);
        Assert.Equal(0, engines.Switches); Assert.Equal(0, files.Applies);
        Assert.True(Find<AntdUI.Button>(page, "FileSkinApply").Enabled);
        Key(cards.Controls[1], Keys.F);
        Assert.Single(prefs.Value.Favorites);
        Find<AntdUI.Select>(page, "FileSkinFilter").SelectedIndex = 2;
        Assert.Single(cards.Controls);
        Find<AntdUI.Button>(page, "FileSkinSavePreset").PerformClick(); Assert.Single(prefs.Value.Presets);
    });

    [Fact]
    public void ActiveGameCanBrowseButCannotApplyAndDoesNotSwitchEngine() => MatchListScrollingTests.OnUiThread(() =>
    {
        var files = new Files(); var engines = new Engines();
        using var host = new Form { Width = 1100, Height = 850 };
        using var page = new FileSkinBrowserForm(files, engines, new Settings(), new Preferences(), new Artwork(), () => true);
        host.Controls.Add(page); host.Show();
        var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
        SecondPhaseRegressionTests.PumpUntil(() => cards.Controls.Count == 2);
        Key(cards.Controls[0], Keys.Space);
        Assert.False(Find<AntdUI.Button>(page, "FileSkinApply").Enabled);
        Assert.Equal(0, engines.Switches); Assert.Equal(0, files.Applies);
    });

    [Fact]
    public void ApplyUsesCoordinatorBeforeFileServiceAndFooterRemainsVisible() => MatchListScrollingTests.OnUiThread(() =>
    {
        var files = new Files(); var engines = new Engines();
        files.BeforeApply = () => Assert.Equal(1, engines.Switches);
        using var host = new Form { Width = 1000, Height = 780 };
        using var page = new FileSkinBrowserForm(files, engines, new Settings(), new Preferences(), new Artwork(), () => false);
        host.Controls.Add(page); host.Show();
        var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
        SecondPhaseRegressionTests.PumpUntil(() => cards.Controls.Count == 2);
        Key(cards.Controls[0], Keys.Space);
        var apply = Find<AntdUI.Button>(page, "FileSkinApply"); apply.PerformClick();
        SecondPhaseRegressionTests.PumpUntil(() => files.Applies == 1);
        Assert.Equal(SkinEngineMode.File, engines.Last);
        Assert.True(page.ClientRectangle.Contains(page.PointToClient(apply.PointToScreen(new Point(apply.Width / 2, apply.Height / 2)))));
    });

    [Fact]
    public void LargeCatalogIsPagedAndKeepsSelectionAcrossPages() => MatchListScrollingTests.OnUiThread(() =>
    {
        var files = new Files { Entries = Enumerable.Range(1, 500).Select(i => new CslolSkinEntry {
            ChampionKey = "Annie", SkinId = 900000 + i, SkinName = "Skin " + i, FileName = "skin/" + i }).ToArray() };
        using var host = new Form { Width = 1100, Height = 850 };
        using var page = new FileSkinBrowserForm(files, new Engines(), new Settings(), new Preferences(), new Artwork(), () => false);
        host.Controls.Add(page); host.Show();
        var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
        SecondPhaseRegressionTests.PumpUntil(() => cards.Controls.Count == 16);
        Key(cards.Controls[0], Keys.Space);
        Find<AntdUI.Button>(page, "FileSkinNextPage").PerformClick();
        Assert.Equal(16, cards.Controls.Count); Assert.Equal("2 / 32", Find<AntdUI.Label>(page, "FileSkinPageNumber").Text);
        Assert.Single(Find<FlowLayoutPanel>(page, "FileSkinSelectionTray").Controls);
        Assert.Equal("skin/17", Assert.IsType<CslolSkinEntry>(cards.Controls[0].Tag).FileName);
    });

    [Fact]
    public void CaptureBrowserLayout() => MatchListScrollingTests.OnUiThread(() =>
    {
        if (Environment.GetEnvironmentVariable("FILE_SKIN_CAPTURE") is not { Length: > 0 } path) return;
        var files = new Files();
        if (Environment.GetEnvironmentVariable("FILE_SKIN_CAPTURE_CATALOG") is { Length: > 0 } catalog)
            files.Entries = System.Text.Json.JsonSerializer.Deserialize<CslolSkinEntry[]>(File.ReadAllText(catalog), new System.Text.Json.JsonSerializerOptions {
                PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString })!;
        using var host = new Form { Width = 1360, Height = 930 };
        using var page = new FileSkinBrowserForm(files, new Engines(), new Settings(), new Preferences(), new Artwork(), () => false);
        host.Controls.Add(page); host.Show();
        var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
        SecondPhaseRegressionTests.PumpUntil(() => cards.Controls.Count > 0);
        if (Environment.GetEnvironmentVariable("FILE_SKIN_CAPTURE_HERO") is { Length: > 0 } hero)
            Key(Find<Control>(page, "FileHero_" + hero), Keys.Space);
        Key(cards.Controls[0], Keys.Space);
        if (cards.Controls.Count > 1) Key(cards.Controls[1], Keys.F);
        System.Windows.Forms.Application.DoEvents();
        var apply = Find<AntdUI.Button>(page, "FileSkinApply");
        Assert.True(page.ClientRectangle.Contains(page.PointToClient(apply.PointToScreen(new Point(apply.Width - 1, apply.Height - 1)))));
        using var bitmap = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bitmap, new Rectangle(0, 0, host.Width, host.Height)); bitmap.Save(path);
    });

    private static void Key(Control control, Keys key) => control.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [new KeyEventArgs(key)]);
    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private sealed class Settings : IApplicationSettingsStore
    {
        public AssistantSettings Value { get; } = new(); public AssistantSettings Load() => Value;
        public void Save(AssistantSettings value) { } public string GetStoragePath() => "";
    }
    private sealed class Engines : ISkinEngineCoordinator
    {
        public int Switches; public SkinEngineMode Last;
        public Task SelectAsync(SkinEngineMode mode, CancellationToken token = default) { Switches++; Last = mode; return Task.CompletedTask; }
    }
    private sealed class Preferences : IFileSkinPreferencesStore
    {
        public FileSkinPreferences Value = new(); public FileSkinPreferences Load() => Value;
        public void Save(FileSkinPreferences value) => Value = value;
    }
    private sealed class Artwork : IFileSkinArtworkService
    {
        public Task<GameAsset?> GetChampionAsync(CslolSkinEntry entry, CancellationToken token) => Read(entry.ChampionKey + ".png");
        public Task<GameAsset?> GetSkinAsync(CslolSkinEntry entry, CancellationToken token) => Read(entry.SkinId + ".jpg");
        private static Task<GameAsset?> Read(string name)
        {
            var root = Environment.GetEnvironmentVariable("FILE_SKIN_CAPTURE_ART");
            return Task.FromResult<GameAsset?>(root != null && File.Exists(Path.Combine(root, name)) ? new(File.ReadAllBytes(Path.Combine(root, name))) : null);
        }
    }
    private sealed class Files : ICslolSkinService
    {
        public int Applies; public Action? BeforeApply; public bool IsAvailable => true; public bool IsRunning => false;
        public CslolSkinEntry[] Entries = [new() { ChampionKey = "Annie", SkinId = 1001, SkinName = "Goth Annie", FileName = "annie1" },
            new() { ChampionKey = "Annie", SkinId = 1008, SkinName = "Panda Annie", FileName = "annie8" },
            new() { ChampionKey = "Vayne", SkinId = 67011, SkinName = "PROJECT Vayne", FileName = "vayne11" }];
        public IReadOnlyList<CslolSkinEntry> Catalog => Entries;
        public event Action<string>? StatusChanged;
        public Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken token = default) { StatusChanged?.Invoke("目录已加载"); return Task.FromResult(Catalog); }
        public Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries, bool ignoreConflict = false, CancellationToken cancellationToken = default)
        { BeforeApply?.Invoke(); Applies++; return Task.FromResult(new CslolApplyResult { Ok = true }); }
        public Task<CslolApplyResult> StopAsync() => Task.FromResult(new CslolApplyResult { Ok = true });
    }
}
