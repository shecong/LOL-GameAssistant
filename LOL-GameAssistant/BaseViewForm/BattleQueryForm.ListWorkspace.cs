using SplitContainer = AntdUI.Splitter;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class BattleQueryForm
{
    private static void ApplyListWorkspaceTheme(Control root, ThemePalette palette)
    {
        var scheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case AntdUI.Panel panel: panel.ColorScheme = scheme; panel.Back = palette.SurfaceRaised; panel.BorderColor = palette.Border; break;
                case AntdUI.Button button:
                    button.ColorScheme = scheme;
                    if (button.Type != AntdUI.TTypeMini.Primary) { button.DefaultBack = palette.SurfaceMuted; button.DefaultBorderColor = palette.Border; button.ForeColor = palette.TextPrimary; }
                    break;
                case AntdUI.Select select: select.ColorScheme = scheme; select.ForeColor = palette.TextPrimary; select.BackColor = palette.SurfaceRaised; break;
                case AntdUI.Input input: input.ColorScheme = scheme; input.BackColor = palette.SurfaceRaised; break;
                case AntdUI.Pagination pages: pages.ColorScheme = scheme; break;
                case AntdUI.Label label: label.ColorScheme = scheme; break;
                case AntdUI.Avatar avatar: avatar.ColorScheme = scheme; break;
            }
            if (child is not MatchHistoryDetailPane) ApplyListWorkspaceTheme(child, palette);
        }
    }
    private IGameAssetService _listAssets = null!;
    private IChampionCatalog _listChampions = null!;
    private readonly SplitContainer _matchWorkspace = new() { Dock = DockStyle.Fill, SplitterWidth = 8, Panel1MinSize = 0, Panel2MinSize = 0 };
    private MatchHistoryDetailPane _detailPane = null!;
    private MatchHistoryListHeader _columnHeader = null!;
    private readonly UiRequestScope _detailScope = new();
    private MatchHistoryGame? _selectedGame;
    private readonly AntdUI.Select _championFilter = new() { Width = 150, Height = UiMetrics.ControlHeight };
    private readonly AntdUI.Select _resultFilter = new() { Width = 112, Height = UiMetrics.ControlHeight };
    private readonly AntdUI.Label _recentSummary = new() { TextAlign = ContentAlignment.MiddleLeft };
    private readonly AntdUI.Label _sourceLabel = new() { Text = "SGP · 摘要优先 / 详情按需", Dock = DockStyle.Right, Width = 230, TextAlign = ContentAlignment.MiddleRight };
    private readonly FlowLayoutPanel _listFilters = new() { WrapContents = false };
    private bool _layingOutFilters;
    private bool _suppressListFilters;
    private int? _selectedChampion;
    private string _selectedResult = "全部结果";
    private bool _filterHistoryTruncated;
    private string _filterHistorySource = "接口";
    private int _filterMissingSummaryCount;
    private sealed record ChampionChoice(int? Id, string Name) { public override string ToString() => Id == null ? UiLanguage.T(Name) : Name; }
    private sealed record FilterChoice(string Value) { public override string ToString() => UiLanguage.T(Value); }
    private static string FilterValue(AntdUI.Select select) => (select.SelectedValue as FilterChoice)?.Value ?? "全部";
    private void RefreshFilterLanguage(object? sender, EventArgs args)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => RefreshFilterLanguage(sender, args)); return; }
        foreach (var select in new[] { _modeSelect, _championFilter, _resultFilter })
            select.Text = select.SelectedValue?.ToString() ?? "";
        RefreshRegionChoices();
        _columnHeader.Invalidate();
        stackMatches.Invalidate(true);
        UpdateResponsiveLayout();
    }

    private void InitializeListWorkspace()
    {
        _detailPane = new(_listAssets, _listChampions) { Dock = DockStyle.None, Visible = false };
        _detailPane.PreferredHeightChanged += ResizeMatchRows;
        _detailPane.CloseRequested += CloseSelectedDetail;
        _detailPane.RetryRequested += () => { if (_selectedGame is { } game) _ = SelectMatchAsync(game, true); };
        _detailPane.FullDetailRequested += (detail, puuid) => MatchDetailForm.OpenAndHandle(detail, puuid, this);
        _detailPane.PlayerRequested += (puuid, platform) => _ = QueryPlayerAsync(puuid, platform);
        _matchWorkspace.Panel2.Controls.Add(_detailPane);
        _matchWorkspace.Panel1.Controls.Add(panelHistory);
        _matchWorkspace.Panel2Collapsed = true;
        panelContent.Controls.Add(_matchWorkspace); _matchWorkspace.BringToFront();
        _matchWorkspace.SizeChanged += (_, _) => LayoutMatchWorkspace();
        _matchWorkspace.SplitterMoved += (_, _) => ResizeMatchRows();
        _columnHeader = new(() => stackMatches.ClientSize.Width) { Dock = DockStyle.Fill };

        // Reuse the existing filters and paging events, but give every region a real layout slot.
        _historyHeader!.Controls.Remove(_historyTitle); _historyHeader.Controls.Remove(_historyHint);
        _modeSelect.Parent?.Controls.Remove(_modeSelect);
        ControlLifetime.ClearAndDispose(_historyHeader);
        _historyTitle.Dock = DockStyle.Left; _historyTitle.Width = 205;
        _historyHint.Visible = false;
        _listFilters.Controls.Add(_modeSelect);
        _championFilter.Items.Add(new ChampionChoice(null, "全部英雄")); _championFilter.SelectedIndex = 0;
        foreach (var champion in _listChampions.GetAll()) _championFilter.Items.Add(new ChampionChoice(champion.Id, champion.DisplayName));
        foreach (string item in new[] { "全部结果", "胜利", "失败", "重开" }) _resultFilter.Items.Add(new FilterChoice(item));
        _resultFilter.SelectedIndex = 0;
        _championFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressListFilters) return;
            _selectedChampion = (_championFilter.SelectedValue as ChampionChoice)?.Id;
            _currentPage = 1; if (_currentPlayer != null) await RenderMatchPageAsync();
        };
        _resultFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressListFilters) return;
            _selectedResult = (_resultFilter.SelectedValue as FilterChoice)?.Value ?? "全部结果";
            _currentPage = 1; if (_currentPlayer != null) await RenderMatchPageAsync();
        };
        var reset = new AntdUI.Button { Text = "重置", Width = 60, Height = UiMetrics.ControlHeight };
        reset.Click += async (_, _) =>
        {
            _suppressListFilters = _suppressModeEvents = true;
            try { _selectedMode = "全部"; _modeSelect.SelectedIndex = 0; _selectedChampion = null; _championFilter.SelectedIndex = 0; _selectedResult = "全部结果"; _resultFilter.SelectedIndex = 0; }
            finally { _suppressListFilters = _suppressModeEvents = false; }
            _currentPage = 1; if (_currentPlayer != null) await RenderMatchPageAsync();
        };
        _listFilters.Controls.Add(_championFilter); _listFilters.Controls.Add(_resultFilter); _listFilters.Controls.Add(reset);
        _viewBar!.Controls.Add(_listFilters);
        _historyTitle.Visible = false;
        _historyHeader.Controls.Add(_sourceLabel);
        _historyHeader.Padding = new Padding(4, 2, 4, 0);
        panelHistory.Controls.Clear();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        foreach (Control control in new Control[] { _historyHeader, _columnHeader, stackMatches, pagination }) { control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; }
        _historyHeader.Controls.Add(pagination);
        pagination.BringToFront();
        layout.Controls.Add(_columnHeader, 0, 0); layout.Controls.Add(stackMatches, 0, 1); layout.Controls.Add(_historyHeader, 0, 2);
        panelHistory.Controls.Add(layout);
        panelPlayer.Controls.Add(_recentSummary);
        _sourceLabel.Click += async (_, _) => { if (_currentPlayer != null) await RenderMatchPageAsync(); };
        _sourceLabel.Cursor = Cursors.Hand;
        UiLanguage.Changed += RefreshFilterLanguage;
        Disposed += (_, _) => UiLanguage.Changed -= RefreshFilterLanguage;
    }
    private void LayoutListFilters()
    {
        if (_layingOutFilters || _viewBar == null || _listFilters.Controls.Count < 4) return;
        _layingOutFilters = true;
        try
        {
            int S(int n) => n * DeviceDpi / 96;
            int w = _viewBar.ClientSize.Width;
            _spectateButton.Dock = _pageSizeArea!.Dock = _backToTopButton!.Dock = DockStyle.None;
            var filterControls = _listFilters.Controls.Cast<Control>();
            int filterHeight = filterControls.Max(control => control.Height + control.Margin.Vertical);
            int rowHeight = Math.Max(filterHeight, _pageSizeArea.Controls.Cast<Control>().Max(control => control.Height + control.Margin.Vertical));
            _recordsLabel.SetBounds(0, 0, S(UiLanguage.IsEnglish ? 120 : 84), rowHeight);
            int right = w - S(4);
            _spectateButton.SetBounds(right - S(130), 0, S(130), rowHeight);
            right -= S(134);
            bool extraActions = w >= S(UiLanguage.IsEnglish ? 980 : 920);
            _backToTopButton.Visible = _pageSizeArea.Visible = extraActions;
            if (extraActions)
            {
                _backToTopButton.SetBounds(right - S(110), 0, S(110), rowHeight);
                right -= S(114);
                int pageSizeWidth = _pageSizeArea.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
                _pageSizeArea.SetBounds(right - pageSizeWidth, 0, pageSizeWidth, rowHeight);
                right -= pageSizeWidth + S(4);
            }
            int left = _recordsLabel.Right + S(4);
            bool compact = right - left < S(520);
            _modeSelect.Width = S(compact ? 116 : 170);
            _championFilter.Width = S(compact ? 112 : 150);
            _resultFilter.Width = S(compact ? 85 : 112);
            _listFilters.Controls[3].Width = S(60);
            int width = filterControls.Sum(control => control.Width + control.Margin.Horizontal);
            bool wrap = right - left < width;
            int available = wrap ? w : right - left;
            int filterTop = wrap ? rowHeight + S(3) : 0;
            _listFilters.SetBounds((wrap ? 0 : left) + Math.Max(0, (available - width) / 2), filterTop, Math.Min(width, w), filterHeight);
            if (_rootLayout != null)
                _rootLayout.RowStyles[2].Height = Math.Max(rowHeight, filterTop + filterHeight) + _viewBar.Margin.Vertical;
            _sourceLabel.Visible = panelHistory.ClientSize.Width >= S(650);
        }
        finally { _layingOutFilters = false; }
    }
    private void LayoutMatchWorkspace(bool reset = false)
    {
        // Details belong to the selected row. The second split panel stays closed,
        // so there is one full-width list and one vertical scrollbar at every size.
        if (!IsDisposed) ResizeMatchRows();
    }
    internal async Task SelectMatchAsync(MatchHistoryGame game, bool refresh = false)
    {
        if (IsDisposed || _currentPlayer == null || !_matchWorkspace.Visible ||
            _matchHistory?.Games?.Games.Any(g => g.GameId == game.GameId) != true) return;
        var source = _matchHistoryService; string puuid = _currentPlayer.Puuid;
        using var request = _detailScope.Begin();
        int previousScroll = -stackMatches.AutoScrollPosition.Y;
        _selectedGame = game;
        foreach (var row in stackMatches.Controls.OfType<MatchHistoryListRow>()) row.Selected = row.Game.GameId == game.GameId;
        if (_detailPane.Parent != stackMatches) stackMatches.Controls.Add(_detailPane);
        _detailPane.Visible = true;
        _detailPane.SetLoading(game, puuid);
        ResizeMatchRows();
        stackMatches.AutoScrollPosition = new Point(0, previousScroll);
        try
        {
            var detail = await PlayerHistoryRepository.For(source).GetSelectedDetailAsync(game.GameId, detail =>
            {
                if (detail.GameId != game.GameId || detail.GetParticipant(puuid) == null)
                    throw new IOException("接口未返回这局的完整玩家统计。");
                if (game.PlatformId.Length > 0 && detail.PlatformId.Length > 0 && game.PlatformId != detail.PlatformId)
                    throw new IOException("返回的大区与所选对局不一致。");
                return true;
            }, refresh, request.Token);
            if (!request.IsCurrent || IsDisposed || !ReferenceEquals(source, _matchHistoryService) || _currentPlayer?.Puuid != puuid) return;
            if (detail == null) throw new IOException("接口未返回这局的完整玩家统计。");
            foreach (var row in stackMatches.Controls.OfType<MatchHistoryListRow>().Where(row => row.Game.GameId == detail.GameId))
                row.ApplyDetailHonors(detail, puuid);
            _detailPane.SetDetail(detail, puuid);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (request.IsCurrent && !IsDisposed) _detailPane.SetError(error.Message);
        }
    }
    internal void CloseSelectedDetail()
    {
        _detailScope.Cancel(); _selectedGame = null;
        _detailPane.Visible = false;
        if (_detailPane.Parent == stackMatches) _matchWorkspace.Panel2.Controls.Add(_detailPane);
        foreach (var row in stackMatches.Controls.OfType<MatchHistoryListRow>()) row.Selected = false;
        if (!_matchWorkspace.IsDisposed) _matchWorkspace.Panel2Collapsed = true;
        ResizeMatchRows();
    }
    private void UpdateSummaryWidgets(IReadOnlyList<MatchHistoryGame> games, string puuid)
    {
        var confirmed = games.Where(g => g.IsCompletedGame()).Select(g => g.GetParticipant(puuid)?.stats).Where(s => s?.HasWinResult == true).ToArray();
        int wins = confirmed.Count(s => s!.Win);
        _recentSummary.Text = confirmed.Length == 0 ? "本页胜负未提供" : $"本页有效 {confirmed.Length} 场\n{wins} 胜 {confirmed.Length - wins} 负 · 胜率 {wins * 100d / confirmed.Length:0}%";
        string source = _matchHistory?.Games?.Source ?? "接口";
        _sourceLabel.Text = _matchHistory?.IsCached == true
            ? $"{source} · 缓存 · 刷新失败 ↻"
            : $"{source} · {DateTime.Now:HH:mm} 读取 ↻";
        _playerIdentityTip.SetToolTip(_sourceLabel, _matchHistory?.IsCached == true
            ? $"本次未取得新数据，显示缓存（保存于 {_matchHistory.AcquiredAt?.ToLocalTime():MM-dd HH:mm}）。点击重试。"
            : "本次接口读取时间；对局时间请看列表右侧。点击刷新。");
        _suppressListFilters = true;
        try
        {
            foreach (int id in games.Select(g => g.GetParticipant(puuid)?.championId ?? 0).Where(id => id > 0).Distinct())
                if (!_championFilter.Items.Cast<object>().OfType<ChampionChoice>().Any(c => c.Id == id))
                    _championFilter.Items.Add(new ChampionChoice(id, _listChampions.GetDisplayName(id)));
        }
        finally { _suppressListFilters = false; }
    }
    private bool HasListFilters => _selectedMode != "全部" || _selectedChampion != null || _selectedResult != "全部结果";
    private bool MatchesListFilters(MatchHistoryGame game, string puuid)
    {
        var mine = game.GetParticipant(puuid);
        if (_selectedMode != "全部" && game.GetModeText() != _selectedMode) return false;
        if (_selectedChampion != null && mine?.championId != _selectedChampion) return false;
        return _selectedResult switch
        {
            "重开" => !game.IsCompletedGame(),
            "胜利" => game.IsCompletedGame() && mine?.stats is { HasWinResult: true, Win: true },
            "失败" => game.IsCompletedGame() && mine?.stats is { HasWinResult: true, Win: false },
            _ => true
        };
    }
}
