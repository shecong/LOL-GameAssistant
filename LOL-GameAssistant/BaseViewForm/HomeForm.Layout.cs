using Panel = AntdUI.Panel;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class HomeForm
{
    private readonly AntdUI.Select _recentMode = new() { Name = "recentModeFilter", List = true, DropDownArrow = true, Width = 160, Height = 36 };
    private bool _updatingModes;
    private bool _updatingPagination;
    private readonly GradientPanel _modeCard = new() { Name = "homeModeCounts", Dock = DockStyle.Top, DrawBorder = true,
        CornerRadius = 12, Padding = new Padding(14, 64, 14, 10), Height = 110 };
    private readonly AntdUI.Label _modeTitle = new() { Text = "各地图模式场次", Location = new Point(14, 8),
        AutoSize = true, BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 10F, FontStyle.Bold) };
    private readonly AntdUI.Label _modeScope = new() { Name = "homeModeCountScope", Text = "等待战绩数据", AutoEllipsis = true,
        BackColor = Color.Transparent, Location = new Point(14, 32), Height = 28 };
    private readonly TableLayoutPanel _modeCounts = new() { Name = "homeModeCountRows", Dock = DockStyle.Top,
        ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };

    private void InitializeDashboardLayout()
    {
        rootGrid.SuspendLayout();
        rootGrid.Controls.Remove(playerCard);
        rootGrid.Controls.Remove(rankedCard);
        rootGrid.Controls.Remove(historyCard);
        rootGrid.RowCount = 2;
        rootGrid.RowStyles.RemoveAt(2);
        rootGrid.ColumnStyles[0].SizeType = SizeType.Absolute;
        rootGrid.ColumnStyles[0].Width = 410 * DeviceDpi / 96;
        rootGrid.ColumnStyles[1].Width = 100;
        var overview = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 0, 12, 0) };
        rankedCard.Dock = DockStyle.Top;
        rankedCard.Height = 250 * DeviceDpi / 96;
        rankGrid.ColumnCount = 2;
        rankGrid.ColumnStyles.Clear();
        rankGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        rankGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        rankGrid.RowCount = 1;
        rankGrid.RowStyles.Clear();
        rankGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rankGrid.SetCellPosition(flexPanel, new TableLayoutPanelCellPosition(1, 0));
        soloPanel.Margin = new Padding(0, 0, 6, 0);
        flexPanel.Margin = Padding.Empty;
        foreach (var title in new[] { game_dspT, game_lhpT }) title.Font = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold);
        playerCard.Dock = DockStyle.Top;
        playerCard.Height = 174 * DeviceDpi / 96;
        _modeCounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        _modeCounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _modeCard.Controls.Add(_modeCounts); _modeCard.Controls.Add(_modeScope); _modeCard.Controls.Add(_modeTitle);
        overview.Controls.Add(_modeCard);
        overview.Controls.Add(rankedCard);
        overview.Controls.Add(playerCard);
        rootGrid.Controls.Add(overview, 0, 1);
        rootGrid.Controls.Add(historyCard, 1, 1);
        rootGrid.SetColumnSpan(historyCard, 1);
        historyCard.Margin = Padding.Empty;
        historyHeader.Controls.Remove(game_pagin);
        game_pagin.Dock = DockStyle.Bottom;
        historyCard.Controls.Add(game_pagin);
        historyHeader.Controls.Add(_recentMode);
        lblHistoryTitle.Text = "近期战绩";
        lblHistoryTitle.Width = 112;
        lblPageSizeCap.Width = 56;
        game_count.Minimum = 1;
        game_count.Maximum = 50;
        game_count.ValueChanged += async (_, _) =>
        {
            InitPagin(matchlists);
            await UpdateGame_paginAsync();
        };
        foreach (Control action in flowSearch.Controls) action.Height = UiMetrics.ControlHeight * DeviceDpi / 96;
        _recentMode.SelectedIndexChanged += async (_, _) =>
        {
            if (_updatingModes) return;
            InitPagin(matchlists);
            await UpdateGame_paginAsync();
        };
        inp_playname.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            PlayInfo_Click(inp_playname, EventArgs.Empty);
        };
        rootGrid.ResumeLayout(true);
    }

    private void UpdateModeCounts(MatchHistoryResponse? history, string emptyText = "等待战绩数据")
    {
        var games = (history?.Games?.Games ?? []).Where(g => g.GameId > 0).DistinctBy(g => g.GameId).ToArray();
        _modeCounts.SuspendLayout();
        try
        {
            ControlLifetime.ClearAndDispose(_modeCounts); _modeCounts.RowStyles.Clear(); _modeCounts.RowCount = 0;
            foreach (var group in games.GroupBy(g => g.GetModeText()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                int row = _modeCounts.RowCount++;
                _modeCounts.RowStyles.Add(new RowStyle(SizeType.Absolute, 26 * DeviceDpi / 96));
                _modeCounts.Controls.Add(new AntdUI.Label { Text = group.Key, Dock = DockStyle.Fill, AutoEllipsis = true,
                    Margin = Padding.Empty, BackColor = Color.Transparent }, 0, row);
                _modeCounts.Controls.Add(new AntdUI.Label { Text = $"{group.Count()} 场", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleRight, Margin = Padding.Empty, BackColor = Color.Transparent }, 1, row);
            }
        }
        finally { _modeCounts.ResumeLayout(true); }
        bool complete = history?.Games is { IsTotalKnown: true, HasMore: false };
        _modeScope.Text = history == null ? emptyText : complete
            ? $"接口全部可读记录：{games.Length} 场" + (history.Games!.MissingSummaryCount > 0 ? $" · {history.Games.MissingSummaryCount} 条不可读" : "")
            : $"正在读取全部历史 · 已读取 {games.Length} 场";
        _historyTip.SetToolTip(_modeScope, _historySnapshot?.ScopeText ?? _modeScope.Text);
        LayoutDashboardCards(); ApplyModeCountTheme(UiTheme.Palette);
    }

    private void LayoutDashboardCards()
    {
        int S(int n) => n * DeviceDpi / 96;
        _modeScope.Width = Math.Max(1, _modeCard.ClientSize.Width - S(28));
        _modeCard.Height = S(74 + 26 * Math.Max(1, _modeCounts.RowCount));
        bottomStats.MaximumSize = new Size(Math.Max(1, rankedCard.ClientSize.Width - rankedCard.Padding.Horizontal), 0);
        int metaHeight = bottomStats.GetPreferredSize(new Size(bottomStats.MaximumSize.Width, 0)).Height;
        rankedCard.Height = rankedCard.Padding.Vertical + S(146) + metaHeight;
        foreach (var item in new[] {
            (soloPanel, pic_dsp, game_dspT, game_dsp_sl, game_dsp_win, game_dsp_loss, game_dsp_lp, game_dsp_highest),
            (flexPanel, pic_lhp, game_lhpT, game_lhp_sl, game_lhp_win, game_lhp_loss, game_lhp_lp, game_lhp_highest) })
        {
            var (panel, icon, title, rate, wins, losses, lp, highest) = item;
            int width = panel.ClientSize.Width, half = Math.Max(1, (width - S(20)) / 2);
            icon.SetBounds(S(8), S(8), S(30), S(30));
            title.SetBounds(S(44), S(6), Math.Max(1, width - S(50)), S(34));
            rate.SetBounds(S(8), S(42), Math.Max(1, width - S(16)), S(22));
            wins.SetBounds(S(8), S(68), half, S(22)); losses.SetBounds(S(8) + half, S(68), half, S(22));
            lp.SetBounds(S(8), S(94), Math.Max(1, width - S(16)), S(22));
            highest.SetBounds(S(8), S(118), Math.Max(1, width - S(16)), S(22));
            foreach (var label in new[] { title, rate, wins, losses, lp, highest }) label.AutoEllipsis = true;
        }
    }

    private void ApplyModeCountTheme(ThemePalette palette)
    {
        SetCardColors(_modeCard, palette.SurfaceRaised, palette.Surface, palette.Border);
        _modeTitle.ForeColor = palette.TextPrimary; _modeScope.ForeColor = palette.TextSecondary;
        _modeCounts.BackColor = Color.Transparent;
        foreach (Control label in _modeCounts.Controls) label.ForeColor = palette.TextPrimary;
    }

    private bool MatchesSelectedMode(MatchHistoryGame game) =>
        _recentMode.SelectedValue is not string mode || mode == "全部模式" || game.GetModeText() == mode;

    private void UpdateModeOptions()
    {
        string? selected = _recentMode.SelectedValue?.ToString();
        _updatingModes = true;
        try
        {
            _recentMode.Items.Clear();
            _recentMode.Items.Add("全部模式");
            foreach (string mode in (matchlists?.Games?.Games ?? []).Select(game => game.GetModeText()).Distinct().OrderBy(mode => mode))
                _recentMode.Items.Add(mode);
            int index = _recentMode.Items.IndexOf(selected ?? "全部模式");
            _recentMode.SelectedIndex = Math.Max(0, index);
        }
        finally { _updatingModes = false; }
    }
}
