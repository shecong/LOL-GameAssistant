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
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void InvalidateSession() { }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(new SkinCoreReply { Ok = true, Independent = true, OriginalRequired = false,
                Session = "1234567890abcdef", Model = "Lux", ActiveModel = "Lux", Entries = [
                    new() { Index = 0, EntryId = "0:-1", SkinNum = 0, Name = "基础皮肤", Model = "Lux" },
                    new() { Index = 1, EntryId = "1:-1", SkinNum = 7, Name = "大元素使 烈焰", Model = "LuxFire" }] });
        }
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken ct = default) => throw new InvalidOperationException();
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
        Assert.All(Descendants(page).OfType<AntdUI.Button>(), button => Assert.False(button.Enabled));
        var toggle = Descendants(page).OfType<AntdUI.Switch>().Single(); toggle.Checked = true;
        Assert.True(core.Enabled); Assert.True(settings.Load().SkinCoreEnabled); Assert.Equal(1, core.Reads);
        var select = Descendants(page).OfType<AntdUI.Select>().Single(); Assert.Equal(2, select.Items.Count);
        var search = Descendants(page).OfType<AntdUI.Input>().Single(c => c.GetType() == typeof(AntdUI.Input)); search.Text = "烈焰"; Assert.Single(select.Items);
        toggle.Checked = false; Assert.False(core.Enabled); Assert.False(settings.Load().SkinCoreEnabled); Assert.Empty(select.Items);
        Assert.All(Descendants(page).OfType<AntdUI.Button>(), button => Assert.False(button.Enabled));
    });
    [Fact]
    public void ThemeAndLayoutRenderWithoutGameConnection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var core = new Core(); using var host = new RenderHost { ClientSize = new Size(820, 620), ShowInTaskbar = false };
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
