using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>Paint native non-client scrollbars without replacing their scrolling and hit testing.</summary>
internal static class NativeScrollBarTheme
{
    private static readonly ConditionalWeakTable<ScrollableControl, ThemeWindow> Windows = new();

    /// <summary>将当前主题配色应用到目标窗口的原生滚动条。</summary>
    public static void Apply(ScrollableControl control, ThemePalette palette) =>
        Windows.GetValue(control, key => new ThemeWindow(key)).Apply(palette);

    /// <summary>绑定原生滚动条窗口消息并应用主题绘制。</summary>
    private sealed class ThemeWindow : NativeWindow
    {
        private readonly ScrollableControl _control;
        private ThemePalette _palette = UiTheme.Palette;
        private readonly System.Windows.Forms.Timer _interactionTimer = new() { Interval = 40 };
        private int _hoveredBar;

        /// <summary>初始化 ThemeWindow 的实例状态，并保存传入的依赖或数据。</summary>
        public ThemeWindow(ScrollableControl control)
        {
            _control = control;
            _interactionTimer.Tick += (_, _) =>
            {
                if (Handle == IntPtr.Zero || control.IsDisposed || !control.Visible)
                {
                    _interactionTimer.Stop();
                    return;
                }
                _hoveredBar = GetHoveredBar();
                if (_hoveredBar == 0 && GetCapture() != Handle) _interactionTimer.Stop();
                PaintScrollbars();
            };
            control.HandleCreated += (_, _) => Attach();
            control.HandleDestroyed += (_, _) => { _interactionTimer.Stop(); ReleaseHandle(); };
            control.Disposed += (_, _) => { _interactionTimer.Dispose(); ReleaseHandle(); };
            if (control.IsHandleCreated) Attach();
        }

        /// <summary>绑定目标控件或窗口的相关事件。</summary>
        private void Attach()
        {
            if (Handle == IntPtr.Zero) AssignHandle(_control.Handle);
            // Prevent the visual-style hover animation from painting a light frame after our paint.
            SetWindowTheme(Handle, "", "");
            RefreshFrame();
        }

        /// <summary>将当前主题配色应用到目标窗口的原生滚动条。</summary>
        public void Apply(ThemePalette palette)
        {
            _palette = palette;
            RefreshFrame();
        }

        /// <summary>请求窗口重新绘制非客户区。</summary>
        private void RefreshFrame()
        {
            if (Handle != IntPtr.Zero)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x001 | 0x100 | 0x400); // INVALIDATE | UPDATENOW | FRAME
        }

        /// <summary>处理当前窗口关注的原生消息，其余消息继续交给基类。</summary>
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0xA0 or 0xA1 && message.WParam.ToInt64() is 6 or 7)
                _interactionTimer.Start();
            base.WndProc(ref message);
            if (!_control.IsDisposed && Handle != IntPtr.Zero &&
                message.Msg is 0x85 or 0x86 or 0xF or 0x114 or 0x115 or 0xA0 or 0xA1 or 0xA2
                    or 0x2A2 or 0x200 or 0x202 or 0x215 or 0x31A)
            {
                _hoveredBar = GetHoveredBar();
                PaintScrollbars();
            }
        }

        /// <summary>根据鼠标位置判断当前悬停的滚动条。</summary>
        private int GetHoveredBar()
        {
            Point cursor = Cursor.Position;
            foreach (int objectId in new[] { -5, -6 })
            {
                var info = new ScrollBarInfo { Size = Marshal.SizeOf<ScrollBarInfo>(), State = new uint[6] };
                if (GetScrollBarInfo(Handle, objectId, ref info) && (info.State[0] & (0x8000 | 0x10000)) == 0 &&
                    Rectangle.FromLTRB(info.Bounds.Left, info.Bounds.Top, info.Bounds.Right, info.Bounds.Bottom).Contains(cursor))
                    return objectId;
            }
            return 0;
        }

        /// <summary>按主题配色绘制窗口中的原生滚动条。</summary>
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

        /// <summary>绘制指定方向滚动条的轨道和滑块。</summary>
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
            using var thumb = new SolidBrush(_hoveredBar == (vertical ? -5 : -6)
                ? _palette.TextSecondary : _palette.Border);
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

    /// <summary>供原生窗口和滚动条接口使用的矩形结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    /// <summary>原生滚动条的区域、状态和滑块信息。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollBarInfo
    {
        public int Size;
        public NativeRect Bounds;
        public int ArrowSize, ThumbTop, ThumbBottom, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] State;
    }
    /// <summary>调用 Windows API 读取窗口在屏幕上的边界。</summary>
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    /// <summary>读取原生滚动条的尺寸和状态信息。</summary>
    [DllImport("user32.dll")] private static extern bool GetScrollBarInfo(IntPtr window, int objectId, ref ScrollBarInfo info);
    /// <summary>取得包含非客户区的窗口设备上下文。</summary>
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    /// <summary>归还从窗口或屏幕取得的设备上下文。</summary>
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    /// <summary>请求窗口重绘指定区域。</summary>
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
    /// <summary>读取当前持有鼠标捕获的窗口。</summary>
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    /// <summary>设置目标窗口的原生主题绘制方式。</summary>
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
}
