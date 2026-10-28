using ToolTip = LOL_GameAssistant.Helper.UiToolTip;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>离线峡谷地形底图；地形、营地与历史路线共用游戏坐标变换。</summary>
internal sealed class JungleRouteMap : Control
{
    private readonly Bitmap _terrain = CreateTerrain();
    private readonly ToolTip _tip = new() { InitialDelay = 150, ReshowDelay = 100 };
    private readonly AntdUI.Button _reset = new() { Text = "复位", Size = new(46, 30), TabStop = false, WaveSize = 0 };
    private readonly AntdUI.Button _zoomIn = new() { Text = "+", Size = new(28, 30), TabStop = false, WaveSize = 0 };
    private readonly AntdUI.Button _zoomOut = new() { Text = "−", Size = new(28, 30), TabStop = false, WaveSize = 0 };
    private IReadOnlyList<JungleObservation> _points = [];
    private readonly List<RectangleF> _labels = [];
    private bool _predicted, _dragging;
    private float _zoom = 1;
    private PointF _pan;
    private Point _lastMouse;
    private string _hoverText = "";
    private static readonly Color RouteColor = Color.FromArgb(255, 216, 112);
    private static readonly Color Blue = Color.FromArgb(101, 211, 245), Red = Color.FromArgb(244, 135, 151);
    private sealed record Landmark(double X, double Y, string Name, Color Color, bool Buff = false);
    private static readonly Landmark[] Camps = BuildCamps();

