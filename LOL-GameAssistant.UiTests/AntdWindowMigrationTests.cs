using System.Reflection;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class AntdWindowMigrationTests
{
    [Fact]
    public void EveryApplicationWindowUsesAntdUI()
    {
        Type[] windows = typeof(GameMain).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Form).IsAssignableFrom(type)).ToArray();
        Assert.NotEmpty(windows);
        Assert.All(windows, type => Assert.Equal(typeof(AntdUI.Window), type.BaseType));
    }

    [Fact]
    public void HeaderReservesContentSpaceAndTracksWindowTitle() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new AntdUI.Window
        {
            Text = "作用详情", ClientSize = new Size(480, 400), MinimizeBox = false, MaximizeBox = false
        };
        using var body = new Panel { Dock = DockStyle.Fill };
        window.Controls.Add(body);
        var header = AntdWindowChrome.AddHeader(window);
        window.PerformLayout();
        Assert.Equal(FormBorderStyle.None, window.FormBorderStyle);
        Assert.True(header.ShowButton);
        Assert.False(header.MinimizeBox);
        Assert.False(header.MaximizeBox);
        Assert.True(body.Top >= header.Bottom);
        window.Text = "装备作用";
        Assert.Equal(window.Text, header.Text);
    });

    [Fact]
    public void ReopeningAndRecreatingHandleKeepsCustomChrome() => MatchListScrollingTests.OnUiThread(() =>
    {
        for (int opening = 0; opening < 3; opening++)
        {
            using var window = new TestWindow { ClientSize = new Size(480, 400), Opacity = 0, ShowInTaskbar = false };
            AntdWindowChrome.AddHeader(window);
            window.Show();
            AssertCustomChrome(window);
            Size originalSize = window.ClientSize;
            for (int recreation = 0; recreation < 3; recreation++)
            {
                window.RebuildHandle();
                Assert.Equal(originalSize, window.ClientSize);
                AssertCustomChrome(window);
                window.Hide();
                window.Show();
                AssertCustomChrome(window);
            }
            window.WindowState = FormWindowState.Maximized;
            AssertCustomChrome(window);
            window.WindowState = FormWindowState.Normal;
            AssertCustomChrome(window);
            // Form.ShowInTaskbar 会重建句柄，也验证经由 Form 类型调用的路径。
            ((Form)window).ShowInTaskbar = true;
            AssertCustomChrome(window);
            ((Form)window).ShowInTaskbar = false;
            AssertCustomChrome(window);
            window.Close();
        }
    });

    private static void AssertCustomChrome(AntdUI.Window window)
    {
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(FormBorderStyle.None, window.FormBorderStyle);
        Assert.Equal(0, GetWindowLong(window.Handle, -16) & 0x00C00000);
        // 检查 AntdUI 内部非客户区绘制状态，确保重建后未停留在 Destroy 状态。
        Assert.False((bool)typeof(AntdUI.Window).GetField("eNonclient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
    }

    private sealed class TestWindow : AntdUI.Window
    {
        public TestWindow() => AntdWindowChrome.Configure(this);
        public void RebuildHandle() => RecreateHandle();
    }

    [Fact]
    public void TaskbarMinimizeCapabilitySurvivesHandleRecreation() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new TestWindow { ClientSize = new Size(480, 400), Opacity = 0, ShowInTaskbar = true };
        AntdWindowChrome.AddHeader(window);
        window.Show();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int style = GetWindowLong(window.Handle, -16);
            Assert.Equal(0x000A0000, style & 0x000A0000); // WS_SYSMENU | WS_MINIMIZEBOX
            AssertCustomChrome(window);
            SendMessage(window.Handle, 0x0112, 0xF020, 0); // WM_SYSCOMMAND / SC_MINIMIZE
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(FormWindowState.Minimized, window.WindowState);
            SendMessage(window.Handle, 0x0112, 0xF120, 0); // SC_RESTORE
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(FormWindowState.Normal, window.WindowState);
            AssertCustomChrome(window);
            window.RebuildHandle();
        }
    });

    [Fact]
    public void DialogWithoutMinimizeButtonDoesNotAdvertiseMinimizeCapability() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new AntdUI.Window { MinimizeBox = false, MaximizeBox = false };
        AntdWindowChrome.Configure(window);
        Assert.Equal(0, GetWindowLong(window.Handle, -16) & 0x00030000);
    });

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint handle, out uint affinity);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [Fact]
    public void MayhemSidebarKeepsNoActivationWhenCollapsedAndReopened() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new MayhemOverlayForm();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        window.Show();
        Assert.True(GetWindowDisplayAffinity(window.Handle, out uint affinity));
        Assert.Equal(0u, affinity);
        Assert.True(SetWindowPos(window.Handle, (nint)(-2), 0, 0, 0, 0, 0x0013));
        Assert.Equal(0, GetWindowLong(window.Handle, -20) & 0x00000008);
        nint foreground = GetForegroundWindow();
        typeof(MayhemOverlayForm).GetMethod("RaiseWithoutActivation", flags)!.Invoke(window, null);
        Assert.Equal(0x00000008, GetWindowLong(window.Handle, -20) & 0x00000008);
        Assert.Equal(foreground, GetForegroundWindow());
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(0x08000000, GetWindowLong(window.Handle, -20) & 0x08000000);
            typeof(MayhemOverlayForm).GetMethod("Collapse", flags)!.Invoke(window, null);
            Assert.Equal(new Size(210, 46), window.Size);
            Assert.Equal((nint)3, SendMessage(window.Handle, 0x0021, 0, 0));
            window.Hide();
            // 重建句柄后仍要保留禁止激活的原生样式。
            typeof(Control).GetMethod("RecreateHandle", flags)!.Invoke(window, null);
            window.Show();
            Assert.True(GetWindowDisplayAffinity(window.Handle, out affinity));
            Assert.Equal(0u, affinity);
            Assert.Equal(0x08000000, GetWindowLong(window.Handle, -20) & 0x08000000);
            Assert.Equal((nint)3, SendMessage(window.Handle, 0x0021, 0, 0));
            typeof(MayhemOverlayForm).GetMethod("Expand", flags)!.Invoke(window, null);
            Assert.Equal(new Size(420, 650), window.Size);
        }
    });

    [Fact]
    public void ScanningKeepsSidebarVisibleAndCapturePolicyUnchanged() => MatchListScrollingTests.OnUiThread(() =>
    {
        var scanner = new PendingScanner();
        using var window = new MayhemOverlayForm(scanner);
        window.Show();
        Rectangle bounds = window.Bounds;
        nint handle = window.Handle;
        nint foreground = GetForegroundWindow();
        int moves = 0, visibilityChanges = 0;
        window.LocationChanged += (_, _) => moves++;
        window.VisibleChanged += (_, _) => visibilityChanges++;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var scan = (Task)typeof(MayhemOverlayForm).GetMethod("ScanOffersAsync", flags)!
            .Invoke(window, new object[] { false })!;

        Assert.False(scan.IsCompleted);
        Assert.True(window.Visible);
        Assert.Equal(bounds, window.Bounds);
        Assert.True(GetWindowDisplayAffinity(handle, out uint affinity));
        Assert.Equal(0u, affinity);
        scanner.Completion.SetResult(new AugmentScanResult(Array.Empty<int>(), "未识别"));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            System.Windows.Forms.Application.DoEvents();
            return scan.IsCompleted;
        }, TimeSpan.FromSeconds(5)));
        scan.GetAwaiter().GetResult();
        Assert.True(window.Visible);
        Assert.Equal(handle, window.Handle);
        Assert.Equal(bounds, window.Bounds);
        Assert.Equal(foreground, GetForegroundWindow());
        Assert.Equal(0, moves);
        Assert.Equal(0, visibilityChanges);
        Assert.True(GetWindowDisplayAffinity(handle, out affinity));
        Assert.Equal(0u, affinity);
    });

    private sealed class PendingScanner : IAugmentScanner
    {
        public TaskCompletionSource<AugmentScanResult> Completion { get; } = new();
        public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) => Completion.Task;
    }

    [Fact]
    public void TransparentRecommendationKeepsNoActivationAndMousePassthrough() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new RecommendationOverlayForm();
        var flags = (CreateParams)typeof(RecommendationOverlayForm)
            .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal(0x080800A0, flags.ExStyle & 0x080800A0);
        Assert.False(window.ShowInTaskbar);
        Assert.True(window.TopMost);
        Assert.False(window.EnableHitTest);
        Assert.True((bool)typeof(RecommendationOverlayForm)
            .GetProperty("ShowWithoutActivation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
    });
}
