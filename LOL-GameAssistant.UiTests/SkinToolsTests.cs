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
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void InvalidateSession() { }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken ct = default)
        {
            Reads++;
            LastReadToken = ct;
            if (PendingRead != null) return PendingRead.Task;
            LastCatalog = new SkinCoreReply { Ok = true, Independent = true, OriginalRequired = false,
                Session = "1234567890abcdef", Model = "Lux", ActiveModel = "Lux", Entries = [
                    new() { Index = 0, EntryId = "0:-1", SkinNum = 0, Name = "基础皮肤", Model = "Lux" },
                    new() { Index = 1, EntryId = "1:-1", SkinNum = 7, Name = "大元素使 烈焰", Model = "LuxFire" }] };
            return Task.FromResult(LastCatalog);
        }
        public List<string> AppliedEntries { get; } = [];
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken ct = default)
        {
            AppliedEntries.Add(entry); var target = LastCatalog!.Entries.Single(e => e.EntryId == entry);
            return Task.FromResult(LastCatalog with { Invoked = true, StateVerified = true, Skin = target.SkinNum, ActiveModel = target.Model, Gear = target.Gear });
        }
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken ct = default) => throw new InvalidOperationException();
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
    [Fact]
    public void DefaultsOffAndDoesNotConnectUntilUserEnables() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); using var page = new SkinToolsForm(core, settings);
        Assert.False(core.Enabled); Assert.Equal(0, core.Reads);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")), button => Assert.False(button.Enabled));
        var toggle = Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled"); toggle.Checked = true;
        Assert.True(core.Enabled); Assert.True(settings.Load().SkinCoreEnabled); Assert.Equal(1, core.Reads);
        var select = Descendants(page).OfType<AntdUI.Select>().Single(); Assert.Equal(2, select.Items.Count);
        var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.Name == "SkinSearch"); search.Text = "烈焰"; Assert.Single(select.Items);
        toggle.Checked = false; Assert.False(core.Enabled); Assert.False(settings.Load().SkinCoreEnabled); Assert.Empty(select.Items);
        Assert.All(Descendants(page).OfType<AntdUI.Button>().Where(b => b.Name.StartsWith("SkinAction")), button => Assert.False(button.Enabled));
    });
    [Fact]
    public void DisposingDuringRefreshCancelsOnceAndIgnoresLateVisibilityAndReply() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(820, 980), ShowInTaskbar = false };
        var page = new SkinToolsForm(core, new Settings()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
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
        using var page = new SkinToolsForm(core, new Settings()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        Descendants(page).OfType<AntdUI.Switch>().Single(c => c.Name == "SkinCoreEnabled").Checked = true;
        var select = Descendants(page).OfType<AntdUI.Select>().Single(); select.SelectedIndex = 1;
        int disabled = 0; select.EnabledChanged += (_, _) => { if (!select.Enabled) disabled++; };
        object second = select.Items[1]!;
        core.PendingRead = new TaskCompletionSource<SkinCoreReply>();
        var refresh = (Task)typeof(SkinToolsForm).GetMethod("RefreshAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(page, null)!;
        Assert.False(refresh.IsCompleted); Assert.True(select.Enabled); Assert.Equal(1, select.SelectedIndex);
        core.PendingRead.SetResult(core.LastCatalog!);
        Assert.True(SpinWait.SpinUntil(() => { System.Windows.Forms.Application.DoEvents(); return refresh.IsCompleted; }, TimeSpan.FromSeconds(5)));
        refresh.GetAwaiter().GetResult();
        Assert.Equal(0, disabled); Assert.True(select.Enabled); Assert.Equal(1, select.SelectedIndex); Assert.Same(second, select.Items[1]);
    });
    [Fact]
    public void HotkeyCanCycleWhilePageHiddenAndDropsRapidRepeats() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); var settings = new Settings(); settings.Load().SkinCoreEnabled = true; settings.Load().SkinHotkeysEnabled = true;
        using var host = new RenderHost { ClientSize = new Size(820, 620), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, settings); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        page.Visible = false;
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult();
        Assert.Equal("1:-1", Assert.Single(core.AppliedEntries));
        page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinNext).GetAwaiter().GetResult(); Assert.Single(core.AppliedEntries);
        core.SetEnabled(false); page.ApplyHotkeyAsync(QuickShoutHotkeyAction.SkinPrevious).GetAwaiter().GetResult(); Assert.Single(core.AppliedEntries);
    });
    [Fact]
    public void ConflictingHotkeyIsRejectedWithoutSavingEnabledState() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new Settings(); using var page = new SkinToolsForm(new Core(), settings);
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
    public void ThemeAndLayoutRenderWithoutGameConnection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(1060, 980), ShowInTaskbar = false };
        using var page = new SkinToolsForm(core, new Settings()); host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
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
}
