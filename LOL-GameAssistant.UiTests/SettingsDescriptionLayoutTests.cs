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
        var notes = Children(settings).OfType<System.Windows.Forms.Label>()
            .Where(note => note.Parent is TableLayoutPanel table && table.ColumnCount == 2).ToArray();
        Assert.True(notes.Length >= 6);
        var opgg = notes.Single(note => note.Text.StartsWith("开启后，助手会"));
        int narrowWidth = opgg.Width;
        int narrowHeight = opgg.Height;
        foreach (int width in new[] { 1600, 900, 1600 })
        {
            host.ClientSize = new Size(width, 760);
            System.Windows.Forms.Application.DoEvents();
            foreach (var note in notes)
            {
                var table = (TableLayoutPanel)note.Parent!;
                Assert.InRange(table.ClientSize.Width - note.Right - note.Margin.Right, 0, 4);
                Assert.True(note.Width > 300, $"Description remained narrow: {note.Bounds}");
                Assert.True(note.Height >= note.GetPreferredSize(new Size(note.Width, 0)).Height);
            }
            if (width == 1600)
            {
                Assert.True(opgg.Width > narrowWidth + 400);
                Assert.True(opgg.Height <= narrowHeight);
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
