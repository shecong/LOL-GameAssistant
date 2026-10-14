using System.Runtime.InteropServices;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class NativeScrollBarThemeTests
{
    [Theory]
    [InlineData("Panel")]
    [InlineData("FlowLayoutPanel")]
    [InlineData("MatchScrollPanel")]
    public void MouseWheelKeepsNativeTracksThemed(string controlType) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ClientSize = new Size(430, 260), ShowInTaskbar = false };
        using ScrollableControl panel = controlType switch
        {
            "FlowLayoutPanel" => new FlowLayoutPanel(),
            "MatchScrollPanel" => new MatchScrollPanel(),
            _ => new Panel()
        };
        panel.Dock = DockStyle.Fill;
        panel.AutoScroll = true;
        panel.AutoScrollMinSize = new Size(800, 900);
        host.Controls.Add(panel);
        host.Show();
        System.Windows.Forms.Application.DoEvents();
        string originalMode = UiTheme.Mode;
        try
        {
            foreach (string mode in new[] { "Dark", "Light", "Dark" })
            {
                UiTheme.SetMode(mode);
                UiTheme.Apply(panel);
                panel.AutoScrollPosition = Point.Empty;
                int maximum = panel.VerticalScroll.Maximum;
                Point cursor = panel.PointToScreen(new Point(50, 50));
                IntPtr coordinates = new((cursor.Y << 16) | (cursor.X & 0xFFFF));
                foreach (int message in new[] { 0x20A, 0x20E }) // WM_MOUSEWHEEL / WM_MOUSEHWHEEL
                {
                    SendMessage(panel.Handle, message, new IntPtr(-120 << 16), coordinates);
                    if (message == 0x20A) Assert.True(panel.AutoScrollPosition.Y < 0);
                    Assert.Equal(maximum, panel.VerticalScroll.Maximum);
                    AssertTrackColors(panel);
                    var deadline = DateTime.UtcNow.AddMilliseconds(100);
                    while (DateTime.UtcNow < deadline)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    AssertTrackColors(panel);
                }
            }
        }
        finally { UiTheme.SetMode(originalMode); }
    });

    private static void AssertTrackColors(ScrollableControl panel)
    {
        Assert.True(panel.VerticalScroll.Visible);
        Assert.True(panel.HorizontalScroll.Visible);
        IntPtr dc = GetWindowDC(panel.Handle);
        try
        {
            Assert.Equal(UiTheme.Palette.SurfaceMuted.ToArgb() & 0xFFFFFF,
                ToRgb(GetPixel(dc, panel.ClientSize.Width + 1, 50)));
            Assert.Equal(UiTheme.Palette.SurfaceMuted.ToArgb() & 0xFFFFFF,
                ToRgb(GetPixel(dc, 50, panel.ClientSize.Height + 1)));
        }
        finally { ReleaseDC(panel.Handle, dc); }
    }

    [Fact]
    public void NativeTracksFollowThemeWithoutChangingScrollRange() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new Form { ClientSize = new Size(430, 260), ShowInTaskbar = false };
        using var panel = new MatchScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(new Panel { Size = new Size(800, 900) });
        host.Controls.Add(panel);
        host.Show();
        System.Windows.Forms.Application.DoEvents();
        string originalMode = UiTheme.Mode;
        int maximum = panel.VerticalScroll.Maximum;
        try
        {
            foreach (string mode in new[] { "Dark", "Light", "Dark" })
            {
                UiTheme.SetMode(mode);
                UiTheme.Apply(panel);
                System.Windows.Forms.Application.DoEvents();
                Assert.True(panel.VerticalScroll.Visible);
                Assert.True(panel.HorizontalScroll.Visible);
                Assert.Equal(maximum, panel.VerticalScroll.Maximum);
                foreach (int hitTest in new[] { 7, 6, 0 })
                {
                    Point cursor = panel.PointToScreen(hitTest == 7
                        ? new Point(panel.ClientSize.Width + 1, 50)
                        : new Point(50, panel.ClientSize.Height + 1));
                    IntPtr coordinates = new((cursor.Y << 16) | (cursor.X & 0xFFFF));
                    SendMessage(panel.Handle, hitTest == 0 ? 0x2A2 : 0xA0, new IntPtr(hitTest), coordinates);
                    // Let any native hover animation or delayed paint run before sampling the frame.
                    var deadline = DateTime.UtcNow.AddMilliseconds(100);
                    while (DateTime.UtcNow < deadline)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(5);
                    }
                    IntPtr dc = GetWindowDC(panel.Handle);
                    try
                    {
                        Assert.Equal(UiTheme.Palette.SurfaceMuted.ToArgb() & 0xFFFFFF,
                            ToRgb(GetPixel(dc, panel.ClientSize.Width + 1, 50)));
                        Assert.Equal(UiTheme.Palette.SurfaceMuted.ToArgb() & 0xFFFFFF,
                            ToRgb(GetPixel(dc, 50, panel.ClientSize.Height + 1)));
                    }
                    finally { ReleaseDC(panel.Handle, dc); }
                }
                panel.AutoScrollPosition = new Point(0, 180);
                Assert.Equal(-180, panel.AutoScrollPosition.Y);
            }
        }
        finally { UiTheme.SetMode(originalMode); }
    });

    private static int ToRgb(uint color) => (int)(((color & 255) << 16) | (color & 0xFF00) | ((color >> 16) & 255));
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
