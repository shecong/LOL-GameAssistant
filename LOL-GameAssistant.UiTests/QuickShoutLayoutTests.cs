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
    [InlineData(980)]
    [InlineData(1440)]
    public void OptionInputsAreVisibleAndAligned(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ClientSize = new Size(width, 760), StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var editor = CreateEditor();
        host.Controls.Add(editor);
        UiTheme.SetMode("Dark");
        UiTheme.Apply(host);
        host.Show();
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
        var phrases = Field<ListBox>(editor, "_phrases");
        Assert.True(phrases.Height >= 100);
        if (width == 1440)
        {
            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            editor.DrawToBitmap(bitmap, editor.ClientRectangle);
            bitmap.Save(Path.Combine(Path.GetTempPath(), "quick-shout-layout.png"));
        }
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

    private static QuickShoutForm CreateEditor() => new(null!, (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)),
        (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)), () => Task.FromResult(default(GameShoutSendResult)), () => { });

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
}
