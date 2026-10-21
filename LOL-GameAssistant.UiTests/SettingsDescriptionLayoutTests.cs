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
