using System.Reflection;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class QuickShoutLayoutTests
{
    [Theory]
    [InlineData(820)]
    [InlineData(980)]
    [InlineData(1440)]
    public void OptionInputsAreVisibleAndAligned(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ClientSize = new Size(width, 760), StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var editor = CreateEditor();
        if (width == 820) editor.Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold);
        host.Controls.Add(editor);
        UiTheme.SetMode("Dark");
        UiTheme.Apply(host);
        host.Show();
        System.Windows.Forms.Application.DoEvents();
        var toggle = Children(editor).OfType<AntdUI.Button>().Single(button => button.Name == "quickShoutToggleOptions");
        toggle.PerformClick();
        System.Windows.Forms.Application.DoEvents();
        var inputs = new[] { "_builtInHotkey", "_customHotkey", "_batchHotkey", "_kdaHotkey" }
            .Select(name => Field<AntdUI.Input>(editor, name)).ToArray();
        foreach (var input in inputs)
        {
            Assert.True(input.Visible);
            Assert.InRange(input.Height, 32, 42);
            Assert.True(input.Width >= 150);
            Assert.True(input.Parent!.ClientRectangle.Contains(input.Bounds));
            Assert.Equal(UiTheme.Palette.TextPrimary, input.ForeColor);
        }
        Assert.Single(inputs.Select(input => input.Left).Distinct());
        var cooldown = Field<AntdUI.InputNumber>(editor, "_minimumInterval");
        Assert.True(cooldown.Visible);
        Assert.True(cooldown.Height >= 32);
        Assert.True(cooldown.Parent!.ClientRectangle.Contains(cooldown.Bounds), $"Cooldown {cooldown.Bounds}, parent {cooldown.Parent.ClientRectangle}");
        var phrases = Field<AntdUI.Table>(editor, "_phrases");
        Assert.True(phrases.Height >= 180);
        if (width == 1440 || width == 820)
        {
            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            editor.DrawToBitmap(bitmap, editor.ClientRectangle);
            bitmap.Save(Path.Combine(Path.GetTempPath(), $"quick-shout-layout-{width}.png"));
        }
    });

    [Theory]
    [InlineData(1100, 760)]
    [InlineData(820, 560)]
    public void PhraseListGetsMoreSpaceAndSettingsRemainReachable(int width, int height) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ClientSize = new Size(width, height), ShowInTaskbar = false };
        using var editor = CreateEditor();
        host.Controls.Add(editor); host.Show(); System.Windows.Forms.Application.DoEvents();
        var phrases = Field<AntdUI.Table>(editor, "_phrases");
        var custom = Field<AntdUI.Input>(editor, "_customPhrases");
        var toggle = Children(editor).OfType<AntdUI.Button>().Single(button => button.Name == "quickShoutToggleOptions");
        var viewport = Children(editor).OfType<System.Windows.Forms.Panel>().Single(panel => panel.Name == "quickShoutViewport");
        int collapsedHeight = phrases.Height;
        Assert.True(collapsedHeight >= 250, $"词库列表高度：{collapsedHeight}");
        Assert.True(custom.Height >= 250);
        Assert.False(Field<AntdUI.Input>(editor, "_builtInHotkey").Visible);
        if (Environment.GetEnvironmentVariable("QUICK_SHOUT_PREVIEWS") is string folder)
        {
            Directory.CreateDirectory(folder);
            using var preview = new Bitmap(editor.Width, editor.Height);
            editor.DrawToBitmap(preview, editor.ClientRectangle);
            preview.Save(Path.Combine(folder, $"quick-shout-{width}x{height}.png"));
        }
        toggle.PerformClick(); System.Windows.Forms.Application.DoEvents();
        Assert.True(Field<AntdUI.Input>(editor, "_builtInHotkey").Visible);
        Assert.True(phrases.Height >= 180);
        Assert.True(viewport.DisplayRectangle.Height >= 680);
        toggle.PerformClick(); System.Windows.Forms.Application.DoEvents();
        Assert.Equal(collapsedHeight, phrases.Height);
    });

    [Fact]
    public void PressingCombinationCapturesAndPersistsIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var editor = CreateEditor();
        var input = Field<AntdUI.Input>(editor, "_kdaHotkey");
        var args = new KeyEventArgs(Keys.Control | Keys.Alt | Keys.K);
        typeof(QuickShoutForm).GetMethod("CaptureHotkey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [input, args]);
        Assert.Equal("Ctrl+Alt+K", input.Text);
        Assert.True(args.SuppressKeyPress);
        var settings = new AssistantSettings();
        editor.WriteSettings(settings);
        Assert.Equal("Ctrl+Alt+K", settings.GameKdaHotkey);
    });

    [Fact]
    public void AntdPhraseRowsToggleMultipleSelectionAndKeepLibraryOrder() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var editor = CreateEditor();
        using var host = new Form { ClientSize = new Size(1100, 780), ShowInTaskbar = false };
        editor.LoadSettings(new AssistantSettings());
        host.Controls.Add(editor); host.Show(); System.Windows.Forms.Application.DoEvents();
        var table = Field<AntdUI.Table>(editor, "_phrases");
        Field<AntdUI.Button>(editor, "_multiSelect").PerformClick();
        table.SelectedIndexs = [];
        void ClickRow(int row)
        {
            var cell = table.CellRectangle(row, 1);
            Assert.True(cell.Width > 0 && cell.Height > 0);
            var args = new MouseEventArgs(MouseButtons.Left, 1, cell.Left + 12, cell.Top + cell.Height / 2, 0);
            typeof(AntdUI.Table).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(table, [args]);
            typeof(AntdUI.Table).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(table, [args]);
        }
        ClickRow(3); ClickRow(1);
        var saved = new AssistantSettings(); editor.WriteSettings(saved);
        Assert.Equal(["D|我去支援，请注意地图。", "D|准备打龙，麻烦先做视野。"], saved.QuickShoutSelectedPhrases);
        ClickRow(3); editor.WriteSettings(saved);
        Assert.Equal(["D|我去支援，请注意地图。"], saved.QuickShoutSelectedPhrases);
    });

    private static QuickShoutForm CreateEditor() => new(null!, (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)),
        (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)), () => Task.FromResult(default(GameShoutSendResult)), () => { });

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));
}
