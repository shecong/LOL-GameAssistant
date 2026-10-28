using System.Reflection;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UnifiedAntdUiTests
{
    [Fact]
    public void IndependentWindowsFollowThemeChanges() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            using var window = new AntdUI.Window { ShowInTaskbar = false, Size = new Size(400, 250) };
            AntdWindowChrome.AddHeader(window);
            var select = new AntdUI.Select { Dock = DockStyle.Top, Height = 40 };
            var table = new AntdUI.Table { Dock = DockStyle.Fill };
            window.Controls.Add(table); window.Controls.Add(select); window.Show();
            foreach (string mode in new[] { "Dark", "Light", "Dark" })
            {
                UiTheme.SetMode(mode); UiTheme.Apply(window);
                Assert.Equal(mode == "Dark" ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light, select.ColorScheme);
                Assert.Equal(select.ColorScheme, table.ColorScheme);
                Assert.Equal(UiTheme.Palette.TextPrimary, select.ForeColor);
            }
            UiTheme.SetMode("Light"); // The open window updates without an explicit Apply.
            Assert.Equal(AntdUI.TAMode.Light, select.ColorScheme);
            Assert.Equal(UiTheme.Palette.SurfaceRaised, table.BackColor);
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void ApplicationFieldsDoNotUseNativeStandardWidgets()
    {
        Type[] native = [typeof(Button), typeof(Label), typeof(TextBox), typeof(ComboBox), typeof(CheckBox),
            typeof(RadioButton), typeof(ListBox), typeof(ListView), typeof(DataGridView), typeof(RichTextBox),
            typeof(PictureBox), typeof(ProgressBar), typeof(ToolTip), typeof(ContextMenuStrip), typeof(TabControl)];
        var offenders = typeof(GameMain).Assembly.GetTypes().Where(t => t.Namespace == typeof(EnemyJungleRouteForm).Namespace || t == typeof(GameMain))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => native.Contains(f.FieldType)).Select(f => $"{t.Name}.{f.Name}: {f.FieldType.Name}"));
        Assert.Empty(offenders);
    }
}
