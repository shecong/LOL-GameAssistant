using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>用战绩列表已经返回的本人数据先显示记录，等待完整对局详情。</summary>
public sealed class MatchHistoryPreviewCard : UserControl
{
    private readonly AntdUI.Panel _surface = new() { Dock = DockStyle.Fill, Radius = 10, BorderWidth = 1 };
    private readonly AntdUI.Label _result;
    private readonly AntdUI.Label _mode;
    private readonly AntdUI.Label _summary;
    private readonly AntdUI.Label _detailState;

    public MatchHistoryPreviewCard(MatchHistoryGame game, string viewerPuuid)
    {
        MatchParticipant? mine = game.GetParticipant(viewerPuuid);
        ThemePalette palette = UiTheme.Palette;
        BackColor = palette.SurfaceMuted;
        Height = 94;
        MinimumSize = new Size(340, 94);
        _surface.BackColor = palette.SurfaceRaised;
        Controls.Add(_surface);

        string result = mine?.stats == null ? "对局" : mine.IsWin() ? "胜利" : "失败";
        Color resultColor = mine?.stats == null ? palette.TextPrimary : mine.IsWin()
            ? (palette.IsDark ? Color.FromArgb(125, 214, 157) : Color.FromArgb(25, 125, 76))
            : (palette.IsDark ? Color.FromArgb(245, 151, 158) : Color.FromArgb(188, 64, 75));
        _result = MakeLabel(result, true, resultColor);
        _mode = MakeLabel(game.GetModeText(), true, palette.TextPrimary);
        string created = "时间未知";
        if (game.GameCreation > 0)
        {
            try { created = DateTimeOffset.FromUnixTimeMilliseconds(game.GameCreation).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
            catch (ArgumentOutOfRangeException) { }
        }
        string duration = game.GameDuration > 0
            ? $"{game.GameDuration / 60}:{game.GameDuration % 60:D2}"
            : "时长未知";
        _summary = MakeLabel($"{created} · {duration} · KDA {(mine?.stats == null ? "—" : mine.GetKdaText())}",
            false, palette.TextSecondary);
        _detailState = MakeLabel("双方详情加载中...", false, palette.TextSecondary);
        foreach (Control control in new Control[] { _result, _mode, _summary, _detailState })
            _surface.Controls.Add(control);
        Resize += (_, _) => LayoutLabels();
        LayoutLabels();
    }

    public void SetDetailUnavailable()
    {
        if (!IsDisposed) _detailState.Text = "双方详情暂不可用，已保留本场摘要";
    }

    private void LayoutLabels()
    {
        int width = ClientSize.Width;
        _result.SetBounds(14, 12, 50, 30);
        _mode.SetBounds(76, 8, Math.Max(100, width - 90), 28);
        _summary.SetBounds(76, 36, Math.Max(100, width - 90), 24);
        _detailState.SetBounds(76, 62, Math.Max(100, width - 90), 22);
    }

    private static AntdUI.Label MakeLabel(string text, bool bold, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        Font = new Font(UiMetrics.FontFamily, 9F, bold ? FontStyle.Bold : FontStyle.Regular),
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };
}
