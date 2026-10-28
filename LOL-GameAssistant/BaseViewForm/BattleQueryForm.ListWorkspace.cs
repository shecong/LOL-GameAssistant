using SplitContainer = AntdUI.Splitter;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class BattleQueryForm : LOL_GameAssistant.Infrastructure.GameData.ILocalCacheStore
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
                case AntdUI.Segmented tabs: tabs.ColorScheme = scheme; tabs.BackColor = palette.Surface; break;
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(object Source, string Session, string Puuid, long Id), MatchDetail> _selectedDetails = [];
    private long _detailCacheGeneration;
    string LOL_GameAssistant.Infrastructure.GameData.ILocalCacheStore.DirectoryPath => Path.Combine(
        LOL_GameAssistant.Infrastructure.GameData.LocalDataCache<MatchDetail>.Root, "player-data", "details", "query-selection");
    (int Entries, long Bytes) LOL_GameAssistant.Infrastructure.GameData.ILocalCacheStore.MeasureMemory() => CacheDataSize.Measure(_selectedDetails.Values.ToArray());
    void LOL_GameAssistant.Infrastructure.GameData.ILocalCacheStore.Clear() { Interlocked.Increment(ref _detailCacheGeneration); _selectedDetails.Clear(); }
    private MatchHistoryGame? _selectedGame;
    private readonly AntdUI.Select _championFilter = new() { Width = 150, Height = UiMetrics.ControlHeight };
    private readonly AntdUI.Select _resultFilter = new() { Width = 112, Height = UiMetrics.ControlHeight };
    private readonly AntdUI.Label _recentSummary = new() { TextAlign = ContentAlignment.MiddleLeft };
    private readonly AntdUI.Label _sourceLabel = new() { Text = "SGP · 摘要优先 / 详情按需", Dock = DockStyle.Right, Width = 230, TextAlign = ContentAlignment.MiddleRight };
    private readonly FlowLayoutPanel _listFilters = new() { Dock = DockStyle.Fill, WrapContents = false };
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
        foreach (var select in panelStats.Controls.Find("matchStatsModeFilter", true).OfType<AntdUI.Select>())
            select.Text = select.SelectedValue?.ToString() ?? "";
        _columnHeader.Invalidate();
        stackMatches.Invalidate(true);
        UpdateResponsiveLayout();
    }
    private bool _adjustingSplit;

    private void InitializeListWorkspace()
    {
        LOL_GameAssistant.Infrastructure.GameData.LocalCacheRegistry.Register(this);
        _detailPane = new(_listAssets, _listChampions) { Dock = DockStyle.Fill };
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
            _currentPage = 1; _statsLoaded = false; if (_currentPlayer != null) await RenderMatchPageAsync();
        };
        _listFilters.Controls.Add(_championFilter); _listFilters.Controls.Add(_resultFilter); _listFilters.Controls.Add(reset);
        _historyHeader.Controls.Add(_listFilters); _historyHeader.Controls.Add(_sourceLabel); _historyHeader.Controls.Add(_historyTitle);
        _historyHeader.Padding = new Padding(4, 2, 4, 0);
        _historyHeader.SizeChanged += (_, _) => LayoutListFilters();
        panelHistory.Controls.Clear();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        foreach (Control control in new Control[] { _historyHeader, _columnHeader, stackMatches, pagination }) { control.Dock = DockStyle.Fill; control.Margin = Padding.Empty; }
        layout.Controls.Add(_historyHeader, 0, 0); layout.Controls.Add(_columnHeader, 0, 1); layout.Controls.Add(stackMatches, 0, 2); layout.Controls.Add(pagination, 0, 3);
        panelHistory.Controls.Add(layout);
        panelPlayer.Controls.Add(_recentSummary);
        _sourceLabel.Click += async (_, _) => { if (_currentPlayer != null) await RenderMatchPageAsync(); };
        _sourceLabel.Cursor = Cursors.Hand;
        UiLanguage.Changed += RefreshFilterLanguage;
        Disposed += (_, _) => UiLanguage.Changed -= RefreshFilterLanguage;
    }
    private void LayoutListFilters()
    {
        int S(int n) => n * DeviceDpi / 96;
        int w = _historyHeader!.ClientSize.Width;
        _historyTitle.Visible = w >= S(1150); _sourceLabel.Visible = w >= S(980);
        int width = _listFilters.ClientSize.Width;
        _modeSelect.Width = S(width < S(490) ? 116 : 170);
        _championFilter.Width = S(width < S(490) ? 112 : 150);
        _resultFilter.Width = S(width < S(490) ? 85 : 112);
        _listFilters.Controls[3].Visible = width >= S(400);
    }
    private void LayoutMatchWorkspace(bool reset = false)
    {
        if (_adjustingSplit || IsDisposed || _matchWorkspace.Panel2Collapsed) return;
        _adjustingSplit = true;
        try
        {
            // Orientation revalidates SplitterDistance immediately. During docking,
            // hiding or resizing either axis can briefly be smaller than the splitter.
            // Wait for the next SizeChanged instead of changing orientation in that state.
            if (Math.Min(_matchWorkspace.ClientSize.Width, _matchWorkspace.ClientSize.Height) <=
                _matchWorkspace.SplitterWidth + 2) return;
            int S(int n) => n * DeviceDpi / 96;
            bool narrow = _matchWorkspace.Width < S(1000);
            _matchWorkspace.Panel1MinSize = _matchWorkspace.Panel2MinSize = 0;
            var orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            bool change = _matchWorkspace.Orientation != orientation;
            // 换向时 AntdUI.Splitter 会按当前分隔距离重新校验，目标方向装不下时留到下次 SizeChanged。
            int dimension = narrow ? _matchWorkspace.Height : _matchWorkspace.Width;
            if (change && dimension < _matchWorkspace.SplitterDistance + _matchWorkspace.SplitterWidth + 1) return;
            _matchWorkspace.Orientation = orientation;
            int preferred = narrow ? dimension / 2 : dimension - Math.Clamp((int)(dimension * .40), S(430), S(670));
            if (reset || change || _matchWorkspace.SplitterDistance < S(180) || dimension - _matchWorkspace.SplitterDistance < S(160))
                _matchWorkspace.SplitterDistance = Math.Clamp(preferred, 1, dimension - _matchWorkspace.SplitterWidth - 1);
            ResizeMatchRows();
        }
        finally { _adjustingSplit = false; }
    }
    internal async Task SelectMatchAsync(MatchHistoryGame game, bool refresh = false)
    {
        if (IsDisposed || _currentPlayer == null || !_matchWorkspace.Visible ||
            _matchHistory?.Games?.Games.Any(g => g.GameId == game.GameId) != true) return;
        var source = _matchHistoryService; string puuid = _currentPlayer.Puuid;
        using var request = _detailScope.Begin();
        _selectedGame = game;
        foreach (var row in stackMatches.Controls.OfType<MatchHistoryListRow>()) row.Selected = row.Game.GameId == game.GameId;
        bool opening = _matchWorkspace.Panel2Collapsed;
        _matchWorkspace.Panel2Collapsed = false;
        LayoutMatchWorkspace(opening);
        _detailPane.SetLoading(game, puuid);
        var key = (Source: (object)source, Session: PlayerHistorySession.Key, Puuid: puuid, Id: game.GameId);
        long generation = Volatile.Read(ref _detailCacheGeneration);
        try
        {
            if (refresh) _selectedDetails.TryRemove(key, out _);
            if (!_selectedDetails.TryGetValue(key, out var detail))
            {
                detail = await source.GetDetailAsync(game.GameId, !refresh, request.Token);
                if (!request.IsCurrent || IsDisposed || !ReferenceEquals(source, _matchHistoryService) || _currentPlayer?.Puuid != puuid) return;
                if (key.Session != PlayerHistorySession.Key) throw new IOException("客户端会话已变化，请重新查询。");
                if (detail == null || detail.GameId != game.GameId || detail.GetParticipant(puuid) == null)
                    throw new IOException("接口未返回这局的完整玩家统计。");
                if (game.PlatformId.Length > 0 && detail.PlatformId.Length > 0 && game.PlatformId != detail.PlatformId)
                    throw new IOException("返回的大区与所选对局不一致。");
                if (_selectedDetails.Count >= 64) _selectedDetails.Clear();
                if (generation == Volatile.Read(ref _detailCacheGeneration)) _selectedDetails[key] = detail;
            }
            if (!request.IsCurrent) return;
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
        foreach (var row in stackMatches.Controls.OfType<MatchHistoryListRow>()) row.Selected = false;
        if (!_matchWorkspace.IsDisposed) _matchWorkspace.Panel2Collapsed = true;
    }
    private void UpdateSummaryWidgets(IReadOnlyList<MatchHistoryGame> games, string puuid)
    {
        var confirmed = games.Where(g => g.IsCompletedGame()).Select(g => g.GetParticipant(puuid)?.stats).Where(s => s?.HasWinResult == true).ToArray();
        int wins = confirmed.Count(s => s!.Win);
        _recentSummary.Text = confirmed.Length == 0 ? "本页胜负未提供" : $"本页有效 {confirmed.Length} 场\n{wins} 胜 {confirmed.Length - wins} 负 · 胜率 {wins * 100d / confirmed.Length:0}%";
        _sourceLabel.Text = $"{_matchHistory?.Games?.Source ?? "接口"} · {DateTime.Now:HH:mm} 更新 ↻";
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
