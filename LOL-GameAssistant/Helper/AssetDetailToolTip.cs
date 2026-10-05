using System.Net;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Helper;

/// <summary>装备和符文的换行提示；点击可打开完整、可滚动的作用说明。</summary>
internal sealed class AssetDetailToolTip : IDisposable
{
    private readonly ToolTip _tip = new() { OwnerDraw = true, InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 30000 };
    private readonly Dictionary<Control, string> _details = new();

    /// <summary>初始化 AssetDetailToolTip 的实例状态。</summary>
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

    /// <summary>将游戏富文本标签和图标占位符转换为可读的纯文本。</summary>
    internal static string PlainText(string? html)
    {
        string text = Regex.Replace(html ?? "", @"<br\s*/?>|</(?:p|div|li)>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        text = Regex.Replace(text, @"%i:[^%]+%", "");
        return Regex.Replace(text, @"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n").Trim();
    }

    /// <summary>绑定资源名称和作用说明，并提供悬停与点击查看入口。</summary>
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

    /// <summary>打开可滚动的资源说明弹窗。</summary>
    private void ShowDetails(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_details.TryGetValue(control, out string? text)) return;
        _tip.Hide(control);
        using var dialog = new AntdUI.Window
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
        AntdWindowChrome.AddHeader(dialog);
        UiTheme.Apply(dialog);
        dialog.ShowDialog(control.FindForm());
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    public void Dispose() { _tip.Dispose(); _details.Clear(); }
}
