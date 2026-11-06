using System.Drawing.Drawing2D;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>A bounded, keyboard-accessible tile; each tile owns only its displayed bitmap.</summary>
internal sealed class FileSkinArtTile : Control, IThemeAware
{
    private Image? _image;
    private ThemePalette _palette = UiTheme.Palette;
    [System.ComponentModel.DefaultValue("")] public string Caption { get; set; } = "";
    [System.ComponentModel.DefaultValue("")] public string Badge { get; set; } = "";
    [System.ComponentModel.DefaultValue(false)] public bool Selected { get; set; }
    [System.ComponentModel.DefaultValue(false)] public bool Favorite { get; set; }
    [System.ComponentModel.DefaultValue(false)] public bool Compact { get; init; }
    public event Action? SelectedByUser;
    public event Action? FavoriteByUser;
    public FileSkinArtTile()
    {
        DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.CheckButton;
    }
    public void SetArtwork(Image image)
    {
        if (IsDisposed) { image.Dispose(); return; }
        _image?.Dispose(); _image = image; Invalidate();
    }
    public void ApplyTheme(ThemePalette palette) { _palette = palette; BackColor = palette.Surface; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e); Focus();
        if (!Compact && e.X > Width - 40 && e.Y < 40) FavoriteByUser?.Invoke(); else SelectedByUser?.Invoke();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) { e.Handled = true; SelectedByUser?.Invoke(); }
        if (!Compact && e.KeyCode == Keys.F) { e.Handled = true; FavoriteByUser?.Invoke(); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(2, 2, Width - 5, Height - 5);
        using var background = new SolidBrush(Selected ? Color.FromArgb(25, _palette.Accent) : _palette.SurfaceRaised);
        using var border = new Pen(Selected ? _palette.Accent : _palette.Border, Selected ? 2 : 1);
        using var shape = Rounded(bounds, Compact ? 10 : 15);
        g.FillPath(background, shape);
        if (Compact)
        {
            int size = Math.Min(44, Width - 16); var image = new Rectangle((Width - size) / 2, 9, size, size);
            using var circle = new GraphicsPath(); circle.AddEllipse(image);
            var clip = g.Save(); g.SetClip(circle);
            DrawArt(g, image); g.Restore(clip);
            TextRenderer.DrawText(g, Caption, Font, new Rectangle(2, size + 15, Width - 4, 24), Selected ? _palette.Accent : _palette.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
        else
        {
            var art = new Rectangle(3, 3, Width - 7, Height - 68);
            var clip = g.Save(); g.SetClip(shape); g.SetClip(art, CombineMode.Intersect); DrawArt(g, art); g.Restore(clip);
            using var badgeBack = new SolidBrush(Color.FromArgb(190, 28, 34, 44));
            var badge = new Rectangle(10, 12, Math.Min(Width - 65, Math.Max(42, TextRenderer.MeasureText(Badge, Font).Width + 10)), 24);
            g.FillRectangle(badgeBack, badge);
            TextRenderer.DrawText(g, Badge, Font, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using var heartFont = new Font("Segoe UI Symbol", 16);
            TextRenderer.DrawText(g, Favorite ? "♥" : "♡", heartFont, new Rectangle(Width - 38, 10, 30, 30), Favorite ? Color.Coral : Color.White,
                Color.FromArgb(45, 48, 56), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Caption, Font, new Rectangle(12, Height - 58, Width - 24, 48), _palette.TextPrimary,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            var check = new Rectangle(Width - 34, art.Bottom - 30, 23, 23);
            using var brush = new SolidBrush(Selected ? _palette.Accent : Color.FromArgb(170, 35, 40, 50));
            g.FillEllipse(brush, check);
            TextRenderer.DrawText(g, Selected ? "✓" : "+", Font, check, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        if (Selected || Focused) g.DrawPath(border, shape);
    }
    private void DrawArt(Graphics g, Rectangle bounds)
    {
        using var gradient = new LinearGradientBrush(bounds, _palette.SurfaceMuted, Color.FromArgb(65, 77, 104), 70);
        g.FillRectangle(gradient, bounds);
        if (_image is null)
        {
            using var font = new Font(UiMetrics.FontFamily, Compact ? 14 : 26, FontStyle.Bold);
            TextRenderer.DrawText(g, (Badge.Length == 0 ? Caption : Badge)[..Math.Min(2, (Badge.Length == 0 ? Caption : Badge).Length)], font, bounds,
                _palette.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        float scale = Math.Max((float)bounds.Width / _image.Width, (float)bounds.Height / _image.Height);
        int width = (int)(_image.Width * scale), height = (int)(_image.Height * scale);
        g.DrawImage(_image, new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height));
    }
    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath(); int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    protected override void Dispose(bool disposing) { if (disposing) _image?.Dispose(); base.Dispose(disposing); }
}
