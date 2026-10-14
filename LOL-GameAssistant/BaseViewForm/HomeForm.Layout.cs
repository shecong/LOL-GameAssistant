using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class HomeForm
{
    private readonly AntdUI.Select _recentMode = new() { Name = "recentModeFilter", List = true, DropDownArrow = true, Width = 160, Height = 36 };
    private bool _updatingModes;
    private bool _updatingPagination;

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
        rankedCard.Height = 450 * DeviceDpi / 96;
        rankGrid.Controls.Remove(flexPanel);
        rankGrid.ColumnCount = 1;
        rankGrid.ColumnStyles.Clear();
        rankGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rankGrid.RowCount = 2;
        rankGrid.RowStyles.Clear();
        rankGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        rankGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        rankGrid.Controls.Add(flexPanel, 0, 1);
        soloPanel.Margin = new Padding(0, 0, 0, 8);
        flexPanel.Margin = Padding.Empty;
        playerCard.Dock = DockStyle.Top;
        playerCard.Height = 244 * DeviceDpi / 96;
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