    internal JungleRouteMap()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        foreach (var button in new[] { _zoomOut, _zoomIn, _reset })
        {
            button.BackColor = Color.FromArgb(26, 43, 53);
            button.ForeColor = Color.FromArgb(203, 217, 222);
            button.DefaultBorderColor = Color.FromArgb(56, 76, 86);
            Controls.Add(button);
        }
        _reset.Click += (_, _) => ResetView();
        _zoomIn.Click += (_, _) => ZoomAt(ViewCenter, 1.25f);
        _zoomOut.Click += (_, _) => ZoomAt(ViewCenter, .8f);
        _tip.SetToolTip(_reset, "恢复完整地图（也可双击地图）");
    }

    internal void SetRoute(IReadOnlyList<JungleObservation> points, bool predicted)
    {
        _points = points.Where(p => p.Position.IsValid).ToArray();
        _predicted = predicted; _hoverText = ""; _tip.SetToolTip(this, ""); Invalidate();
    }

    private RectangleF Viewport => new(12, 58, Math.Max(1, Width - 24), Math.Max(1, Height - 99));
    private PointF ViewCenter => new(Viewport.Left + Viewport.Width / 2, Viewport.Top + Viewport.Height / 2);
    internal RectangleF MapBounds
    {
        get
        {
            float size = Math.Min(Viewport.Width, Viewport.Height) * _zoom;
            return new(ViewCenter.X - size / 2 + _pan.X, ViewCenter.Y - size / 2 + _pan.Y, size, size);
        }
    }
    internal PointF Project(RiftPoint point) => new(MapBounds.Left + (float)(point.X / 15000 * MapBounds.Width),
        MapBounds.Bottom - (float)(point.Y / 15000 * MapBounds.Height));

    internal void ZoomAt(PointF cursor, float factor)
    {
        var before = MapBounds;
        float next = Math.Clamp(_zoom * factor, 1, 3);
        float ratio = next / _zoom;
        _zoom = next;
        var after = MapBounds;
        _pan = new(_pan.X + cursor.X - (after.Left + (cursor.X - before.Left) * ratio),
            _pan.Y + cursor.Y - (after.Top + (cursor.Y - before.Top) * ratio));
        ClampPan();
        Invalidate();
    }
    internal void ResetView() { _zoom = 1; _pan = PointF.Empty; Invalidate(); }
    private void ClampPan()
    {
        float limitX = Math.Max(0, (MapBounds.Width - Viewport.Width) / 2);
        float limitY = Math.Max(0, (MapBounds.Height - Viewport.Height) / 2);
        _pan = new(Math.Clamp(_pan.X, -limitX, limitX), Math.Clamp(_pan.Y, -limitY, limitY));
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_reset == null) return;
        _reset.Location = new(Math.Max(0, Width - 58), 10);
        _zoomIn.Location = new(Math.Max(0, Width - 90), 10);
        _zoomOut.Location = new(Math.Max(0, Width - 122), 10);
        ClampPan();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    { base.OnMouseWheel(e); if (Viewport.Contains(e.Location)) ZoomAt(e.Location, e.Delta > 0 ? 1.2f : 1 / 1.2f); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Viewport.Contains(e.Location)) return;
        Focus(); _dragging = true; _lastMouse = e.Location; Capture = true; Cursor = Cursors.SizeAll;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    { base.OnMouseUp(e); _dragging = false; Capture = false; Cursor = Cursors.Default; }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { _dragging = false; Cursor = Cursors.Default; }
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    { base.OnMouseDoubleClick(e); if (Viewport.Contains(e.Location)) ResetView(); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            _pan = new(_pan.X + e.X - _lastMouse.X, _pan.Y + e.Y - _lastMouse.Y);
            _lastMouse = e.Location; ClampPan(); Invalidate(); return;
        }
        string hover = "";
        if (Viewport.Contains(e.Location))
        {
            var point = _points.FirstOrDefault(p => Distance(Project(p.Position), e.Location) < 12);
            if (point != null) hover = $"{point.Seconds / 60}:{point.Seconds % 60:D2} · {point.Evidence}";
            else
            {
                var camp = Camps.FirstOrDefault(p => Distance(Project(new(p.X, p.Y)), e.Location) < 14);
                if (camp != null) hover = camp.Name + " · 固定位置参考";
            }
        }
        if (_hoverText != hover) { _hoverText = hover; _tip.SetToolTip(this, hover); }
    }
    private static double Distance(PointF a, PointF b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.FromArgb(11, 23, 31));
        using var title = new Font(UiMetrics.FontFamily, 10F, FontStyle.Bold);
        using var small = new Font(UiMetrics.FontFamily, 8F);
        using var secondary = new SolidBrush(Color.FromArgb(138, 163, 174));
        g.DrawString("召唤师峡谷", title, Brushes.White, 13, 10);
        g.DrawString("滚轮缩放 · 拖动平移 · 双击复位", small, secondary, 13, 33);
        var state = g.Save();
        g.SetClip(Viewport);
        var map = MapBounds;
        using var shadow = new SolidBrush(Color.FromArgb(5, 14, 18));
        g.FillRectangle(shadow, Viewport);
        g.DrawImage(_terrain, map);
        using var frame = new Pen(Color.FromArgb(75, 102, 105), 1);
        g.DrawRectangle(frame, map.X, map.Y, map.Width, map.Height);
        _labels.Clear();
        DrawBuildings(g);
        foreach (var camp in Camps) DrawCamp(g, camp, small);
        DrawRoute(g, small);
        g.Restore(state);
        string legend = _points.Count == 0 ? "等待历史时间线" : _predicted ? "虚线：预测路线" : "实线：历史采样连线";
        using var route = new Pen(RouteColor, 2) { DashStyle = _predicted ? DashStyle.Dash : DashStyle.Solid };
        g.DrawLine(route, 14, Height - 24, 39, Height - 24);
        g.DrawString(legend + " · 地形与建筑为固定参考", small, secondary, 46, Height - 33);
    }

    private void DrawCamp(Graphics g, Landmark camp, Font font)
    {
        var p = Project(new(camp.X, camp.Y));
        if (!Viewport.Contains(p)) return;
        using var halo = new SolidBrush(Color.FromArgb(50, camp.Color));
        using var fill = new SolidBrush(Color.FromArgb(13, 26, 32));
        using var border = new Pen(camp.Color, 1.5f);
        using var color = new SolidBrush(camp.Color);
        g.FillEllipse(halo, p.X - 11, p.Y - 11, 22, 22);
        g.FillEllipse(fill, p.X - 6, p.Y - 6, 12, 12);
        g.DrawEllipse(border, p.X - 6, p.Y - 6, 12, 12);
        if (camp.Buff) g.FillPolygon(color, new PointF[] { new(p.X, p.Y - 4), new(p.X + 3, p.Y), new(p.X, p.Y + 4), new(p.X - 3, p.Y) });
        else { g.FillEllipse(color, p.X - 2, p.Y - 2, 4, 4); }
        DrawLabel(g, camp.Name, p, camp.Color, font, false);
    }

    private void DrawBuildings(Graphics g)
    {
        // 固定建筑位置只用于辨认地图；不表达本局建筑是否存活。
        (int X, int Y)[] towers = [(981, 10441), (1512, 6699), (1169, 4287), (5846, 6396), (5048, 4812),
            (3651, 3696), (10504, 1029), (6919, 1483), (4281, 1253), (1748, 2270), (2177, 1807)];
        foreach (bool red in new[] { false, true })
        {
            Color color = red ? Red : Blue;
            using var outline = new Pen(Color.FromArgb(180, color), 1.2f);
            using var fill = new SolidBrush(Color.FromArgb(200, 13, 28, 38));
            using var core = new SolidBrush(color);
            foreach (var (x, y) in towers)
            {
                var p = Project(red ? new RiftPoint(x, y).Mirror() : new(x, y));
                var diamond = new PointF[] { new(p.X, p.Y - 5), new(p.X + 4, p.Y), new(p.X, p.Y + 5), new(p.X - 4, p.Y) };
                g.FillPolygon(fill, diamond); g.DrawPolygon(outline, diamond);
                g.FillRectangle(core, p.X - 1, p.Y - 2, 2, 4);
            }
            var nexus = Project(red ? new RiftPoint(1430, 1430).Mirror() : new(1430, 1430));
            g.FillEllipse(fill, nexus.X - 10, nexus.Y - 10, 20, 20);
            g.DrawEllipse(outline, nexus.X - 10, nexus.Y - 10, 20, 20);
            g.FillPolygon(core, new PointF[] { new(nexus.X, nexus.Y - 7), new(nexus.X + 5, nexus.Y), new(nexus.X, nexus.Y + 7), new(nexus.X - 5, nexus.Y) });
        }
    }

    private void DrawRoute(Graphics g, Font font)
    {
        using var shadow = new Pen(Color.FromArgb(200, 5, 12, 16), 7) { LineJoin = LineJoin.Round };
        using var line = new Pen(RouteColor, 2.6f) { DashStyle = _predicted ? DashStyle.Dash : DashStyle.Solid };
        using var arrow = new AdjustableArrowCap(3, 4);
        line.CustomEndCap = arrow;
        for (int i = 1; i < _points.Count; i++)
        {
            var a = Project(_points[i - 1].Position); var b = Project(_points[i].Position);
            if (Distance(a, b) < 5) continue;
            g.DrawLine(shadow, a, b); g.DrawLine(line, a, b);
        }
        using var fill = new SolidBrush(RouteColor);
        using var ring = new Pen(Color.FromArgb(8, 19, 24), 2);
        for (int i = 0; i < _points.Count; i++)
        {
            var point = _points[i]; var p = Project(point.Position);
            if (!Viewport.Contains(p)) continue;
            g.FillEllipse(fill, p.X - 4.5f, p.Y - 4.5f, 9, 9);
            g.DrawEllipse(ring, p.X - 4.5f, p.Y - 4.5f, 9, 9);
            if (_points.Count <= 12 || i % 2 == 0 || i == _points.Count - 1)
                DrawLabel(g, $"{point.Seconds / 60}:{point.Seconds % 60:D2}", p, RouteColor, font, true);
        }
    }

    private void DrawLabel(Graphics g, string text, PointF anchor, Color color, Font font, bool route)
    {
        var size = g.MeasureString(text, font);
        float width = size.Width + 10, height = size.Height + 4;
        PointF[] offsets = route ? [new(10, -height - 7), new(-width - 10, -height - 7), new(10, 9), new(-width - 10, 9), new(-width / 2, -height - 27)]
            : [new(10, -height / 2), new(-width - 10, -height / 2), new(-width / 2, 11), new(-width / 2, -height - 11)];
        foreach (var offset in offsets)
        {
            var bounds = new RectangleF(anchor.X + offset.X, anchor.Y + offset.Y, width, height);
            if (!Viewport.Contains(bounds) || _labels.Any(r => r.IntersectsWith(bounds))) continue;
            _labels.Add(RectangleF.Inflate(bounds, 2, 2));
            using var back = new SolidBrush(Color.FromArgb(route ? 235 : 205, 7, 19, 26));
            using var edge = new Pen(Color.FromArgb(route ? 145 : 70, color), 1);
            using var foreground = new SolidBrush(route ? color : Color.FromArgb(220, 232, 230));
            g.FillRectangle(back, bounds); g.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            g.DrawString(text, font, foreground, bounds.X + 5, bounds.Y + 2);
            return;
        }
        // 拥挤时省略标签，完整信息仍可通过悬停和右侧列表查看。
    }

    private static Landmark[] BuildCamps()
    {
        var camps = new List<Landmark>();
        foreach (var (x, y, name, buff) in new[] { (3900, 7900, "蓝BUFF", true), (2100, 8400, "魔沼蛙", false),
            (3800, 6500, "三狼", false), (7800, 4100, "红BUFF", true), (6500, 5500, "锋喙鸟", false), (8400, 2700, "石甲虫", false) })
        {
            camps.Add(new(x, y, name, Blue, buff));
            camps.Add(new(15000 - x, 15000 - y, name, Red, buff));
        }
        camps.Add(new(5000, 10400, "大龙坑", Color.FromArgb(188, 152, 247)));
        camps.Add(new(9900, 4500, "小龙坑", Color.FromArgb(235, 188, 102)));
        return camps.ToArray();
    }

    private static Bitmap CreateTerrain()
    {
        using var stream = typeof(JungleRouteMap).Assembly.GetManifestResourceStream("LOL_GameAssistant.Resources.Maps.SummonersRift.png")
            ?? throw new InvalidOperationException("缺少内置峡谷地图资源。");
        using var source = new Bitmap(stream);
        const int size = 1536;
        var terrain = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(terrain);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(new Rectangle(0, 0, size, size), Color.FromArgb(23, 47, 39), Color.FromArgb(10, 29, 30), 45);
        g.FillRectangle(background, 0, 0, size, size);
        // 只在游戏底图透明的不可行走地形中绘制林木层次，保留原始道路、河道和营地轮廓。
        var random = new Random(11);
        using var shadow = new SolidBrush(Color.FromArgb(110, 3, 13, 16));
        using var tree = new SolidBrush(Color.FromArgb(28, 59, 47));
        using var crown = new SolidBrush(Color.FromArgb(42, 75, 57));
        using var ridge = new Pen(Color.FromArgb(70, 105, 87, 62), 2);
        for (int y = 10; y < 502; y += 7)
            for (int x = 10; x < 502; x += 7)
            {
                if (source.GetPixel(x, y).A > 30) continue;
                float px = (x + random.Next(-2, 3)) * 3, py = (y + random.Next(-2, 3)) * 3;
                float radius = random.Next(8, 14);
                g.FillEllipse(shadow, px - radius, py - radius / 2 + 5, radius * 2.2f, radius * 1.6f);
                g.FillPolygon(tree, new PointF[] { new(px, py - radius), new(px + radius, py + radius / 2), new(px, py + radius), new(px - radius, py + radius / 2) });
                g.FillPolygon(crown, new PointF[] { new(px, py - radius), new(px + radius / 2, py + 2), new(px - radius / 2, py + 3) });
                if (random.Next(5) == 0) g.DrawLine(ridge, px - radius, py + radius, px + radius, py + radius / 2);
            }
        using var colors = new ImageAttributes();
        colors.SetColorMatrix(new ColorMatrix { Matrix00 = .68f, Matrix11 = .77f, Matrix22 = .72f, Matrix33 = 1, Matrix44 = 1 });
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, colors);
        return terrain;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _terrain.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
