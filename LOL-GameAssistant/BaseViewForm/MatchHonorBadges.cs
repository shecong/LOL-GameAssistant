using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class MatchHonorBadges : Control, IThemeAware
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MatchHonors Honors { get; set; } = new(null, [], false);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string StatusText { get; set; } = "";
    internal MatchHonorBadges()
    {
        Name = "matchHonors";
        Height = 20;
        DoubleBuffered = true;
        Font = new Font(UiMetrics.FontFamily, 8F, FontStyle.Bold);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Honors.Labels.Count > 0) Draw(e.Graphics, Honors, Font, ClientRectangle);
        else MatchListColumns.Text(e.Graphics, StatusText, Font, UiTheme.Palette.TextSecondary, ClientRectangle);
    }
    internal static void Draw(Graphics graphics, MatchHonors honors, Font font, Rectangle bounds)
    {
        int gap = Math.Max(3, bounds.Height / 5), x = bounds.X;
        foreach (string text in honors.Labels)
        {
            int width = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width + gap * 2;
            int available = bounds.Right - x;
            if (available < gap * 3) break;
            var color = text.StartsWith("胜方 MVP", StringComparison.Ordinal) ? Color.FromArgb(176, 119, 15)
                : text.StartsWith("败方 MVP", StringComparison.Ordinal) ? Color.FromArgb(129, 101, 215)
                : text.StartsWith("五杀", StringComparison.Ordinal) ? Color.FromArgb(221, 78, 106)
                : text.StartsWith("超神", StringComparison.Ordinal) ? Color.FromArgb(222, 139, 37) : UiTheme.Palette.Accent;
            var rect = new Rectangle(x, bounds.Y, Math.Min(width, available), Math.Max(1, bounds.Height - 2));
            using var fill = new SolidBrush(Color.FromArgb(UiTheme.Palette.IsDark ? 55 : 24, color));
            graphics.FillRectangle(fill, rect);
            TextRenderer.DrawText(graphics, text, font,
                new Rectangle(rect.X + gap, rect.Y, Math.Max(0, rect.Width - gap * 2), rect.Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            x += width + gap;
        }
    }
    public void ApplyTheme(ThemePalette palette) { BackColor = Parent?.BackColor ?? palette.Surface; Invalidate(); }
}
