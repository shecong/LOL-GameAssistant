using System.Runtime.InteropServices;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class TclsPasswordInputTests
{
    [Fact]
    public void FocusedKeyboardInputPreservesCasePunctuationAndSpaces() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new Form { ClientSize = new Size(320, 120), ShowInTaskbar = false };
        using var field = new TextBox { Location = new Point(20, 20), Width = 260, UseSystemPasswordChar = true, Text = "replace-me" };
        form.Controls.Add(field);form.Show();field.Focus();System.Windows.Forms.Application.DoEvents();
        var window = form.Handle;
        var point = field.PointToScreen(new Point(20, 10));
        Assert.True(TclsPasswordKeyboard.HasFocusedEditor(window, point));
        Assert.False(TclsPasswordKeyboard.HasFocusedEditor(window, form.PointToScreen(new Point(10, 100))));
        const string expected = " Ab9!@#._-+ zZ ";
        var task = Task.Run(() => TclsPasswordKeyboard.Replace(window, expected, () =>
        {
            Assert.True(TclsPasswordKeyboard.HasFocusedEditor(window, point));
        }, default));
        SecondPhaseRegressionTests.PumpUntil(() => task.IsCompleted);
        Assert.True(task.GetAwaiter().GetResult());
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(expected, field.Text);
    });

    [Fact]
    public void UnsupportedControlCharactersAreRejectedBeforeAnyInput()
    {
        Assert.Null(TclsPasswordKeyboard.Plan("abc\n", GetKeyboardLayout(0), false));
        var lower = TclsPasswordKeyboard.Plan("aA", GetKeyboardLayout(0), false)!;
        var caps = TclsPasswordKeyboard.Plan("aA", GetKeyboardLayout(0), true)!;
        Assert.Equal(lower[0].ScanCode, caps[0].ScanCode);
        Assert.False(lower[0].Shift);Assert.True(lower[1].Shift);
        Assert.True(caps[0].Shift);Assert.False(caps[1].Shift);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OnlyCompleteMaskRowConfirmsPassword(double scale)
    {
        using var image = new Bitmap((int)(190 * scale), (int)(20 * scale));
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(10, 18, 25));
        graphics.FillRectangle(Brushes.White, (int)(3 * scale), (int)(2 * scale), Math.Max(1, (int)scale), (int)(16 * scale));
        Assert.False(TclsPasswordFeedback.IsComplete(image, 1)); // Caret only.
        for (int i = 0; i < 8; i++)
            graphics.FillEllipse(Brushes.White, (int)((8 + i * 9) * scale), (int)(7 * scale), (int)(5 * scale), (int)(5 * scale));
        Assert.True(TclsPasswordFeedback.IsComplete(image, 8));
        Assert.False(TclsPasswordFeedback.IsComplete(image, 9));
        Assert.False(TclsPasswordFeedback.IsComplete(image, 0));
    }

    [Fact]
    public void FailedPasswordInputNeverAdvancesToRegionSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new Form { Text = "模拟登录", ShowInTaskbar = false };
        form.Show();System.Windows.Forms.Application.DoEvents();
        var session = new TclsLoginSession { CredentialsAttempted = true };
        var handle = form.Handle;
        var task = Task.Run(() =>
        {
            using var automation = new FlaUI.UIA3.UIA3Automation();
            return TclsLoginAutomation.Step([automation.FromHandle(handle)], session, "fake", "fake", "艾欧尼亚", default);
        });
        SecondPhaseRegressionTests.PumpUntil(() => task.IsCompleted);
        var step = task.GetAwaiter().GetResult();
        Assert.True(step.Pause);Assert.Contains("密码", step.Message);
        Assert.False(session.Submitted);Assert.False(session.RegionSelected);
    });

    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
}
