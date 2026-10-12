using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>详情卡片自己维护语义色，避免动态创建后切换主题留下旧颜色。</summary>
internal sealed class MatchPlayerCard(bool isCurrentPlayer) : AntdUI.Panel, IThemeAware
{
    private readonly List<(AntdUI.Label Label, bool Primary)> _labels = [];
    internal void TrackLabel(AntdUI.Label label, bool primary) => _labels.Add((label, primary));
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = isCurrentPlayer ? palette.IsDark ? Color.FromArgb(75, 60, 25) : Color.FromArgb(255, 249, 230) : palette.SurfaceRaised;
        foreach (var (label, primary) in _labels)
            if (!label.IsDisposed) label.ForeColor = primary ? palette.TextPrimary : palette.TextSecondary;
    }
}
