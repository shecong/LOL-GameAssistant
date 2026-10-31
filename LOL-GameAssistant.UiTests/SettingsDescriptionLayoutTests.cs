using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SettingsDescriptionLayoutTests
{
    [Theory]
    [InlineData(760, "Dark")]
    [InlineData(1000, "Dark")]
    [InlineData(1594, "Light")]
    public void AiDirectoryNavigatesEveryCardAndTracksManualScrolling(int width, string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string oldTheme = LOL_GameAssistant.Helper.UiTheme.Mode;
        try
        {
            LOL_GameAssistant.Helper.UiTheme.SetMode(theme);
            using var settings = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
                Service<IApplicationSettingsStore>((method, _) => method.Name == "GetStoragePath" ? "test-settings.json" : new AssistantSettings()),
                Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
            using var host = new Form { ClientSize = new Size(width, 780), ShowInTaskbar = false, Opacity = 0 };
            settings.Dock = DockStyle.Fill; host.Controls.Add(settings); host.Show();
            var tabs = (AntdUI.Segmented)typeof(SettingForm).GetField("_settingsSegmented", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            tabs.SelectIndex = 4; System.Windows.Forms.Application.DoEvents();
            var menu = Children(settings).OfType<AntdUI.Menu>().Single(c => c.Name == "aiSectionNavigation");
            var compact = Children(settings).OfType<AntdUI.Select>().Single(c => c.Name == "aiCompactNavigation");
            var page = Children(settings).OfType<AntdUI.Panel>().Single(c => c.Name == "aiSettingsDashboard");
            Assert.Equal(9, menu.Items.Count);
            Assert.Equal(width == 760, compact.Visible);
            Assert.Equal(!compact.Visible, menu.Visible);
            Assert.Equal(menu.Items.Count, compact.Items.Count);
            Assert.NotNull(page.YScroll);
            int fixedTop = menu.PointToScreen(Point.Empty).Y;
            foreach (AntdUI.MenuItem item in menu.Items)
            {
                if (compact.Visible) compact.SelectedIndex = menu.Items.IndexOf(item);
                else menu.Select(item, false);
                System.Windows.Forms.Application.DoEvents();
                var card = Assert.IsAssignableFrom<Control>(item.Tag);
                int relativeTop = card.PointToScreen(Point.Empty).Y - page.PointToScreen(Point.Empty).Y;
                Assert.InRange(relativeTop, page.Padding.Top - 2, page.ClientSize.Height - 1);
                Assert.True(item.Select, $"{item.Name} must remain highlighted after jumping; top={relativeTop}, scroll={page.YScroll!.Value}, cardTop={card.Top}, contentTop={card.Parent!.Top}, selected={string.Join(',', menu.Items.Where(i => i.Select).Select(i => i.Name))}");
                Assert.Equal(fixedTop, menu.PointToScreen(Point.Empty).Y);
            }
            page.YScroll!.Value = 0; System.Windows.Forms.Application.DoEvents();
            Assert.True(menu.Items[0].Select);
            Assert.False(menu.Items[^1].Select);
            Assert.Equal(0, compact.SelectedIndex);
            if (Environment.GetEnvironmentVariable("ANTD_UI_PREVIEWS") is string folder)
            {
                Directory.CreateDirectory(folder);
                using var image = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(image, settings.ClientRectangle);
                image.Save(Path.Combine(folder, $"ai-navigation-{width}-{theme}.png"));
            }
            tabs.SelectIndex = 0; tabs.SelectIndex = 4;
            Assert.Same(menu, Children(settings).OfType<AntdUI.Menu>().Single(c => c.Name == "aiSectionNavigation"));
        }
        finally { LOL_GameAssistant.Helper.UiTheme.SetMode(oldTheme); }
    });

    [Fact]
    public void AiCardsFitTheirRowsOnFirstDisplayAndAfterSwitching() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var settings = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name == "GetStoragePath"
                ? "test-settings.json" : new AssistantSettings()),
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        using var host = new Form { ClientSize = new Size(1594, 780), ShowInTaskbar = false, Opacity = 0 };
        settings.Dock = DockStyle.Fill;
        host.Controls.Add(settings);
        host.Show();
        var navigation = (AntdUI.Segmented)typeof(SettingForm)
            .GetField("_settingsSegmented", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var initialHeights = new Dictionary<string, int>();
        foreach (int width in new[] { 1594, 1000, 1594, 1594 })
        {
            host.ClientSize = new Size(width, 780);
            navigation.SelectIndex = 4;
            System.Windows.Forms.Application.DoEvents();
            foreach (var card in Children(settings).Where(control => control.Name.StartsWith("ai") && control.Name.EndsWith("Card")))
            {
                var rows = Assert.Single(card.Controls.OfType<TableLayoutPanel>());
                Assert.True(Math.Abs(card.Height - rows.Height - card.Padding.Vertical) <= 1,
                    $"{card.Name}: card {card.Height}, rows {rows.Height}, padding {card.Padding.Vertical}");
                foreach (Control child in rows.Controls)
                    Assert.True(child.Bottom + child.Margin.Bottom <= rows.Height,
                        $"{card.Name}: row content extends beyond the card: {child.Bounds}");
                if (width == 1594)
                {
                    if (initialHeights.TryGetValue(card.Name, out int initialHeight))
                        Assert.True(initialHeight == card.Height,
                            $"{card.Name}: first display {initialHeight}, after resizing {card.Height}");
                    else initialHeights.Add(card.Name, card.Height);
                }
            }
            navigation.SelectIndex = 0;
        }
    });

    [Fact]
    public void AiDescriptionsUseAvailableWidthAndRewrapAfterResizing() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var settings = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name == "GetStoragePath"
                ? "test-settings.json" : new AssistantSettings()),
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        using var host = new Form { ClientSize = new Size(1000, 760), ShowInTaskbar = false, Opacity = 0 };
        settings.Dock = DockStyle.Fill;
        host.Controls.Add(settings);
        host.Show();
        var navigation = (AntdUI.Segmented)typeof(SettingForm)
            .GetField("_settingsSegmented", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        navigation.SelectIndex = 4;
        System.Windows.Forms.Application.DoEvents();
        var notes = Children(settings).OfType<AntdUI.Label>()
            .Where(note => note.Name == "aiDescription" && note.Parent is TableLayoutPanel table && table.ColumnCount == 2).ToArray();
        Assert.True(notes.Length >= 6);
        var opgg = notes.Single(note => note.Parent?.Parent?.Name == "aiSelectionCard");
        int narrowWidth = opgg.Width;
        int narrowHeight = opgg.Height;
        LOL_GameAssistant.Helper.UiTheme.Apply(settings);
        foreach (int width in new[] { 1600, 900, 1600 })
        {
            host.ClientSize = new Size(width, 760);
            System.Windows.Forms.Application.DoEvents();
            foreach (var note in notes)
            {
                var table = (TableLayoutPanel)note.Parent!;
                Assert.InRange(table.ClientSize.Width - note.Right - note.Margin.Right, 0, 4);
                Assert.True(note.Width > 220, $"Description remained narrow beside the fixed directory: {note.Bounds}");
                Assert.True(note.Height >= note.GetPreferredSize(new Size(note.Width, 0)).Height);
            }
            if (width == 1600)
            {
                Assert.True(opgg.Width > narrowWidth + 400);
                Assert.True(opgg.Height <= narrowHeight);
                if (Environment.GetEnvironmentVariable("ANTD_UI_PREVIEWS") is string folder)
                {
                    Directory.CreateDirectory(folder);
                    using var image = new Bitmap(settings.Width, settings.Height);
                    settings.DrawToBitmap(image, settings.ClientRectangle);
                    image.Save(Path.Combine(folder, "settings-ai.png"));
                }
            }
        }
    });

    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
