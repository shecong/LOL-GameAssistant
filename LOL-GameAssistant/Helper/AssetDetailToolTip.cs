using System.Net;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Helper;

/// <summary>装备和符文的换行提示；点击可打开完整、可滚动的作用说明。</summary>
internal sealed class AssetDetailToolTip : IDisposable
{
    private readonly ToolTip _tip = new() { OwnerDraw = true, InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 30000 };
    private readonly Dictionary<Control, string> _details = new();

    public AssetDetailToolTip()
    {
        _tip.Popup += (_, e) =>
        {
            string text = _tip.GetToolTip(e.AssociatedControl) ?? "";
            Size measured = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(380, 0), TextFormatFlags.WordBreak);
            e.ToolTipSize = new Size(measured.Width + 24, measured.Height + 24);
        };
        _tip.Draw += (_, e) =>
        {
            ThemePalette palette = UiTheme.Palette;
            using var background = new SolidBrush(palette.SurfaceRaised);
            using var border = new Pen(palette.TextSecondary);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, SystemFonts.MessageBoxFont,
                new Rectangle(12, 12, e.Bounds.Width - 24, e.Bounds.Height - 24), palette.TextPrimary,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
    }

    internal static string PlainText(string? html)
    {
        string text = Regex.Replace(html ?? "", @"<br\s*/?>|</(?:p|div|li)>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        text = Regex.Replace(text, @"%i:[^%]+%", "");
        return Regex.Replace(text, @"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n").Trim();
    }

    public void SetDetails(Control control, string title, string? description)
    {
        if (control.IsDisposed) return;
        string body = PlainText(description);
        string text = title + "\n\n" + (string.IsNullOrWhiteSpace(body) ? "暂未获取到作用说明，请稍后重新打开详情。" : body);
        if (!_details.ContainsKey(control))
        {
            control.Cursor = Cursors.Hand;
            control.Click += ShowDetails;
            control.Disposed += (_, _) => _details.Remove(control);
        }
        _details[control] = text;
        _tip.SetToolTip(control, (text.Length > 800 ? text[..800] + "…" : text) + "\n\n点击查看完整详情");
    }

    private void ShowDetails(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_details.TryGetValue(control, out string? text)) return;
        _tip.Hide(control);
        using var dialog = new Form
        {
            Text = text.Split('\n')[0], Size = new Size(480, 400), MinimumSize = new Size(360, 260),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            MinimizeBox = false, MaximizeBox = false, Icon = control.FindForm()?.Icon
        };
        dialog.Controls.Add(new TextBox
        {
            Text = text.Replace("\n", Environment.NewLine), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 10F), BackColor = UiTheme.Palette.SurfaceRaised,
            ForeColor = UiTheme.Palette.TextPrimary
        });
        dialog.Padding = new Padding(16);
        UiTheme.Apply(dialog);
        dialog.ShowDialog(control.FindForm());
    }

    public void Dispose() { _tip.Dispose(); _details.Clear(); }
}
