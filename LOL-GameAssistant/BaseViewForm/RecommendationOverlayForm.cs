using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 不抢焦点的游戏内建议浮窗。仅显示助手已生成的文本，不读取或覆盖游戏画面。
/// </summary>
internal sealed class RecommendationOverlayForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private readonly Label _content = new();
    private readonly System.Windows.Forms.Timer _hideTimer = new();

    public RecommendationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(20, 25, 32);
        Opacity = .9;
        Size = new Size(410, 150);
        Padding = new Padding(14, 10, 14, 10);
        StartPosition = FormStartPosition.Manual;

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(144, 202, 249),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Text = "LOL 助手 · 当前建议"
        };
        _content.Dock = DockStyle.Fill;
        _content.ForeColor = Color.White;
        _content.Font = new Font("Microsoft YaHei UI", 9.5F);
        _content.AutoEllipsis = true;
        _content.TextAlign = ContentAlignment.TopLeft;
        Controls.Add(_content);
        Controls.Add(title);
        _hideTimer.Tick += (_, _) => Hide();
        Disposed += (_, _) => _hideTimer.Dispose();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
            return parameters;
        }
    }

    public void ShowRecommendation(string recommendation, CloudAiSettings settings)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(recommendation)) return;
        string compact = string.Join(Environment.NewLine,
            recommendation.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Take(4));
        _content.Text = compact.Length > 300 ? compact[..300] + "…" : compact;
        Location = GetLocation(settings);
        _hideTimer.Stop();
        _hideTimer.Interval = Math.Clamp(settings.RecommendationOverlayDurationSeconds, 3, 30) * 1000;
        if (!Visible) Show();
        _hideTimer.Start();
    }

    private Point GetLocation(CloudAiSettings settings)
    {
        Rectangle area = Screen.FromHandle(Program.GameMain.Handle).WorkingArea;
        int x = area.Left + settings.RecommendationOverlayOffsetX;
        int y = area.Bottom - Height - settings.RecommendationOverlayOffsetY;
        switch (settings.RecommendationOverlayPosition)
        {
            case "TopLeft":
                y = area.Top + settings.RecommendationOverlayOffsetY;
                break;
            case "TopRight":
                x = area.Right - Width - settings.RecommendationOverlayOffsetX;
                y = area.Top + settings.RecommendationOverlayOffsetY;
                break;
            case "BottomRight":
                x = area.Right - Width - settings.RecommendationOverlayOffsetX;
                break;
            case "Center":
                x = area.Left + (area.Width - Width) / 2 + settings.RecommendationOverlayOffsetX;
                y = area.Top + (area.Height - Height) / 2 + settings.RecommendationOverlayOffsetY;
                break;
        }
        return new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
    }
}
