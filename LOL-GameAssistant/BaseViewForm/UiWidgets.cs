using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 渐变圆角面板。
    /// </summary>
    public class GradientPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color StartColor { get; set; } = Color.Transparent;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color EndColor { get; set; } = Color.Transparent;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Angle { get; set; } = 0f;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = 8;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DrawBorder { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.Transparent;

        public GradientPanel()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, CornerRadius);

            if (StartColor.A > 0 || EndColor.A > 0)
            {
                using var brush = new LinearGradientBrush(rect, StartColor, EndColor, Angle);
                e.Graphics.FillPath(brush, path);
            }

            if (DrawBorder && BorderColor.A > 0)
            {
                using var pen = new Pen(BorderColor, 1f);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 圆形头像（带描边）。
    /// </summary>
    public class RoundPictureBox : PictureBox
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderWidth { get; set; } = 2;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.FromArgb(220, 255, 255, 255);

        public RoundPictureBox()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = PictureBoxSizeMode.Zoom;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            int radius = Math.Min(rect.Width, rect.Height) / 2;
            using var path = GradientPanel.RoundedRect(rect, radius);

            g.SetClip(path);
            if (Image != null)
            {
                g.DrawImage(Image, rect);
            }
            else
            {
                using var placeholder = new SolidBrush(Color.FromArgb(226, 229, 234));
                g.FillPath(placeholder, path);
            }
            g.ResetClip();

            if (BorderWidth > 0 && BorderColor.A > 0)
            {
                using var pen = new Pen(BorderColor, BorderWidth);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 呼吸脉冲状态点。
    /// </summary>
    public class PulseDot : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private double _phase;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DotColor { get; set; } = Color.FromArgb(76, 175, 80);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MaxRadius { get; set; } = 9;

        public PulseDot()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(18, 18);
            _timer = new System.Windows.Forms.Timer { Interval = 40 };
            _timer.Tick += (_, _) =>
            {
                _phase = (_phase + 0.09) % (Math.PI * 2);
                Invalidate();
            };
            _timer.Start();
            Disposed += (_, _) => _timer.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = Width / 2;
            int cy = Height / 2;
            double pulse = (Math.Sin(_phase) + 1) / 2;

            int ripple = MaxRadius + (int)(pulse * 5);
            using var rippleBrush = new SolidBrush(Color.FromArgb((int)(70 * (1 - pulse)), DotColor));
            g.FillEllipse(rippleBrush, cx - ripple, cy - ripple, ripple * 2, ripple * 2);

            using var coreBrush = new SolidBrush(DotColor);
            g.FillEllipse(coreBrush, cx - 3, cy - 3, 6, 6);
        }
    }

    /// <summary>
    /// 加载微光动画占位。
    /// </summary>
    public class ShimmerPanel : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private float _offset;

        public ShimmerPanel()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(238, 240, 244);
            _timer = new System.Windows.Forms.Timer { Interval = 30 };
            _timer.Tick += (_, _) =>
            {
                _offset += 9f;
                if (_offset > Width + 160) _offset = -160;
                Invalidate();
            };
            _timer.Start();
            Disposed += (_, _) => _timer.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            int barHeight = 14;
            int y = 10;
            for (int i = 0; i < 5; i++)
            {
                DrawShimmerBar(g, y, (Width * 0.75f) - (i * 20), barHeight);
                y += barHeight + 12;
            }
        }

        private void DrawShimmerBar(Graphics g, int y, float width, int height)
        {
            using var baseBrush = new SolidBrush(Color.FromArgb(226, 229, 234));
            g.FillRectangle(baseBrush, 10, y, Math.Max(40f, width), height);

            var rect = new Rectangle((int)_offset - 70, y, 140, height);
            using var shine = new LinearGradientBrush(
                rect,
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(170, 255, 255, 255),
                LinearGradientMode.Horizontal);
            g.FillRectangle(shine, rect);
        }
    }

    /// <summary>
    /// 轻量动画工具：缓动、展开、滑入、悬停颜色过渡。
    /// </summary>
    public static class UiAnimation
    {
        public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

        public static Color LerpColor(Color from, Color to, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * t),
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        /// <summary>
        /// 高度展开动画（适合 FlowLayoutPanel 中的卡片，逐帧 PerformLayout）。
        /// </summary>
        public static void ExpandIn(Control control, int fromHeight = 0, int durationMs = 280, int delayMs = 0)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 15 };
            int targetHeight = control.Height;
            int startHeight = fromHeight;
            control.Height = startHeight;
            DateTime startTime = DateTime.UtcNow.AddMilliseconds(delayMs);

            timer.Tick += (_, _) =>
            {
                double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed < 0) return;
                double t = Math.Min(1, elapsed / durationMs);
                control.Height = startHeight + (int)((targetHeight - startHeight) * EaseOutCubic(t));
                control.Parent?.PerformLayout();
                if (t >= 1)
                {
                    timer.Stop();
                    timer.Dispose();
                    control.Height = targetHeight;
                    control.Parent?.PerformLayout();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 水平滑入动画（适用于手工定位的控件）。
        /// </summary>
        public static void SlideIn(Control control, int fromOffsetX = -18, int durationMs = 220, int delayMs = 0)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 15 };
            int targetX = control.Left;
            int startX = targetX + fromOffsetX;
            control.Left = startX;
            DateTime startTime = DateTime.UtcNow.AddMilliseconds(delayMs);

            timer.Tick += (_, _) =>
            {
                double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed < 0) return;
                double t = Math.Min(1, elapsed / durationMs);
                control.Left = startX + (int)((targetX - startX) * EaseOutCubic(t));
                if (t >= 1)
                {
                    timer.Stop();
                    timer.Dispose();
                    control.Left = targetX;
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 为控件及其所有子控件挂载悬停背景色过渡。
        /// </summary>
        public static void AttachHoverBackColor(Control root, Color normal, Color hover, int durationMs = 160)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 15 };
            DateTime start = DateTime.UtcNow;
            Color from = normal;
            Color to = normal;
            bool active = false;

            timer.Tick += (_, _) =>
            {
                if (!active) return;
                double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / durationMs);
                root.BackColor = LerpColor(from, to, EaseOutCubic(t));
                if (t >= 1)
                {
                    active = false;
                    timer.Stop();
                }
            };

            void Enter(object? s, EventArgs e)
            {
                from = root.BackColor;
                to = hover;
                start = DateTime.UtcNow;
                active = true;
                timer.Start();
            }

            void Leave(object? s, EventArgs e)
            {
                from = root.BackColor;
                to = normal;
                start = DateTime.UtcNow;
                active = true;
                timer.Start();
            }

            root.MouseEnter += Enter;
            root.MouseLeave += Leave;
            AttachToChildren(root, c => { c.MouseEnter += Enter; c.MouseLeave += Leave; });
            root.Disposed += (_, _) => timer.Dispose();
        }

        private static void AttachToChildren(Control root, Action<Control> attach)
        {
            foreach (Control child in root.Controls)
            {
                attach(child);
                AttachToChildren(child, attach);
            }
        }
    }
}
