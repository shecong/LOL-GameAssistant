using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using System.Globalization;
using System.Text;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>战绩查询页：管理玩家搜索、统计、分页和收藏操作。</summary>
    public partial class BattleQueryForm : UserControl, IThemeAware
    {
        private PlayerProfile? _currentPlayer;
        private IPlayerProfileService _playerProfileService;
        private readonly IProfileIconService _profileIconService;
        private IMatchHistoryService _matchHistoryService;
        private IRankedStatsService _rankedStatsService;
        private readonly IRegionalPlayerQueryFactory? _regionalQueries;
        private LOL_GameAssistant.Application.Teams.IPremadeDetectionService? _queryPremades;
        private string _queryPlatform = "";
        private readonly AntdUI.Select _regionSelect = new() { Width = 185, Height = UiMetrics.ControlHeight };
        private readonly IFavoritePlayerStore _favoritePlayerStore;
        private readonly ITextExportService _textExportService;
        private readonly IPlayerGrowthService _growthService;
        private PlayerGrowthControl? _growthView;
        private MatchHistoryResponse? _matchHistory;
        private Image? _ownedPlayerImage;
        private int _totalGameCount;
        private bool _historyTotalKnown = true;
        private bool _exportTruncated;
        private int _currentPage = 1;
        private int _pageSize = 10;
        private const int StatsLoadLimit = 100;
        private const int FilterLoadLimit = 5000;
        private List<MatchHistoryGame>? _filterHistory;
        private string _selectedMode = "全部";
        private bool _suppressModeEvents;
        private readonly AntdUI.Select _modeSelect = new() { Width = 180, Height = UiMetrics.ControlHeight };
        private const int DetailLoadConcurrency = 6;
        private const int MatchListHorizontalInset = 20;

        private List<FavoritePlayer> _favorites;

        private List<RawGameStat>? _rawGameStats;
        private bool _statsLoaded;
        private int _statsVersion;

        private bool _resizingMatchRows;
        private TableLayoutPanel? _rootLayout;
        private AntdUI.Button? _myHistoryButton;
        private FlowLayoutPanel? _pageSizeArea;
        private AntdUI.Button? _backToTopButton;
        private AntdUI.Panel? _historyHeader;

        private CancellationTokenSource? _pageLoadCts;
        private readonly UiRequestScope _searchScope = new();
        private readonly UiRequestScope _statsScope = new();
        private readonly UiRequestScope _exportScope = new();
        private bool _suppressPaginationEvents;
        private readonly ToolTip _playerIdentityTip = new();
        private readonly AntdUI.Label _historyTitle = new();
        private readonly AntdUI.Label _historyHint = new();
        private readonly AntdUI.Panel _searchSurface = new() { Radius = 10, BorderWidth = 1, Padding = new Padding(12) };
        private readonly AntdUI.Panel _profileSurface = new() { Radius = 10, BorderWidth = 1, Padding = new Padding(8) };
        private readonly AntdUI.Segmented _viewSwitch = new() { Width = 250, Height = 37 };
        private readonly AntdUI.Select _favoriteSelect = new() { Width = 245, Height = UiMetrics.ControlHeight, PlaceholderText = "收藏玩家" };
        private readonly AntdUI.Select _pageSizeSelect = new() { Width = 90, Height = UiMetrics.ControlHeight };

        private readonly AntdUI.Label _playerPlaceholder = new()
        {
            Dock = DockStyle.Fill,
            Text = "查询玩家后显示头像、等级与排位信息",
            TextAlign = ContentAlignment.MiddleCenter
        };

        private readonly FlowLayoutPanel _searchActions = new() { Dock = DockStyle.Top, Height = 42, WrapContents = false };
        private readonly FlowLayoutPanel _favoriteActions = new() { Dock = DockStyle.Bottom, Height = 42, WrapContents = true };
        private RankedQueue? solo, flex;

        /// <summary>统计计算使用的单场原始数据快照。</summary>
        private class RawGameStat
        {
            public string Mode = "";
            public double Kda;
            public bool Win;
            public int ChampionId;
        }

        /// <summary>初始化 BattleQueryForm 的实例状态。</summary>
        public BattleQueryForm() : this(
            AppCompositionRoot.PlayerProfileService,
            AppCompositionRoot.ProfileIconService,
            AppCompositionRoot.MatchHistoryService,
            AppCompositionRoot.RankedStatsService,
            AppCompositionRoot.FavoritePlayerStore,
            AppCompositionRoot.TextExportService, regionalQueries: AppCompositionRoot.RegionalPlayerQueries)
        {
        }

        /// <summary>战绩查询页只依赖应用服务，不直接解析 LCU JSON 或调用静态数据接口。</summary>
        internal BattleQueryForm(
            IPlayerProfileService playerProfileService,
            IProfileIconService profileIconService,
            IMatchHistoryService matchHistoryService,
            IRankedStatsService rankedStatsService,
            IFavoritePlayerStore favoritePlayerStore,
            ITextExportService textExportService, IPlayerGrowthService? growthService = null,
            IRegionalPlayerQueryFactory? regionalQueries = null)
        {
            _playerProfileService = playerProfileService;
            _regionalQueries = regionalQueries;
            _profileIconService = profileIconService;
            _matchHistoryService = matchHistoryService;
            _rankedStatsService = rankedStatsService;
            _favoritePlayerStore = favoritePlayerStore;
            _textExportService = textExportService;
            _growthService = growthService ?? AppCompositionRoot.PlayerGrowthService;
            _favorites = _favoritePlayerStore.Load();
            InitializeComponent();
            WindowLayout.ConfigureDpi(this);
            Resize += (_, _) => UpdateResponsiveLayout();
            DpiChangedAfterParent += (_, _) => UpdateResponsiveLayout();
            stackMatches.SizeChanged += (_, _) => ResizeMatchRows();
            panelPlayer.SizeChanged += (_, _) => LayoutPlayerPanel();
            ConfigureCopyablePlayerIdentity();
            InitializeModernLayout();
            Disposed += (_, _) =>
            {
                _searchScope.Dispose();
                _statsScope.Dispose();
                _exportScope.Dispose();
                CancelPageLoad();
                _playerIdentityTip.Dispose();
                _ownedPlayerImage?.Dispose();
            };
        }

        /// <summary>组装战绩查询界面的现代布局和操作控件。</summary>
        private void InitializeModernLayout()
        {
            Controls.Clear();
            panelSearch.Controls.Clear();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(UiMetrics.SpaceLarge, UiMetrics.SpaceMedium, UiMetrics.SpaceLarge, 10) };
            _rootLayout = root;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));

            inpSearch.Width = 420;
            inpSearch.Height = UiMetrics.ControlHeight;
            _searchActions.Controls.Add(inpSearch);
            foreach (var region in _regionalQueries?.Regions ?? [new PlayerQueryRegion("", "当前大区（自动）")]) _regionSelect.Items.Add(region);
            _regionSelect.SelectedIndex = 0;
            _regionSelect.Enabled = _regionalQueries != null;
            _regionSelect.SelectedIndexChanged += (_, _) =>
            {
                if (_regionSelect.SelectedValue is PlayerQueryRegion region) SelectQueryRegion(region.Platform);
            };
            _playerIdentityTip.SetToolTip(_regionSelect, "跨区查询请使用 PUUID；跨区排位、回放及本机英雄档案不可用。");
            _searchActions.Controls.Add(_regionSelect);
            _searchActions.Controls.Add(btnSearch);
            var myHistory = new AntdUI.Button { Text = "我的战绩", Size = new Size(92, UiMetrics.ControlHeight), Margin = new Padding(6, 1, 0, 0) };
            _myHistoryButton = myHistory;
            myHistory.Click += async (_, _) =>
            {
                if (IsDisposed) return;
                SelectQueryRegion("");
                using var request = _searchScope.Begin();
                try
                {
                    PlayerProfile? self = await _playerProfileService.GetCurrentAsync(request.Token);
                    if (request.IsCurrent && !string.IsNullOrWhiteSpace(self?.Puuid)) await PerformSearchAsync(self.Puuid);
                }
                catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
                catch (Exception ex) { RuntimeDiagnostics.WriteException(ex); }
            };
            _searchActions.Controls.Add(myHistory);

            foreach (int size in new[] { 10, 20, 50 }) _pageSizeSelect.Items.Add(size);
            _pageSizeSelect.SelectedIndex = 0;
            _pageSizeSelect.SelectedIndexChanged += (_, _) =>
            {
                if (_pageSizeSelect.SelectedIndex >= 0) cboPageSize.SelectedIndex = _pageSizeSelect.SelectedIndex;
            };
            _favoriteSelect.SelectedIndexChanged += (_, _) => cboFavorites.SelectedIndex = _favoriteSelect.SelectedIndex;
            _favoriteActions.Controls.Add(_favoriteSelect);
            _favoriteActions.Controls.Add(btnLoadFavorite);
            _favoriteActions.Controls.Add(btnFavorite);
            _favoriteActions.Controls.Add(btnExport);
            _searchSurface.Dock = DockStyle.Fill;
            _searchSurface.Margin = new Padding(0, 0, 0, 8);
            _searchSurface.Controls.Add(_searchActions);
            _searchSurface.Controls.Add(_favoriteActions);

            panelPlayer.Dock = DockStyle.Fill;
            panelPlayer.Visible = false;
            _profileSurface.Dock = DockStyle.Fill;
            _profileSurface.Margin = new Padding(0, 0, 0, 8);
            _profileSurface.Controls.Add(panelPlayer);
            _profileSurface.Controls.Add(_playerPlaceholder);

            _viewSwitch.Items.Add(new AntdUI.SegmentedItem { Text = "对局记录" });
            _viewSwitch.Items.Add(new AntdUI.SegmentedItem { Text = "数据统计" });
            _viewSwitch.Items.Add(new AntdUI.SegmentedItem { Text = "英雄池档案" });
            _viewSwitch.Items.Add(new AntdUI.SegmentedItem { Text = "个人复盘中心" });
            _viewSwitch.SelectIndexChanged += (_, e) =>
            {
                if (e.Value >= 2 && _queryPlatform.Length > 0)
                {
                    _viewSwitch.SelectIndex = 0;
                    lblStatus.Text = "指定大区查询支持战绩和数据统计；英雄档案及复盘请切回当前大区。";
                    return;
                }
                if (e.Value == 0) BtnViewRecord_Click(null, EventArgs.Empty);
                else if (e.Value == 1) BtnViewStats_Click(null, EventArgs.Empty);
                else _ = ShowGrowthAsync(e.Value == 2);
            };
            _viewSwitch.SelectIndex = 0;
            var viewBar = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 8, BorderWidth = 1, Margin = new Padding(0, 0, 0, 8) };
            _viewSwitch.Dock = DockStyle.Left;
            viewBar.Controls.Add(_viewSwitch);
            var pageSizeArea = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 145, WrapContents = false };
            _pageSizeArea = pageSizeArea;
            pageSizeArea.Controls.Add(new AntdUI.Label { Text = "每页", Width = 42, Height = 34 });
            pageSizeArea.Controls.Add(_pageSizeSelect);
            viewBar.Controls.Add(pageSizeArea);
            var backToTop = new AntdUI.Button { Text = "回到顶部", Dock = DockStyle.Right, Width = 96 };
            _backToTopButton = backToTop;
            backToTop.Click += (_, _) => stackMatches.AutoScrollPosition = Point.Empty;
            viewBar.Controls.Add(backToTop);

            panelContent.Dock = DockStyle.Fill;
            panelContent.Margin = Padding.Empty;
            lblStatus.Dock = DockStyle.Fill;
            root.Controls.Add(_searchSurface, 0, 0);
            root.Controls.Add(_profileSurface, 0, 1);
            root.Controls.Add(viewBar, 0, 2);
            root.Controls.Add(panelContent, 0, 3);
            root.Controls.Add(lblStatus, 0, 4);
            Controls.Add(root);

            var historyHeader = new AntdUI.Panel { Dock = DockStyle.Top, Height = 46, Radius = 0, Padding = new Padding(10, 3, 10, 0) };
            _historyHeader = historyHeader;
            _historyTitle.Dock = DockStyle.Top;
            _historyTitle.Height = 23;
            _historyTitle.Text = "最近对局";
            _historyTitle.Font = new Font(UiMetrics.FontFamily, 10F, FontStyle.Bold);
            _historyHint.Dock = DockStyle.Fill;
            _historyHint.Text = "滚轮浏览记录 · 点击详情查看完整对局";
            historyHeader.Controls.Add(_historyHint);
            historyHeader.Controls.Add(_historyTitle);
            var modeArea = new Panel { Dock = DockStyle.Right, Width = 190, Padding = new Padding(4, 2, 0, 0) };
            foreach (string mode in new[] { "全部", "峡谷单双排", "峡谷灵活组排", "峡谷匹配", "峡谷匹配（盲选）",
                "峡谷快速游戏", "深渊大乱斗", "海克斯大乱斗", "斗魂竞技场", "无限火力", "克隆大作战",
                "终极魔典", "极限闪击", "人机对战", "自定义对局", "其他模式" })
                _modeSelect.Items.Add(mode);
            _modeSelect.Name = "matchModeFilter";
            _modeSelect.SelectedIndex = 0;
            _modeSelect.SelectedIndexChanged += async (_, _) =>
            {
                if (!_suppressModeEvents) await ApplyModeFilterAsync(_modeSelect.SelectedValue?.ToString() ?? "全部");
            };
            modeArea.Controls.Add(_modeSelect);
            historyHeader.Controls.Add(modeArea);
            // 先为右侧筛选器分配空间，再让标题与提示填充余下区域。
            modeArea.SendToBack();
            panelHistory.Controls.Add(historyHeader);
            historyHeader.SendToBack();
            pagination.BringToFront();
            stackMatches.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                Text = "输入 Riot ID（名称#TAG）或 PUUID 查询战绩",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = UiTheme.Palette.TextSecondary
            });
            ApplyTheme(UiTheme.Palette);
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Surface;
            _searchSurface.BackColor = palette.SurfaceRaised;
            _searchSurface.BorderColor = _profileSurface.BorderColor = palette.Border;
            _profileSurface.BackColor = palette.SurfaceRaised;
            _playerPlaceholder.ForeColor = palette.TextSecondary;
            panelPlayer.BackColor = palette.SurfaceRaised;
            panelContent.BackColor = palette.Surface;
            panelHistory.BackColor = palette.Surface;
            panelStats.BackColor = palette.SurfaceMuted;
            stackMatches.BackColor = palette.Surface;
            _historyTitle.ForeColor = palette.TextPrimary;
            _historyHint.ForeColor = palette.TextSecondary;
            lblStatus.ForeColor = palette.TextSecondary;
        }

        /// <summary>更新战绩区域的标题和分页信息。</summary>
        private void UpdateHistoryHeader(int totalGames = 0)
        {
            _historyTitle.Text = !_historyTotalKnown && _selectedMode == "全部" ? "最近对局 · SGP（总数未知）" : _selectedMode == "全部"
                ? totalGames > 0 ? $"最近对局 · {totalGames} 场" : "最近对局"
                : $"{_selectedMode} · {totalGames} 场";
            _historyHint.Text = totalGames > 0
                ? "滚轮浏览记录 · 点击详情查看完整对局；双方资料自动补齐。"
                : "输入 Riot ID（名称#TAG）或 PUUID 查询；每局直接展示双方玩家。";
            if (_selectedMode != "全部") _historyHint.Text = "按客户端可读取的最近记录筛选（最多 5000 场）";
            if (_queryPlatform.Length > 0) _historyHint.Text = $"查询大区：{_queryPlatform} · 跨区请用 PUUID · 仅战绩与数据统计，不提供跨区排位/回放。";
        }

        /// <summary>
        /// 玩家名称与 Riot ID 均可直接点击复制，方便转发或再次检索。
        /// </summary>
        private void ConfigureCopyablePlayerIdentity()
        {
            lblPlayerName.Cursor = Cursors.Hand;
            lblPlayerTag.Cursor = Cursors.Hand;
            lblPlayerName.Click += (_, _) => CopyPlayerIdentity(_currentPlayer?.GameName, "名称");
            lblPlayerTag.Click += (_, _) => CopyPlayerIdentity(lblPlayerTag.Text, "ID");
            _playerIdentityTip.SetToolTip(lblPlayerName, "点击复制名称");
            _playerIdentityTip.SetToolTip(lblPlayerTag, "点击复制 ID");
        }

        /// <summary>复制当前玩家的 Riot ID，供搜索或分享使用。</summary>
        private void CopyPlayerIdentity(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            try
            {
                Clipboard.SetText(value);
                lblStatus.Text = $"已复制{label}：{value}";
                if (ParentForm != null)
                {
                    LOL_GameAssistant.Helper.UiMessage.success(ParentForm, $"已复制{label}");
                }
            }
            catch
            {
                lblStatus.Text = $"复制{label}失败";
                if (ParentForm != null)
                {
                    LOL_GameAssistant.Helper.UiMessage.error(ParentForm, $"复制{label}失败，请重试");
                }
            }
        }

        /// <summary>在控件加载后完成依赖尺寸或句柄的初始化。</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshFavoriteList(null);
            UpdateResponsiveLayout();
            _ = InitializeDefaultSearchAsync();
        }

        /// <summary>按可用宽度调整界面布局，避免内容超出容器。</summary>
        private void UpdateResponsiveLayout()
        {
            if (IsDisposed) return;
            int Scale(int value) => value * DeviceDpi / 96;
            _viewSwitch.Width = Math.Min(Scale(600), Math.Max(Scale(310), ClientSize.Width - Scale(ClientSize.Width > Scale(950) ? 330 : 65)));
            if (_pageSizeArea != null) _pageSizeArea.Visible = ClientSize.Width > Scale(950) && _viewSwitch.SelectIndex == 0;
            if (_backToTopButton != null) _backToTopButton.Visible = ClientSize.Width > Scale(950) && _viewSwitch.SelectIndex == 0;
            _viewSwitch.Height = Scale(37);
            _favoriteSelect.Width = Scale(245);
            _pageSizeSelect.Width = Scale(90);
            _regionSelect.Width = Scale(185);
            _pageSizeSelect.Height = Scale(UiMetrics.ControlHeight);
            if (_myHistoryButton != null) _myHistoryButton.Width = Scale(120);
            if (_backToTopButton != null) _backToTopButton.Width = Scale(110);
            if (_pageSizeArea != null)
            {
                _pageSizeArea.Width = Scale(155);
                _pageSizeArea.Controls[0].Size = new Size(Scale(48), Scale(34));
            }
            if (_historyHeader != null) _historyHeader.Height = Scale(46);
            _historyTitle.Height = Scale(23);
            _modeSelect.Size = new Size(Scale(180), Scale(UiMetrics.ControlHeight));
            if (_modeSelect.Parent != null) _modeSelect.Parent.Width = Scale(190);
            if (_rootLayout != null)
            {
                _rootLayout.RowStyles[2].Height = Scale(52);
                _rootLayout.RowStyles[4].Height = Scale(33);
            }
            foreach (Control control in _searchActions.Controls.Cast<Control>().Concat(_favoriteActions.Controls.Cast<Control>()))
                control.Height = Math.Max(control.Height, Scale(UiMetrics.ControlHeight));
            LayoutSearchPanel();
            LayoutPlayerPanel();
            ResizeMatchRows();
        }

        /// <summary>
        /// 搜索工具栏在窄窗口中自动换行并增加高度，避免固定 56px 时裁剪控件。
        /// </summary>
        private void LayoutSearchPanel()
        {
            int width = _searchActions.ClientSize.Width;
            if (width <= 0) return;
            int Scale(int value) => value * DeviceDpi / 96;
            bool narrow = width < Scale(620);
            _searchActions.WrapContents = width < Scale(720);
            _searchActions.Height = Scale(width < Scale(720) ? 144 : 48);
            _favoriteActions.Height = Scale(narrow ? 76 : 42);
            if (_rootLayout != null)
                _rootLayout.RowStyles[0].Height = Scale(width < Scale(720) ? 244 : 120);
            int available = width - _searchActions.Controls.Cast<Control>().Where(c => c != inpSearch).Sum(c => c.Width + c.Margin.Horizontal) - Scale(12);
            inpSearch.Width = Math.Clamp(available, Scale(160), Scale(620));
        }

        /// <summary>
        /// 玩家概览优先保留资料区域，剩余空间再分配给排位面板，防止窄窗口中两个区域重叠。
        /// </summary>
        private void LayoutPlayerPanel()
        {
            if (panelPlayer.ClientSize.Width <= 0) return;
            int Scale(int value) => value * DeviceDpi / 96;
            void Bounds(Control control, int x, int y, int width, int height) => control.SetBounds(x, Scale(y), width, Scale(height));
            if (_currentPlayer == null)
            {
                if (_rootLayout != null) _rootLayout.RowStyles[1].Height = Scale(64);
                return;
            }
            panelRanked.Dock = DockStyle.None;
            panelPlayerInfo.Dock = DockStyle.None;
            avatarPlayer.Dock = DockStyle.None;
            lblPlayerName.Dock = DockStyle.None;
            lblPlayerTag.Dock = DockStyle.None;
            lblPlayerLevel.Dock = DockStyle.None;
            lblSoloTitle.Dock = DockStyle.None;
            lblSoloStats.Dock = DockStyle.None;
            lblFlexTitle.Dock = DockStyle.None;
            lblFlexStats.Dock = DockStyle.None;
            int width = panelPlayer.ClientSize.Width;
            Bounds(avatarPlayer, Scale(8), 4, Scale(84), 88);
            if (width < Scale(650))
            {
                if (_rootLayout != null) _rootLayout.RowStyles[1].Height = Scale(226);
                Bounds(panelPlayerInfo, Scale(104), 4, Math.Max(Scale(120), width - Scale(112)), 88);
                Bounds(panelRanked, Scale(8), 100, Math.Max(Scale(200), width - Scale(16)), 88);
            }
            else
            {
                if (_rootLayout != null) _rootLayout.RowStyles[1].Height = Scale(118);
                int rankedWidth = Math.Clamp(width - Scale(112 + 245), Scale(240), Scale(472));
                Bounds(panelPlayerInfo, Scale(104), 4, Math.Max(Scale(160), width - Scale(112) - rankedWidth), 88);
                Bounds(panelRanked, width - rankedWidth - Scale(8), 4, rankedWidth, 88);
            }
            int identityWidth = panelPlayerInfo.ClientSize.Width;
            Bounds(lblPlayerName, 0, 0, identityWidth, 32);
            Bounds(lblPlayerTag, 0, 32, identityWidth, 26);
            Bounds(lblPlayerLevel, 0, 58, identityWidth, 26);
            int rankedTextWidth = panelRanked.ClientSize.Width;
            Bounds(lblSoloTitle, 0, 0, rankedTextWidth, 22);
            Bounds(lblSoloStats, 0, 22, rankedTextWidth, 22);
            Bounds(lblFlexTitle, 0, 44, rankedTextWidth, 22);
            Bounds(lblFlexStats, 0, 66, rankedTextWidth, 22);
        }

        /// <summary>
        /// 查询结果为手工定位控件；容器宽度改变后必须重新设置每行宽度和滚动范围。
        /// </summary>
        private void ResizeMatchRows()
        {
            if (_resizingMatchRows) return;
            var rows = stackMatches.Controls.Cast<Control>()
                .Where(row => row is MatchHistoryCard or MatchHistoryPreviewCard)
                .OrderBy(row => row.Tag is int index ? index : int.MaxValue)
                .ToList();
            if (rows.Count == 0 || stackMatches.ClientSize.Width <= 0) return;
            _resizingMatchRows = true;
            try
            {
                MatchListScrolling.LayoutRows(stackMatches, rows, 10, 8);
            }
            finally { _resizingMatchRows = false; }
        }

        /// <summary>
        /// 默认在搜索框填入当前登录玩家 puuid。
        /// </summary>
        private async Task InitializeDefaultSearchAsync()
        {
            string initial = inpSearch.Text;
            try
            {
                PlayerProfile? self = await _playerProfileService.GetCurrentAsync(_searchScope.Token);
                if (!string.IsNullOrEmpty(self?.Puuid) && !IsDisposed && inpSearch.Text == initial && string.IsNullOrWhiteSpace(initial))
                {
                    inpSearch.Text = self.Puuid;
                }
            }
            catch
            {
                // 默认值获取失败不阻塞界面
            }
        }

        // ── 搜索 ──

        private void InpSearch_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                _ = PerformSearchAsync();
            }
        }

        /// <summary>响应查询按钮，解析输入并启动玩家查询。</summary>
        private async void BtnSearch_Click(object? sender, EventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// 执行搜索；可从其他界面传入 puuid / 名称#TAG 直接跳转查询。
        /// </summary>
        public async Task PerformSearchAsync(string? input = null)
        {
            if (IsDisposed) return;
            input = (input ?? inpSearch.Text).Trim();
            if (string.IsNullOrEmpty(input))
            {
                LOL_GameAssistant.Helper.UiMessage.warn(ParentForm!, "请输入 puuid 或 名称#TAG");
                return;
            }
            inpSearch.Text = input;

            using var request = _searchScope.Begin();
            var token = request.Token;
            _statsScope.Cancel();
            _exportScope.Cancel();
            CancelPageLoad();

            _growthView?.ResetPlayer();
            lblStatus.Text = "正在搜索...";
            btnSearch.Enabled = false;
            try
            {
                string? puuid = await ResolvePuuidAsync(input, token);
                if (!request.IsCurrent) return;
                if (string.IsNullOrEmpty(puuid))
                {
                    lblStatus.Text = "未找到该玩家";
                    LOL_GameAssistant.Helper.UiMessage.error(ParentForm!, "未找到该玩家");
                    return;
                }

                _statsLoaded = false;
                await LoadPlayerDataAsync(puuid, token);
            }
            catch (Exception ex)
            {
                if (!request.IsCurrent) return;
                lblStatus.Text = $"查询失败：{ex.Message}";
                RuntimeDiagnostics.Report("战绩查询", "查询失败", ex.Message);
            }
            finally
            {
                if (request.IsCurrent) { btnSearch.Enabled = true; }
            }
        }

        internal void SelectQueryRegion(string platform)
        {
            if (_regionalQueries == null) return;
            platform = platform.ToUpperInvariant();
            if (_queryPlatform == platform) return;
            var services = _regionalQueries.Create(platform);
            _searchScope.Cancel(); _statsScope.Cancel(); _exportScope.Cancel(); CancelPageLoad();
            _queryPlatform = platform;
            _playerProfileService = services.Profiles; _matchHistoryService = services.History;
            _rankedStatsService = services.Ranked; _queryPremades = services.Premades;
            _currentPlayer = null; _matchHistory = null; _filterHistory = null; _rawGameStats = null;
            _statsLoaded = false; _statsVersion++; _currentPage = 1; _totalGameCount = 0; _historyTotalKnown = true;
            solo = flex = null; ClearMatchControls(); pagination.Visible = false;
            _growthView?.Dispose(); _growthView = null;
            avatarPlayer.Image = null; _ownedPlayerImage?.Dispose(); _ownedPlayerImage = null;
            lblPlayerName.Visible = lblPlayerTag.Visible = lblPlayerLevel.Visible = avatarPlayer.Visible = false;
            lblSoloTitle.Visible = lblSoloStats.Visible = lblFlexTitle.Visible = lblFlexStats.Visible = false;
            _playerPlaceholder.Visible = true;
            btnSearch.Enabled = true;
            _regionSelect.SelectedIndex = _regionalQueries.Regions.ToList().FindIndex(r => r.Platform == platform);
            inpSearch.PlaceholderText = platform.Length == 0 ? "名称#TAG 或 PUUID" : "跨区查询请输入目标玩家 PUUID";
            _viewSwitch.SelectIndex = 0; panelHistory.Visible = true; panelStats.Visible = false;
            UpdateHistoryHeader();
            lblStatus.Text = platform.Length == 0 ? "已切回当前大区" : $"已选择 {platform} · 跨区请使用 PUUID，排位不可用";
        }

        /// <summary>
        /// 从其他界面（首页战绩卡、对局详情等）跳转到战绩查询并搜索指定玩家。
        /// </summary>
        public static async Task QueryPlayerAsync(string? puuid, string? platform = null)
        {
            if (string.IsNullOrEmpty(puuid)) return;
            var main = Program.GameMain;
            if (main == null) return;

            main.ShowBattleQueryPage();
            string target = platform?.ToUpperInvariant() ?? "";
            main.battleQueryForm.SelectQueryRegion(AppCompositionRoot.RegionalPlayerQueries.Regions.Any(r => r.Platform == target) ? target : "");
            await main.battleQueryForm.PerformSearchAsync(puuid);
        }

        /// <summary>
        /// 解析输入为 puuid：支持 名称#TAG、长字符串 puuid，最后回退到当前玩家历史战绩模糊匹配。
        /// </summary>
        private async Task<string?> ResolvePuuidAsync(string input, CancellationToken token)
        {
            if (input.Contains('#'))
            {
                var parts = input.Split('#', 2);
                string name = parts[0].Trim();
                string tag = parts[1].Trim();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(tag))
                {
                    PlayerProfile? player = await _playerProfileService.FindByRiotIdAsync(name, tag, token);
                    if (!string.IsNullOrEmpty(player?.Puuid)) return player.Puuid;
                }
            }
            else if (input.Length >= 30)
            {
                return input;
            }

            var byName = input.Contains('#') ? null : await _playerProfileService.FindByNameAsync(input, token);
            return byName?.Puuid ?? await ScanMatchHistoryForPuuidAsync(input, token);
        }

        /// <summary>扫描战绩信息以定位目标玩家的唯一标识。</summary>
        private async Task<string?> ScanMatchHistoryForPuuidAsync(string keyword, CancellationToken token)
        {
            try
            {
                PlayerProfile? self = await _playerProfileService.GetCurrentAsync(token);
                if (string.IsNullOrEmpty(self?.Puuid)) return null;

                var history = await _matchHistoryService.GetPageAsync(self.Puuid, 0, 199, token);
                if (history?.Games?.Games == null) return null;

                foreach (var game in history.Games.Games)
                {
                    if (game?.ParticipantIdentities == null) continue;
                    foreach (var identity in game.ParticipantIdentities)
                    {
                        var p = identity?.Player;
                        if (p == null) continue;
                        string full = $"{p.GameName}#{p.TagLine}";
                        if (full.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return p.Puuid;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch
            {
                // 兜底失败不影响主流程
            }
            return null;
        }

        private async Task ShowGrowthAsync(bool pool)
        {
            if (_queryPlatform.Length > 0)
            {
                lblStatus.Text = "指定大区查询支持战绩和数据统计；英雄档案及复盘请切回当前大区。";
                return;
            }
            _statsScope.Cancel(); CancelPageLoad();
            _growthView ??= CreateGrowthView();
            panelHistory.Visible = panelStats.Visible = false; _growthView.Visible = true; _growthView.BringToFront();
            UpdateResponsiveLayout();
            await _growthView.ShowAsync(_currentPlayer?.Puuid, pool, _selectedMode);
        }
        private PlayerGrowthControl CreateGrowthView()
        {
            var view = new PlayerGrowthControl(_growthService, _matchHistoryService, AppCompositionRoot.ChampionCatalog);
            view.ModeChanged += mode => { _selectedMode = mode; _statsLoaded = false; _statsVersion++; _suppressModeEvents = true; try { _modeSelect.SelectedValue = mode; } finally { _suppressModeEvents = false; } UpdateHistoryHeader(); };
            panelContent.Controls.Add(view); return view;
        }

        // ── 视图切换 ──

        private void BtnViewRecord_Click(object? sender, EventArgs e)
        {
            if (_growthView != null) { _growthView.Cancel(); _growthView.Visible = false; }
            panelHistory.Visible = true;
            panelStats.Visible = false;
            UpdateResponsiveLayout();
            if (_currentPlayer != null) _ = RenderMatchPageAsync();
            SetViewButtonState(true);
        }

        /// <summary>响应统计查看操作，刷新目标玩家的战绩统计。</summary>
        private async void BtnViewStats_Click(object? sender, EventArgs e)
        {
            if (_growthView != null) { _growthView.Cancel(); _growthView.Visible = false; }
            panelHistory.Visible = false;
            panelStats.Visible = true;
            UpdateResponsiveLayout();
            SetViewButtonState(false);

            if (_currentPlayer != null && !_statsLoaded)
            {
                await LoadAndShowStatsAsync();
            }
            else if (_statsLoaded)
            {
                RebuildStatsCharts(_selectedMode);
            }
        }

        /// <summary>切换统计查看按钮的可用状态和提示文本。</summary>
        private void SetViewButtonState(bool recordSelected)
        {
            btnViewRecord.Type = recordSelected ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            btnViewRecord.Font = new Font("Microsoft YaHei UI", 9F, recordSelected ? FontStyle.Bold : FontStyle.Regular);
            btnViewStats.Type = recordSelected ? AntdUI.TTypeMini.Default : AntdUI.TTypeMini.Primary;
            btnViewStats.Font = new Font("Microsoft YaHei UI", 9F, recordSelected ? FontStyle.Regular : FontStyle.Bold);
        }

        // ── 加载玩家数据 ──

        private async Task LoadPlayerDataAsync(string puuid, CancellationToken token)
        {
            lblStatus.Text = "正在加载玩家数据...";

            var player = await _playerProfileService.GetByPuuidAsync(puuid, token);
            if (IsDisposed || token.IsCancellationRequested) return;
            _currentPlayer = player;
            if (_currentPlayer == null) { lblStatus.Text = "获取玩家信息失败，请确认客户端已启动并登录。"; return; }

            CancelPageLoad();
            ClearMatchControls();
            pagination.Visible = false;
            _statsLoaded = false;
            _rawGameStats = null;
            _filterHistory = null;
            _statsVersion++;
            _matchHistory = null;
            _totalGameCount = 0;
            UpdateHistoryHeader();

            solo = null;
            flex = null;
            lblSoloTitle.Visible = lblSoloStats.Visible = lblFlexTitle.Visible = lblFlexStats.Visible = false;
            var rankedTask = LoadRankedDataAsync(puuid, token);
            var matchTask = LoadMatchHistoryAsync(puuid);

            avatarPlayer.Visible = true;
            try
            {
                byte[]? iconBytes = await _profileIconService.GetProfileIconAsync(player!.ProfileIconId, token);
                if (IsDisposed || token.IsCancellationRequested) { await Task.WhenAll(rankedTask, matchTask); return; }
                Image? profileImage = CopyToImage(iconBytes);
                if (profileImage != null)
                {
                    avatarPlayer.Image = profileImage;
                    _ownedPlayerImage?.Dispose();
                    _ownedPlayerImage = profileImage;
                }
            }
            catch { }

            if (IsDisposed || token.IsCancellationRequested) { await Task.WhenAll(rankedTask, matchTask); return; }
            lblPlayerName.Text = _currentPlayer.GameName;
            lblPlayerName.Visible = true;
            lblPlayerTag.Text = $"#{_currentPlayer.TagLine}";
            lblPlayerTag.Visible = true;
            lblPlayerLevel.Text = _currentPlayer.SummonerLevel > 0 ? $"等级: {_currentPlayer.SummonerLevel}" : "等级未提供";
            _playerIdentityTip.SetToolTip(lblPlayerLevel, _currentPlayer.SourceText);
            lblPlayerLevel.Visible = true;
            panelPlayer.Visible = true;
            _playerPlaceholder.Visible = false;
            UpdateResponsiveLayout();

            await Task.WhenAll(rankedTask, matchTask);

            if (!IsDisposed && !token.IsCancellationRequested) { RefreshFavoriteState(); if (_viewSwitch.SelectIndex >= 2) await ShowGrowthAsync(_viewSwitch.SelectIndex == 2); }
        }

        /// <summary>头像字节由应用服务提供，窗体只负责解码为 WinForms 图片。</summary>
        private static Image? CopyToImage(byte[]? bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0) return null;
                using var ms = new MemoryStream(bytes);
                using var temp = Image.FromStream(ms);
                return new Bitmap(temp);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>异步读取玩家排位数据并更新显示。</summary>
        private async Task LoadRankedDataAsync(string puuid, CancellationToken token)
        {
            try
            {
                var rankedData = await _rankedStatsService.GetAsync(puuid, cancellationToken: token);
                if (rankedData == null || IsDisposed || token.IsCancellationRequested) return;
                _playerIdentityTip.SetToolTip(lblSoloStats, rankedData.SourceText); _playerIdentityTip.SetToolTip(lblFlexStats, rankedData.SourceText);
                solo = rankedData.GetQueue(RankedQueues.Solo5x5);
                flex = rankedData.GetQueue(RankedQueues.Flex5x5);

                if (solo != null)
                {
                    lblSoloTitle.Visible = true;
                    lblSoloTitle.Text = "单双排";
                    lblSoloStats.Text = FormatRanked(RankedRecordResolver.Resolve(solo, _matchHistory, puuid)!);
                    lblSoloStats.Visible = true;
                }
                if (flex != null)
                {
                    lblFlexTitle.Visible = true;
                    lblFlexTitle.Text = "灵活组排";
                    lblFlexStats.Text = FormatRanked(RankedRecordResolver.Resolve(flex, _matchHistory, puuid)!);
                    lblFlexStats.Visible = true;
                }
            }
            catch (NotSupportedException)
            {
                if (IsDisposed || token.IsCancellationRequested) return;
                lblSoloTitle.Visible = lblSoloStats.Visible = lblFlexTitle.Visible = lblFlexStats.Visible = true;
                lblSoloTitle.Text = "单双排"; lblFlexTitle.Text = "灵活组排";
                lblSoloStats.Text = lblFlexStats.Text = "跨区排位不可用";
            }
            catch { }
        }

        /// <summary>将排位信息转换为界面使用的文本。</summary>
        private static string FormatRanked(RankedQueue entry)
        {
            string extra = "";
            if (entry.IsProvisional)
                extra += $" · 定位赛 {entry.ProvisionalGamesRemaining}/{entry.ProvisionalGameThreshold}";
            if (!string.IsNullOrEmpty(entry.MiniSeriesProgress))
                extra += $" · 晋级赛 {entry.MiniSeriesProgress}";
            if (!string.IsNullOrEmpty(entry.HighestTier) && entry.HighestTier != "NONE")
                extra += $" · 最高 {TierToChinese(entry.HighestTier)}{entry.HighestDivision}";

            if (!string.IsNullOrEmpty(entry.Tier) && entry.Tier != "NONE")
            {
                string record = entry.HasWinLossRecord
                    ? $"{entry.WinRate}% ({entry.Wins}W/{entry.Losses}L)" + (entry.RecordScope.Length > 0 ? $" · {entry.RecordScope}" : "")
                    : "赛季胜负未提供";
                return $"{TierToChinese(entry.Tier)} {entry.Division} · {entry.LeaguePoints}LP · {record}{extra}";
            }
            if (entry.IsProvisional)
                return $"定位赛 ({entry.ProvisionalGamesRemaining}/{entry.ProvisionalGameThreshold})";
            return "暂无排位数据";
        }

        /// <summary>读取当前玩家的战绩列表并准备分页展示。</summary>
        private async Task LoadMatchHistoryAsync(string puuid)
        {
            _currentPage = 1;
            await RenderMatchPageAsync();
        }

        internal async Task ApplyModeFilterAsync(string mode)
        {
            if (_selectedMode == mode) return;
            _selectedMode = mode;
            _suppressModeEvents = true;
            try { _modeSelect.SelectedValue = mode; }
            finally { _suppressModeEvents = false; }
            _currentPage = 1;
            _totalGameCount = 0;
            _statsLoaded = false;
            _rawGameStats = null;
            _statsVersion++;
            UpdateHistoryHeader();
            if (_currentPlayer == null) return;
            await RenderMatchPageAsync();
            if (_selectedMode == mode && panelStats.Visible) await LoadAndShowStatsAsync();
        }

        private async Task<List<MatchHistoryGame>?> GetFilteredHistoryAsync(string puuid, string mode, CancellationToken token)
        {
            if (_filterHistory == null)
            {
                var history = await _matchHistoryService.GetAllAsync(puuid, FilterLoadLimit, token);
                token.ThrowIfCancellationRequested();
                if (_currentPlayer?.Puuid != puuid || IsDisposed) return null;
                if (history?.Games?.Games == null) return null;
                _filterHistory = history.Games.Games.DistinctBy(game => game.GameId)
                    .OrderByDescending(game => game.GameCreation).ToList();
                _suppressModeEvents = true;
                try
                {
                    foreach (string availableMode in _filterHistory.Select(game => game.GetModeText()).Distinct())
                        if (!_modeSelect.Items.Contains(availableMode)) _modeSelect.Items.Add(availableMode);
                }
                finally { _suppressModeEvents = false; }
            }
            return _filterHistory.Where(game => mode == "全部" || game.GetModeText() == mode).ToList();
        }

        /// <summary>构建当前战绩页的卡片和占位状态。</summary>
        private async Task RenderMatchPageAsync(MatchHistoryResponse? preloadedPage = null)
        {
            if (_currentPlayer == null) return;
            CancelPageLoad();
            var pageLoad = CancellationTokenSource.CreateLinkedTokenSource(_searchScope.Token);
            _pageLoadCts = pageLoad;
            CancellationToken token = pageLoad.Token;
            string puuid = _currentPlayer.Puuid;
            int requestedPage = _currentPage;
            int requestedPageSize = _pageSize;
            string requestedMode = _selectedMode;
            bool enrichmentStarted = false;
            try
            {
                int start = (requestedPage - 1) * requestedPageSize;
                ClearMatchControls();
                // 加载中先显示微光占位，避免空白闪烁
                var shimmer = new ShimmerPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(246, 248, 251)
                };
                stackMatches.Controls.Add(shimmer);

                MatchHistoryResponse? page;
                if (requestedMode == "全部")
                    page = preloadedPage ?? await _matchHistoryService.GetPageAsync(
                        puuid, start, start + requestedPageSize - 1, token);
                else
                {
                    lblStatus.Text = "正在按模式筛选战绩…";
                    var filtered = await GetFilteredHistoryAsync(puuid, requestedMode, token);
                    page = filtered == null ? null : new MatchHistoryResponse { Games = new MatchHistoryGames
                    {
                        GameCount = filtered.Count, GameIndexBegin = start,
                        GameIndexEnd = start + requestedPageSize - 1,
                        Games = filtered.Skip(start).Take(requestedPageSize).ToList()
                    }};
                }
                if (!IsCurrentPage(puuid, requestedPage, requestedPageSize, token)) return;
                _matchHistory = page;
                if (solo != null) lblSoloStats.Text = FormatRanked(RankedRecordResolver.Resolve(solo, page, puuid)!);
                if (flex != null) lblFlexStats.Text = FormatRanked(RankedRecordResolver.Resolve(flex, page, puuid)!);
                if (page == null)
                {
                    ClearMatchControls();
                    lblStatus.Text = "战绩页读取失败，请稍后重试";
                    return;
                }
                var pageGames = page.Games?.Games?.OrderByDescending(g => g.GameCreation)
                    .Take(requestedPageSize).ToList();
                _historyTotalKnown = page.Games?.IsTotalKnown ?? true;
                _totalGameCount = Math.Max(page.Games?.GameCount ?? 0, pageGames?.Count ?? 0);
                _suppressPaginationEvents = true;
                try
                {
                    pagination.Total = _totalGameCount;
                    pagination.PageSize = requestedPageSize;
                    pagination.Current = requestedPage;
                    pagination.Visible = _totalGameCount > 0;
                }
                finally { _suppressPaginationEvents = false; }
                UpdateHistoryHeader(_totalGameCount);
                if (pageGames == null || pageGames.Count == 0)
                {
                    ClearMatchControls();
                    lblStatus.Text = requestedMode == "全部" ? "该页暂无比赛记录" : $"{requestedMode}暂无比赛记录";
                    return;
                }
                ClearMatchControls();
                var previews = new List<MatchHistoryPreviewCard>(pageGames.Count);
                for (int index = 0; index < pageGames.Count; index++)
                {
                    var preview = new MatchHistoryPreviewCard(pageGames[index], puuid)
                    {
                        Tag = index,
                        Width = Math.Max(1, stackMatches.ClientSize.Width - MatchListHorizontalInset)
                    };
                    stackMatches.Controls.Add(preview);
                    UiTheme.Apply(preview);
                    previews.Add(preview);
                }
                ResizeMatchRows();
                lblStatus.Text = $"已显示 {pageGames.Count} 场摘要，正在加载双方详情...";
                UpdateHistoryHeader(_totalGameCount);
                _ = EnrichMatchPageAsync(pageGames, previews, puuid, requestedPage,
                    requestedPageSize, pageLoad);
                enrichmentStarted = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!IsCurrentPage(puuid, requestedPage, requestedPageSize, token)) return;
                ClearMatchControls();
                lblStatus.Text = $"战绩页读取失败：{ex.Message}";
                RuntimeDiagnostics.Report("战绩查询", "战绩页读取失败", ex.Message);
            }
            finally
            {
                if (!enrichmentStarted)
                {
                    if (ReferenceEquals(_pageLoadCts, pageLoad)) _pageLoadCts = null;
                    pageLoad.Dispose();
                }
            }
        }

        /// <summary>补齐当前页的详细信息，避免首屏等待全部请求。</summary>
        private async Task EnrichMatchPageAsync(
            IReadOnlyList<MatchHistoryGame> games,
            IReadOnlyList<MatchHistoryPreviewCard> previews,
            string puuid,
            int page,
            int pageSize,
            CancellationTokenSource pageLoad)
        {
            CancellationToken token = pageLoad.Token;
            using var gate = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
            int failures = 0;
            try
            {
                var tasks = games.Select(async (head, index) =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        MatchDetail? detail = await _matchHistoryService.GetDetailAsync(
                            head.GameId, cancellationToken: token);
                        if (!IsCurrentPage(puuid, page, pageSize, token)) return;
                        if (detail?.GetParticipant(puuid) == null)
                        {
                            previews[index].SetDetailUnavailable();
                            Interlocked.Increment(ref failures);
                            return;
                        }

                        var card = new MatchHistoryCard(detail, puuid, AppCompositionRoot.GameAssetService, _queryPremades ?? AppCompositionRoot.PremadeDetectionService)
                        {
                            Tag = index,
                            Width = Math.Max(1, stackMatches.ClientSize.Width - MatchListHorizontalInset)
                        };
                        var preview = previews[index];
                        // 新卡片接替摘要的屏幕位置，重排才能保留用户正在阅读的记录。
                        card.Location = preview.Location;
                        stackMatches.Controls.Remove(preview);
                        preview.Dispose();
                        stackMatches.Controls.Add(card);
                        UiTheme.Apply(card);
                        card.SizeChanged += (_, _) => ResizeMatchRows();
                        ResizeMatchRows();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        if (!IsCurrentPage(puuid, page, pageSize, token)) return;
                        previews[index].SetDetailUnavailable();
                        if (Interlocked.Increment(ref failures) == 1)
                            RuntimeDiagnostics.Report("战绩查询", "对局详情读取失败", ex.Message);
                    }
                    finally { gate.Release(); }
                }).ToArray();
                await Task.WhenAll(tasks);
                if (!IsCurrentPage(puuid, page, pageSize, token)) return;
                int totalPages = Math.Max(1, (int)Math.Ceiling((double)_totalGameCount / pageSize));
                string position = _historyTotalKnown ? $"共 {_totalGameCount} 场 · 第 {page}/{totalPages} 页" : $"SGP · 第 {page} 页 · 总场数未知";
                lblStatus.Text = failures == 0 ? position : $"{position} · {failures} 场详情暂不可用，摘要仍可查看";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (IsCurrentPage(puuid, page, pageSize, token))
                    RuntimeDiagnostics.Report("战绩查询", "加载对局详情失败", ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_pageLoadCts, pageLoad)) _pageLoadCts = null;
                pageLoad.Dispose();
            }
        }

        /// <summary>检查异步结果是否仍属于当前战绩页。</summary>
        private bool IsCurrentPage(string puuid, int page, int pageSize, CancellationToken token) =>
            !IsDisposed && !token.IsCancellationRequested &&
            _currentPlayer?.Puuid == puuid && _currentPage == page && _pageSize == pageSize;

        /// <summary>取消旧战绩页的后台加载，避免结果回写新页面。</summary>
        private void CancelPageLoad()
        {
            _pageLoadCts?.Cancel();
            _pageLoadCts = null;
        }

        /// <summary>清空战绩卡片并释放已创建的控件资源。</summary>
        private void ClearMatchControls()
        {
            foreach (Control control in stackMatches.Controls.Cast<Control>().ToArray())
            {
                stackMatches.Controls.Remove(control);
                control.Dispose();
            }
            stackMatches.AutoScrollMinSize = Size.Empty;
            stackMatches.AutoScrollPosition = Point.Empty;
        }

        /// <summary>响应页码变化，加载对应的战绩页。</summary>
        private void Pagination_ValueChanged(object? sender, AntdUI.PagePageEventArgs e)
        {
            if (_suppressPaginationEvents) return;
            _currentPage = e.Current;
            _ = RenderMatchPageAsync();
        }

        /// <summary>
        /// 切换每页数量后重新加载当前页。
        /// </summary>
        private void CboPageSize_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cboPageSize.SelectedItem is int size && size > 0)
            {
                _pageSize = size;
                if (_matchHistory != null)
                {
                    _currentPage = 1;
                    _suppressPaginationEvents = true;
                    try
                    {
                        pagination.PageSize = _pageSize;
                        pagination.Current = 1;
                    }
                    finally { _suppressPaginationEvents = false; }
                    _ = RenderMatchPageAsync();
                }
            }
        }

        // ── 统计看板 ──

        private async Task LoadAndShowStatsAsync()
        {
            if (_currentPlayer == null) return;
            string puuid = _currentPlayer.Puuid;
            string requestedMode = _selectedMode;
            using var request = _statsScope.Begin();
            var token = request.Token;
            int version = ++_statsVersion;
            bool IsCurrentStats() => request.IsCurrent && !IsDisposed && version == _statsVersion &&
                _currentPlayer?.Puuid == puuid && _selectedMode == requestedMode;
            try
            {
                ControlLifetime.ClearAndDispose(panelStats);
                lblStatus.Text = "正在计算统计数据...";

                List<MatchHistoryGame>? games;
                if (requestedMode == "全部")
                {
                    var statsPage = await _matchHistoryService.GetPageAsync(puuid, 0, StatsLoadLimit - 1, token);
                    games = statsPage?.Games?.Games;
                }
                else games = await GetFilteredHistoryAsync(puuid, requestedMode, token);
                if (!IsCurrentStats()) return;
                if (games == null) throw new InvalidOperationException("近期战绩暂时不可用，请重试。");
                var recentGames = games.OrderByDescending(game => game.GameCreation).Take(StatsLoadLimit).ToList();
                var rawStats = new List<RawGameStat>();
                int totalWins = 0, totalLosses = 0;
                var championStats = new Dictionary<int, (int games, int wins)>();
                var kdaTrend = new List<(double kda, bool win)>();

                // 并行拉取对局详情，避免逐场串行等待
                using var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
                var detailTasks = recentGames.Select(async head =>
                {
                    await semaphore.WaitAsync(token);
                    try { return head.HasFullParticipants ? head.ToSummaryDetail() : await _matchHistoryService.GetDetailAsync(head.GameId, cancellationToken: token); }
                    finally { semaphore.Release(); }
                }).ToList();
                var details = await Task.WhenAll(detailTasks);
                if (!IsCurrentStats()) return;

                int totalGames = 0;
                for (int i = 0; i < recentGames.Count; i++)
                {
                    var detail = details[i];
                    if (detail == null || _currentPlayer == null) continue;
                    var gamer = detail.GetParticipant(puuid);
                    if (gamer?.stats == null) continue;

                    totalGames++;
                    bool win = gamer.IsWin();
                    if (win) totalWins++; else totalLosses++;

                    double gameKda = gamer.GetKdaRatio();
                    kdaTrend.Add((gameKda, win));
                    string mode = recentGames[i].GetModeText();
                    rawStats.Add(new RawGameStat { Mode = mode, Kda = gameKda, Win = win, ChampionId = gamer.championId });

                    var cur = championStats.GetValueOrDefault(gamer.championId);
                    championStats[gamer.championId] = (cur.games + 1, cur.wins + (win ? 1 : 0));

                    if (i % 20 == 0 || i == recentGames.Count - 1)
                        lblStatus.Text = $"正在计算统计数据... ({i + 1}/{recentGames.Count})";
                }

                _statsLoaded = true;
                _rawGameStats = rawStats;
                RebuildStatsCharts(requestedMode);
            }
            catch (Exception ex)
            {
                if (!IsCurrentStats()) return;
                _rawGameStats = new();
                RebuildStatsCharts(requestedMode);
                lblStatus.Text = $"统计加载失败：{ex.Message}";
                RuntimeDiagnostics.Report("战绩查询", "统计加载失败", ex.Message);
            }
        }

        /// <summary>
        /// 根据模式筛选重建统计图表。
        /// </summary>
        private void RebuildStatsCharts(string filter)
        {
            if (_rawGameStats == null) return;

            var filtered = string.IsNullOrEmpty(filter) || filter == "全部"
                ? _rawGameStats
                : _rawGameStats.Where(s => s.Mode == filter).ToList();

            int totalWins = filtered.Count(s => s.Win);
            int totalLosses = filtered.Count - totalWins;
            var champStats = new Dictionary<int, (int g, int w)>();
            var kdaList = new List<(double kda, bool win)>();

            foreach (var stat in filtered)
            {
                int cid = stat.ChampionId;
                var cur = champStats.GetValueOrDefault(cid);
                champStats[cid] = (cur.g + 1, cur.w + (stat.Win ? 1 : 0));
                kdaList.Add((stat.Kda, stat.Win));
            }

            double overallKda = filtered.Count > 0 ? filtered.Average(s => s.Kda) : 0;
            double winRate = filtered.Count > 0 ? Math.Round((double)totalWins / filtered.Count * 100, 1) : 0;

            foreach (Control old in panelStats.Controls.Cast<Control>().ToArray())
            {
                panelStats.Controls.Remove(old);
                old.Dispose();
            }
            ThemePalette palette = UiTheme.Palette;
            var filterBar = new AntdUI.Panel { Dock = DockStyle.Top, Height = 48, Radius = 8, BorderWidth = 1, Padding = new Padding(10, 6, 10, 4), BackColor = palette.SurfaceRaised };
            var modeSelect = new AntdUI.Select { Width = 200, Height = 34 };
            foreach (string mode in _modeSelect.Items.OfType<string>())
                modeSelect.Items.Add(mode);
            modeSelect.SelectedValue = filter;
            modeSelect.SelectedIndexChanged += async (_, _) =>
                await ApplyModeFilterAsync(modeSelect.SelectedValue?.ToString() ?? "全部");
            filterBar.Controls.Add(modeSelect);

            var chartContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = palette.SurfaceMuted };
            var content = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(5)
            };
            chartContainer.Controls.Add(content);
            chartContainer.Resize += (_, _) => content.Width = Math.Max(100, chartContainer.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);

            var summaries = new FlowLayoutPanel { Height = 90, WrapContents = true, Margin = new Padding(0, 0, 0, 10) };
            summaries.Controls.Add(StatTile("有效场次", filtered.Count.ToString(), palette));
            summaries.Controls.Add(StatTile("胜率", $"{winRate:F1}%", palette));
            summaries.Controls.Add(StatTile("平均 KDA", $"{overallKda:F2}", palette));
            content.Controls.Add(summaries);

            if (kdaList.Count > 1)
            {
                var trendData = kdaList.TakeLast(30).ToList();
                var kdaPanel = new Panel { Height = 210, BackColor = palette.SurfaceRaised, Margin = new Padding(0, 0, 0, 10) };
                kdaPanel.Paint += (s, e) => ChartDrawer.DrawKdaTrend(e.Graphics, kdaPanel.ClientRectangle, trendData, "KDA 趋势（近 N 场 · 绿=胜 红=负）");
                content.Controls.Add(kdaPanel);
            }

            if (champStats.Count > 0)
            {
                var champData = champStats
                    .OrderByDescending(x => x.Value.g).Take(10)
                    .Select(x => (GetChampionDisplayName(x.Key), x.Value.g, Math.Round((double)x.Value.w / x.Value.g * 100, 1)))
                    .ToList();
                var champPanel = new Panel { Height = Math.Max(80, champData.Count * 25 + 30), BackColor = palette.SurfaceRaised };
                champPanel.Paint += (s, e) => ChartDrawer.DrawChampionBars(e.Graphics, champPanel.ClientRectangle, champData, "常用英雄 Top 10");
                content.Controls.Add(champPanel);
            }
            panelStats.Controls.Add(chartContainer);
            panelStats.Controls.Add(filterBar);
            content.Resize += (_, _) =>
            {
                int width = Math.Max(280, content.ClientSize.Width - 18);
                summaries.Width = width;
                int columns = Math.Clamp((width + 9) / 189, 1, 3);
                int tileWidth = (width - (columns - 1) * 9) / columns;
                foreach (Control tile in summaries.Controls) tile.Width = tileWidth;
                summaries.Height = (3 + columns - 1) / columns * 82 + 8;
                foreach (Control chart in content.Controls.Cast<Control>().Where(control => control != summaries))
                    chart.Width = width;
            };
            content.Width = Math.Max(300, chartContainer.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            lblStatus.Text = filtered.Count == 0 ? $"{filter}暂无有效统计数据" :
                "筛选: " + filter + "  最近 " + filtered.Count + " 场 · 胜率 " + winRate + "%";
        }

        /// <summary>创建一个战绩统计指标卡片。</summary>
        private static AntdUI.Panel StatTile(string title, string value, ThemePalette palette)
        {
            var card = new AntdUI.Panel
            {
                Width = 180,
                Height = 78,
                Radius = 8,
                BorderWidth = 1,
                Margin = new Padding(0, 0, 9, 0),
                BackColor = palette.SurfaceRaised
            };
            card.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Top,
                Height = 37,
                Text = value,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = palette.TextPrimary,
                Padding = new Padding(10, 4, 0, 0)
            });
            card.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = title,
                ForeColor = palette.TextSecondary,
                Padding = new Padding(10, 7, 0, 0)
            });
            return card;
        }

        // ── 收藏玩家（新增功能） ──

        private void BtnFavorite_Click(object? sender, EventArgs e)
        {
            if (_currentPlayer == null || string.IsNullOrEmpty(_currentPlayer.Puuid))
            {
                LOL_GameAssistant.Helper.UiMessage.warn(ParentForm!, "请先查询一名玩家再收藏");
                return;
            }

            var existing = _favorites.FirstOrDefault(f => f.Puuid == _currentPlayer.Puuid && (f.Platform ?? "") == _queryPlatform);
            var updated = new List<FavoritePlayer>(_favorites);
            if (existing != null)
            {
                updated.Remove(existing);
            }
            else
            {
                updated.Add(new FavoritePlayer
                {
                    Puuid = _currentPlayer.Puuid,
                    Platform = _queryPlatform,
                    GameName = _currentPlayer.GameName,
                    TagLine = _currentPlayer.TagLine,
                    SummonerLevel = _currentPlayer.SummonerLevel.ToString(),
                    AddedAt = DateTime.Now
                });
            }

            try { _favoritePlayerStore.Save(updated); }
            catch (Exception ex)
            {
                LOL_GameAssistant.Helper.UiMessage.error(ParentForm!, $"收藏保存失败：{ex.Message}");
                return;
            }
            _favorites = updated;
            LOL_GameAssistant.Helper.UiMessage.success(ParentForm!, existing != null
                ? $"已取消收藏 {_currentPlayer.GameName}"
                : $"已收藏 {_currentPlayer.GameName}");
            RefreshFavoriteState();
        }

        /// <summary>响应收藏玩家选择，查询所选玩家。</summary>
        private async void BtnLoadFavorite_Click(object? sender, EventArgs e)
        {
            string input;
            if (cboFavorites.SelectedIndex >= 0 && cboFavorites.SelectedIndex < _favorites.Count)
            {
                input = _favorites[cboFavorites.SelectedIndex].Puuid;
                SelectQueryRegion(_favorites[cboFavorites.SelectedIndex].Platform ?? "");
            }
            else
            {
                // 左侧框既可选择收藏，也可直接输入 Riot ID；此前未选择收藏时会静默返回，
                // 用户会误以为“加载”按钮无效。
                input = cboFavorites.Text.Trim();
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                lblStatus.Text = "请从收藏列表选择玩家，或在左侧输入 Riot ID 后加载";
                LOL_GameAssistant.Helper.UiMessage.warn(ParentForm!, "请选择收藏玩家或输入 Riot ID");
                return;
            }

            inpSearch.Text = input;
            await PerformSearchAsync(input);
        }

        /// <summary>重新读取收藏列表并更新选择控件。</summary>
        private void RefreshFavoriteList(string? selectedPuuid)
        {
            cboFavorites.Items.Clear();
            _favoriteSelect.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < _favorites.Count; i++)
            {
                var fav = _favorites[i];
                cboFavorites.Items.Add($"{fav.GameName}#{fav.TagLine} ({fav.SummonerLevel})");
                _favoriteSelect.Items.Add($"{fav.GameName}#{fav.TagLine} ({fav.SummonerLevel})" + (string.IsNullOrWhiteSpace(fav.Platform) ? "" : $" · {fav.Platform}"));
                if (fav.Puuid == selectedPuuid && (fav.Platform ?? "") == _queryPlatform) selectedIndex = i;
            }
            cboFavorites.SelectedIndex = selectedIndex >= 0 ? selectedIndex : -1;
            _favoriteSelect.SelectedIndex = selectedIndex >= 0 ? selectedIndex : -1;
            btnLoadFavorite.Enabled = true;
        }

        /// <summary>同步当前玩家的收藏状态到界面。</summary>
        private void RefreshFavoriteState()
        {
            bool isFavorite = _currentPlayer != null && _favorites.Any(f => f.Puuid == _currentPlayer.Puuid && (f.Platform ?? "") == _queryPlatform);
            btnFavorite.Text = isFavorite ? "★ 已收藏" : "☆ 收藏";
            RefreshFavoriteList(_currentPlayer?.Puuid);
        }

        // ── 导出战绩 CSV（新增功能） ──

        private async void BtnExport_Click(object? sender, EventArgs e)
        {
            if (_currentPlayer == null || _matchHistory?.Games?.Games == null || _matchHistory.Games.Games.Count == 0)
            {
                LOL_GameAssistant.Helper.UiMessage.warn(ParentForm!, "没有可导出的战绩数据");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"战绩_{_currentPlayer.GameName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                AddExtension = true,
                DefaultExt = "csv"
            };
            if (dialog.ShowDialog(ParentForm ?? FindForm()) != DialogResult.OK) return;

            using var export = _exportScope.Begin();
            btnExport.Enabled = false;
            try
            {
                lblStatus.Text = "正在导出战绩...";
                int exported = await ExportMatchHistoryAsync(dialog.FileName, export.Token);
                if (!export.IsCurrent) return;
                lblStatus.Text = $"已导出 {exported} 场战绩" + (_exportTruncated ? "（已达最近 5000 场上限，并非全部历史）" : "");
                LOL_GameAssistant.Helper.UiMessage.success(ParentForm!, "导出完成");
            }
            catch (Exception ex)
            {
                if (!export.IsCurrent) return;
                lblStatus.Text = "导出失败";
                LOL_GameAssistant.Helper.UiMessage.error(ParentForm!, $"导出失败: {ex.Message}");
            }
            finally
            {
                if (!IsDisposed) btnExport.Enabled = true;
            }
        }

        /// <summary>将战绩数据组织成导出文本并保存。</summary>
        private async Task<int> ExportMatchHistoryAsync(string path, CancellationToken token)
        {
            var player = _currentPlayer ?? throw new InvalidOperationException("没有可导出的玩家。");
            var sb = new StringBuilder();
            sb.AppendLine("大区,GameId,模式,日期,时长,英雄,结果,KDA,击杀,死亡,助攻,补刀,伤害,金币,视野,装备");
            MatchHistoryResponse? complete = await _matchHistoryService.GetAllAsync(player.Puuid, FilterLoadLimit, token);
            _exportTruncated = complete?.Games?.IsTruncated ?? false;
            var games = complete?.Games?.Games;
            if (games == null) throw new InvalidOperationException("无法读取完整战绩，未写入导出文件。");

            for (int offset = 0; offset < games.Count; offset += 50)
            {
                var batch = games.Skip(offset).Take(50).ToList();
                using var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
                var details = await Task.WhenAll(batch.Select(async head =>
                {
                    await semaphore.WaitAsync(token);
                    try { return await _matchHistoryService.GetDetailAsync(head.GameId, cancellationToken: token); }
                    finally { semaphore.Release(); }
                }));
                for (int i = 0; i < batch.Count; i++)
                {
                    var head = batch[i];
                    var detail = details[i];
                    if (detail == null || _currentPlayer == null)
                        throw new InvalidOperationException($"第 {offset + i + 1} 场详情不可用，未写入不完整的导出文件。");
                    var gamer = detail.GetParticipant(player.Puuid);
                    if (gamer?.stats == null)
                        throw new InvalidOperationException($"第 {offset + i + 1} 场缺少玩家统计，未写入不完整的导出文件。");

                    var s = gamer.stats;
                    int cs = s.totalMinionsKilled + s.neutralMinionsKilled;
                    string date = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "";
                    string champ = GetChampionDisplayName(gamer.championId);
                    int[] items = { s.item0, s.item1, s.item2, s.item3, s.item4, s.item5, s.item6 };
                    string itemsText = string.Join("|", items.Where(id => id > 0));

                    var fields = new[]
                    {
                    EscapeCsv(head.PlatformId),
                    head.GameId.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(detail.GetModeText()),
                    EscapeCsv(date),
                    detail.GetDurationText(),
                    EscapeCsv(champ),
                    gamer.IsWin() ? "胜利" : "失败",
                    gamer.GetKdaText(),
                    s.kills.ToString(CultureInfo.InvariantCulture),
                    s.deaths.ToString(CultureInfo.InvariantCulture),
                    s.assists.ToString(CultureInfo.InvariantCulture),
                    cs.ToString(CultureInfo.InvariantCulture),
                    s.totalDamageDealtToChampions.ToString(CultureInfo.InvariantCulture),
                    s.goldEarned.ToString(CultureInfo.InvariantCulture),
                    s.visionScore.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(itemsText)
                };
                    sb.AppendLine(string.Join(",", fields));
                }
                token.ThrowIfCancellationRequested();
                if (IsDisposed) return 0;
                lblStatus.Text = $"正在导出战绩... ({Math.Min(offset + batch.Count, games.Count)}/{games.Count})";
            }

            // 带 BOM 的 UTF-8，方便 Excel 直接打开中文
            await _textExportService.SaveUtf8WithBomAsync(path, sb.ToString(), token);
            return games.Count;
        }

        /// <summary>英雄显示名由应用目录提供，未知 ID 保留稳定的兜底文本。</summary>
        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        /// <summary>转义 CSV 字段中的分隔符、引号和换行。</summary>
        private static string EscapeCsv(string value)
        {
            // CSV 可能直接在 Excel 中打开；外部文本不能被解释成公式。
            if (!string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@') value = "'" + value;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>将排位段位标识转换为中文名称。</summary>
        private static string TierToChinese(string tier) => tier switch
        {
            "IRON" => "黑铁",
            "BRONZE" => "青铜",
            "SILVER" => "白银",
            "GOLD" => "黄金",
            "PLATINUM" => "铂金",
            "DIAMOND" => "钻石",
            "MASTER" => "超凡大师",
            "GRANDMASTER" => "宗师",
            "CHALLENGER" => "最强王者",
            _ => tier
        };
    }
}
