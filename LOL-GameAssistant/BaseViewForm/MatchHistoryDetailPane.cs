using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>列表行下方的完整对局详情，三种布局共享详情和静态资源。</summary>
internal sealed class MatchHistoryDetailPane : UserControl, IThemeAware
{
    private readonly MatchDetailLayoutPreference _preference;
    private readonly MatchScrollPanel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly AntdUI.Label _state = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly AntdUI.Label _title = new() { Dock = DockStyle.Fill };
    private readonly AntdUI.Button _close = new() { Text = "收起", Dock = DockStyle.Right, Width = 62 };
    private readonly AntdUI.Button _expand = new() { Text = "独立窗口", Dock = DockStyle.Right, Width = 90 };
    private readonly AntdUI.Button _retry = new() { Text = "重试", Dock = DockStyle.Bottom, Height = 36, Visible = false };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Bottom, Height = 42, WrapContents = true };
    private readonly AntdUI.Segmented _layouts = new() { Dock = DockStyle.Right, Width = 430 };
    private readonly MatchDetailStatisticsView _statistics;
    private bool _resizing;
    internal MatchDetail? Detail { get; private set; }
    internal string ViewerPuuid { get; private set; } = "";
    internal string StateText => _state.Text ?? "";
    internal MatchDetailLayout LayoutMode => _statistics.LayoutMode;
    internal MatchDetailStatisticsView Statistics => _statistics;
    internal event Action? CloseRequested;
    internal event Action? RetryRequested;
    internal event Action<MatchDetail, string>? FullDetailRequested;
    internal event Action<string, string>? PlayerRequested;
    internal event Action? PreferredHeightChanged;

    internal MatchHistoryDetailPane(IGameAssetService assets, IChampionCatalog champions, MatchDetailLayoutPreference? preference = null)
    {
        _preference = preference ?? new();
        _statistics = new(assets, champions);
        _statistics.PlayerRequested += puuid => { if (Detail != null) PlayerRequested?.Invoke(puuid, Detail.PlatformId); };
        var header = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 0, 0, 0) };
        header.Controls.Add(_title); header.Controls.Add(_layouts); header.Controls.Add(_expand); header.Controls.Add(_close);
        foreach (string name in new[] { "双队五行对照", "双行紧凑记分板", "全员字段矩阵" })
            _layouts.Items.Add(new AntdUI.SegmentedItem { Text = name });
        _statistics.LayoutMode = _preference.Read(); _layouts.SelectIndex = (int)_statistics.LayoutMode;
        _layouts.SelectIndexChanged += (_, _) => SetLayout((MatchDetailLayout)_layouts.SelectIndex);
        _close.Click += (_, _) => CloseRequested?.Invoke();
        _retry.Click += (_, _) => RetryRequested?.Invoke();
        _expand.Click += (_, _) => { if (Detail is { } detail) FullDetailRequested?.Invoke(detail, ViewerPuuid); };
        Controls.Add(_content); Controls.Add(_actions); Controls.Add(header);
        _content.Controls.Add(_statistics); _content.Controls.Add(_state); _content.Controls.Add(_retry);
        SizeChanged += (_, _) => ResizeBody();
        _content.SizeChanged += (_, _) => ResizeBody();
        header.SizeChanged += (_, _) =>
        {
            bool narrow = header.Width < 800 * DeviceDpi / 96;
            _layouts.Dock = narrow ? DockStyle.Bottom : DockStyle.Right;
            header.Height = (narrow ? 76 : 40) * DeviceDpi / 96;
            _layouts.Height = 36 * DeviceDpi / 96;
        };
        ApplyTheme(UiTheme.Palette);
    }
    internal void SetLayout(MatchDetailLayout mode)
    {
        if (!Enum.IsDefined(mode)) return;
        _statistics.LayoutMode = mode;
        if (_layouts.SelectIndex != (int)mode) _layouts.SelectIndex = (int)mode;
        try { _preference.Save(mode); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { RuntimeDiagnostics.WriteException(error); }
        _content.AutoScrollPosition = Point.Empty;
        ResizeBody(); _statistics.Invalidate();
    }
    private void Reset()
    {
        Detail = null; _expand.Enabled = false; _retry.Visible = false;
        _statistics.Visible = false; _state.Visible = true; _state.BringToFront();
        _content.AutoScrollPosition = Point.Empty; _content.AutoScrollMinSize = Size.Empty;
        ControlLifetime.ClearAndDispose(_actions);
    }
    internal void SetLoading(MatchHistoryGame game, string puuid)
    {
        Reset(); ViewerPuuid = puuid; _title.Text = "对局详情 · 加载中";
        _state.Text = $"正在读取本局详情… {game.GetModeText()}";
        ResizeBody();
    }
    internal void SetError(string message)
    {
        Reset(); _title.Text = "对局详情 · 暂不可用"; _state.Text = $"本局详情加载失败：{message}";
        _retry.Visible = true; _retry.BringToFront(); ResizeBody();
    }
    internal void SetDetail(MatchDetail detail, string puuid)
    {
        Reset(); Detail = detail; ViewerPuuid = puuid; _expand.Enabled = true;
        _title.Text = $"{detail.GetModeText()} · {detail.GetDurationText()} · {detail.PlatformId}";
        _state.Visible = false; _statistics.Visible = true; _statistics.SetDetail(detail, puuid);
        if (!detail.IsCrossRegion) _actions.Controls.Add(new MatchReplayActions(detail.GameId) { Margin = Padding.Empty });
        _actions.Controls.Add(new AntdUI.Label { Text = detail.IsCrossRegion ? "跨区回放不可用" : $"对局 {detail.GameId} · {detail.participants.Count} 人", Width = 290, Height = 32 });
        ResizeBody(); ApplyTheme(UiTheme.Palette);
    }
    private void ResizeBody()
    {
        if (_resizing || IsDisposed) return;
        _resizing = true;
        try
        {
            int scale(int n) => n * DeviceDpi / 96;
            int width = Math.Max(scale(300), _content.ClientSize.Width - scale(4));
            int naturalWidth = Math.Max(width, _statistics.MinimumContentWidth);
            int bodyHeight = Detail == null ? scale(100) : _statistics.MeasureContentHeight(naturalWidth);
            _statistics.SetBounds(0, 0, naturalWidth, bodyHeight);
            int horizontal = naturalWidth > width ? SystemInformation.HorizontalScrollBarHeight : 0;
            int chrome = Controls.Cast<Control>().Where(c => c != _content && c.Visible).Sum(c => c.Height);
            int height = bodyHeight + chrome + horizontal + scale(6);
            if (Height != height) { Height = height; PreferredHeightChanged?.Invoke(); }
        }
        finally { _resizing = false; }
    }
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _content.BackColor = _actions.BackColor = palette.SurfaceRaised;
        var scheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        _title.ColorScheme = _state.ColorScheme = _close.ColorScheme = _expand.ColorScheme = _retry.ColorScheme = _layouts.ColorScheme = scheme;
        _title.ForeColor = _state.ForeColor = palette.TextPrimary;
        _layouts.BackColor = palette.SurfaceRaised;
        foreach (var label in _actions.Controls.OfType<AntdUI.Label>()) { label.ColorScheme = scheme; label.ForeColor = palette.TextSecondary; }
        _statistics.ApplyTheme(palette);
    }
}
