using LOL_GameAssistant.Helper;
using System.Runtime.InteropServices;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class TaskbarWindowRegistrationTests
{
    [Fact]
    public void StartupHideRestoreAndClose_RegisterOnlyVisibleWindow() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new TestWindow();
        var updates = new List<(nint Handle, bool Visible)>();
        using var registration = new TaskbarWindowRegistration(form, (handle, visible) =>
        {
            updates.Add((handle, visible));
            return 0;
        });
        Assert.Empty(updates);
        form.Show();
        nint handle = form.Handle;
        Assert.Equal(new[] { (handle, true) }, updates);
        registration.Synchronize();
        Assert.Single(updates);
        form.Hide();
        form.Show();
        form.Close();
        Assert.Equal(new[] { (handle, true), (handle, false), (handle, true), (handle, false) }, updates);
    });

    [Fact]
    public void HandleRecreation_RemovesOldRegistrationAndRegistersNewWindow() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new TestWindow();
        var updates = new List<bool>();
        using var registration = new TaskbarWindowRegistration(form, (_, visible) => { updates.Add(visible); return 0; });
        form.Show();
        form.RebuildHandle();
        Assert.Equal(new[] { true, false, true }, updates);
    });

    [Fact]
    public void ExplorerRestart_ReRegistersVisibleWindowOnly() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new TestWindow();
        int additions = 0;
        using var registration = new TaskbarWindowRegistration(form, (_, visible) => { if (visible) additions++; return 0; });
        form.Show();
        SendMessage(form.Handle, TaskbarWindowRegistration.TaskbarCreatedMessage, 0, 0);
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(2, additions);
        form.Hide();
        SendMessage(form.Handle, TaskbarWindowRegistration.TaskbarCreatedMessage, 0, 0);
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(2, additions);
    });

    [Fact]
    public void FailedRegistration_CanRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new TestWindow();
        bool ready = false;
        int attempts = 0;
        using var registration = new TaskbarWindowRegistration(form, (_, visible) =>
        {
            if (visible) attempts++;
            return ready ? 0 : unchecked((int)0x80004005);
        });
        form.Show();
        int failedAttempts = attempts;
        ready = true;
        registration.Synchronize();
        registration.Synchronize();
        Assert.Equal(failedAttempts + 1, attempts);
    });

    [Fact]
    public void WindowsShell_AcceptsRegistrationAndRemoval() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new TestWindow { Icon = AppIcon.Shared };
        form.Show();
        Assert.Equal(0, TaskbarWindowRegistration.UpdateShell(form.Handle, true));
        Assert.Equal(0, TaskbarWindowRegistration.UpdateShell(form.Handle, false));
    });

    private sealed class TestWindow : AntdUI.Window
    {
        public TestWindow()
        {
            Text = "Taskbar registration test";
            ShowInTaskbar = true;
            Opacity = 0;
            Size = new Size(200, 100);
        }
        public void RebuildHandle() => RecreateHandle();
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
}
