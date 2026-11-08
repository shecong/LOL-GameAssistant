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
        Assert.All(windows,
            type => Assert.Equal(typeof(AntdUI.Window), type.BaseType));
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
        Assert.Same(header, AntdWindowChrome.AddHeader(window));
        Assert.Single(window.Controls.OfType<AntdUI.PageHeader>());
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void CompanionHasAntdHeaderAndKeepsItsChromeWhileFollowingClient(string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(theme);
            using var fixture = new ChampSelectCompanionTests.Fixture(() => new Rectangle(350, 100, 1000, 650));
            var companion = fixture.Form;
            companion.StartTracking();
            System.Windows.Forms.Application.DoEvents();
            var header = Assert.Single(companion.Controls.OfType<AntdUI.PageHeader>());
            Assert.Equal("companionWindowHeader", header.Name);
            Assert.True(header.ShowButton);
            Assert.False(header.MaximizeBox);
            Assert.False(header.MinimizeBox);
            var cards = (Control)typeof(ChampSelectCompanionForm).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(companion)!;
            Assert.True(cards.Top >= header.Bottom);
            var size = companion.ClientSize;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                AssertCustomChrome(companion);
                Assert.NotEqual(0, GetWindowLong(companion.Handle, -20) & 0x08000000);
                typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(companion, null);
                Assert.Equal(size, companion.ClientSize);
                companion.Hide(); companion.SyncClientVisibility();
            }
            if (Environment.GetEnvironmentVariable("WINDOW_AUDIT_PREVIEWS") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                using var bitmap = new Bitmap(companion.Width, companion.Height);
                companion.DrawToBitmap(bitmap, companion.ClientRectangle);
                bitmap.Save(Path.Combine(folder, $"Companion-{theme}.png"));
            }
        }
        finally { UiTheme.SetMode(previous); }
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
        // 通过真实标题控件和系统样式验收，不依赖不同 AntdUI 版本的私有字段。
        var header = Assert.Single(window.Controls.OfType<AntdUI.PageHeader>());
        Assert.True(header.Visible);
        Assert.True(window.ClientRectangle.Contains(header.Bounds));
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

    private sealed class PendingScanner : IAugmentScanner
    {
        public TaskCompletionSource<AugmentScanResult> Completion { get; } = new();
        public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) => Completion.Task;
    }

}
