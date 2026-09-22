using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 不抢焦点的游戏内建议浮窗。仅显示助手已生成的文本，不读取或覆盖游戏画面。
/// </summary>
internal sealed class RecommendationOverlayForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private readonly Label _content = new();
    private readonly System.Windows.Forms.Timer _hideTimer = new();

    public RecommendationOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(20, 25, 32);
        Opacity = .9;
        Size = new Size(430, 164);
        Padding = new Padding(14, 10, 14, 10);
        StartPosition = FormStartPosition.Manual;

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(144, 202, 249),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Text = "LOL 助手 · 当前时间线建议"
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
            // 不激活、不进入 Alt+Tab，且鼠标可直接穿透到游戏窗口。
            parameters.ExStyle |= WsExNoActivate | WsExTransparent | WsExToolWindow;
            return parameters;
        }
    }

    public void ShowRecommendation(CoachRecommendation recommendation, CloudAiSettings settings)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(recommendation.Body)) return;
        string compact = $"{recommendation.Title}\n{recommendation.Body}\n依据：{recommendation.Evidence}";
        _content.Text = compact.Length > 330 ? compact[..330] + "…" : compact;
        Location = GetLocation(settings);
        _hideTimer.Stop();
        _hideTimer.Interval = Math.Clamp(settings.RecommendationOverlayDurationSeconds, 3, 30) * 1000;
        if (!Visible) Show();
        _hideTimer.Start();
    }

    private Point GetLocation(CloudAiSettings settings)
    {
        Rectangle area = Screen.FromHandle(GetLeagueWindowHandle() ?? Program.GameMain.Handle).WorkingArea;
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
        int maxX = Math.Max(area.Left, area.Right - Width);
        int maxY = Math.Max(area.Top, area.Bottom - Height);
        return new Point(Math.Clamp(x, area.Left, maxX), Math.Clamp(y, area.Top, maxY));
    }

    /// <summary>优先跟随游戏窗口所在显示器；未找到时才回退到助手主窗口。</summary>
    private static IntPtr? GetLeagueWindowHandle()
    {
        IntPtr foreground = GetForegroundWindow();
        if (IsLeagueWindow(foreground)) return foreground;

        foreach (string processName in new[] { "League of Legends", "LeagueClientUx", "LeagueClient" })
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return null;
    }

    private static bool IsLeagueWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return false;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClientUx", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClient", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}