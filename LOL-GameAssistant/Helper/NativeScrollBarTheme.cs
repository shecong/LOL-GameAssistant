using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>Paint native non-client scrollbars without replacing their scrolling and hit testing.</summary>
internal static class NativeScrollBarTheme
{
    private static readonly ConditionalWeakTable<ScrollableControl, ThemeWindow> Windows = new();

    public static void Apply(ScrollableControl control, ThemePalette palette) =>
        Windows.GetValue(control, key => new ThemeWindow(key)).Apply(palette);

    private sealed class ThemeWindow : NativeWindow
    {
        private readonly ScrollableControl _control;
        private ThemePalette _palette = UiTheme.Palette;

        public ThemeWindow(ScrollableControl control)
        {
            _control = control;
            control.HandleCreated += (_, _) => Attach();
            control.HandleDestroyed += (_, _) => ReleaseHandle();
            control.Disposed += (_, _) => ReleaseHandle();
            if (control.IsHandleCreated) Attach();
        }

        private void Attach()
        {
            if (Handle == IntPtr.Zero) AssignHandle(_control.Handle);
            RefreshFrame();
        }

        public void Apply(ThemePalette palette)
        {
            _palette = palette;
            RefreshFrame();
        }

        private void RefreshFrame()
        {
            if (Handle != IntPtr.Zero)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x001 | 0x100 | 0x400); // INVALIDATE | UPDATENOW | FRAME
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (!_control.IsDisposed && Handle != IntPtr.Zero &&
                message.Msg is 0x85 or 0x86 or 0xF or 0x114 or 0x115 or 0xA0)
                PaintScrollbars();
        }

        private void PaintScrollbars()
        {
            if ((!_control.VerticalScroll.Visible && !_control.HorizontalScroll.Visible) ||
                !GetWindowRect(Handle, out NativeRect window)) return;
            IntPtr dc = GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using Graphics graphics = Graphics.FromHdc(dc);
                Rectangle vertical = PaintBar(graphics, window, true);
                Rectangle horizontal = PaintBar(graphics, window, false);
                if (!vertical.IsEmpty && !horizontal.IsEmpty)
                {
                    using var brush = new SolidBrush(_palette.SurfaceMuted);
                    graphics.FillRectangle(brush, new Rectangle(vertical.Left, horizontal.Top, vertical.Width, horizontal.Height));
                }
            }
            finally { ReleaseDC(Handle, dc); }
        }

        private Rectangle PaintBar(Graphics graphics, NativeRect window, bool vertical)
        {
            if (vertical ? !_control.VerticalScroll.Visible : !_control.HorizontalScroll.Visible) return Rectangle.Empty;
            var info = new ScrollBarInfo { Size = Marshal.SizeOf<ScrollBarInfo>(), State = new uint[6] };
            if (!GetScrollBarInfo(Handle, vertical ? -5 : -6, ref info) ||
                (info.State[0] & (0x8000 | 0x10000)) != 0) return Rectangle.Empty;
            var bounds = Rectangle.FromLTRB(info.Bounds.Left - window.Left, info.Bounds.Top - window.Top,
                info.Bounds.Right - window.Left, info.Bounds.Bottom - window.Top);
            if (bounds.Width <= 0 || bounds.Height <= 0) return Rectangle.Empty;
            using var track = new SolidBrush(_palette.SurfaceMuted);
            using var thumb = new SolidBrush(_palette.Border);
            using var arrow = new SolidBrush(_palette.TextSecondary);
            graphics.FillRectangle(track, bounds);
            Rectangle thumbBounds = vertical
                ? new Rectangle(bounds.Left + 3, bounds.Top + info.ThumbTop, Math.Max(1, bounds.Width - 6), info.ThumbBottom - info.ThumbTop)
                : new Rectangle(bounds.Left + info.ThumbTop, bounds.Top + 3, info.ThumbBottom - info.ThumbTop, Math.Max(1, bounds.Height - 6));
            if (thumbBounds.Width > 0 && thumbBounds.Height > 0) graphics.FillRectangle(thumb, thumbBounds);
            int center = vertical ? bounds.Left + bounds.Width / 2 : bounds.Top + bounds.Height / 2;
            int first = (vertical ? bounds.Top : bounds.Left) + info.ArrowSize / 2;
            int last = (vertical ? bounds.Bottom : bounds.Right) - info.ArrowSize / 2;
            Point[] firstArrow = vertical
                ? [new Point(center, first - 2), new Point(center - 3, first + 2), new Point(center + 3, first + 2)]
                : [new Point(first - 2, center), new Point(first + 2, center - 3), new Point(first + 2, center + 3)];
            Point[] lastArrow = vertical
                ? [new Point(center, last + 2), new Point(center - 3, last - 2), new Point(center + 3, last - 2)]
                : [new Point(last + 2, center), new Point(last - 2, center - 3), new Point(last - 2, center + 3)];
            graphics.FillPolygon(arrow, firstArrow);
            graphics.FillPolygon(arrow, lastArrow);
            return bounds;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollBarInfo
    {
        public int Size;
        public NativeRect Bounds;
        public int ArrowSize, ThumbTop, ThumbBottom, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] State;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr window, int objectId, ref ScrollBarInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
}
