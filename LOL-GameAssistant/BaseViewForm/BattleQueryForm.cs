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
    public partial class BattleQueryForm : UserControl, IThemeAware
    {
        private PlayerProfile? _currentPlayer;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IProfileIconService _profileIconService;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IRankedStatsService _rankedStatsService;
        private readonly IFavoritePlayerStore _favoritePlayerStore;
        private readonly ITextExportService _textExportService;
        private MatchHistoryResponse? _matchHistory;
        private int _currentPage = 1;
        private int _pageSize = 10;
        private const int StatsLoadLimit = 100;
        private const int DetailLoadConcurrency = 6;
        private const int MatchListHorizontalInset = 20;

        private List<FavoritePlayer> _favorites;

        private List<RawGameStat>? _rawGameStats;
        private bool _statsLoaded;
        private bool _searchBusy;
        private bool _resizingMatchRows;
        private TableLayoutPanel? _rootLayout;

        private readonly SemaphoreSlim _pageLoadGate = new(1, 1);
        private readonly ToolTip _playerIdentityTip = new();
        private readonly Label _historyTitle = new();
        private readonly Label _historyHint = new();
        private readonly AntdUI.Panel _searchSurface = new() { Radius = 10, BorderWidth = 1, Padding = new Padding(12) };
        private readonly AntdUI.Panel _profileSurface = new() { Radius = 10, BorderWidth = 1, Padding = new Padding(8) };
        private readonly AntdUI.Segmented _viewSwitch = new() { Width = 250, Height = 37 };
        private readonly AntdUI.Select _favoriteSelect = new() { Width = 245, Height = 34, PlaceholderText = "收藏玩家" };
        private readonly AntdUI.Select _pageSizeSelect = new() { Width = 90, Height = 34 };
        private readonly AntdUI.Label _playerPlaceholder = new()
        {
            Dock = DockStyle.Fill, Text = "查询玩家后显示头像、等级与排位信息",
            TextAlign = ContentAlignment.MiddleCenter
        };
        private readonly FlowLayoutPanel _searchActions = new() { Dock = DockStyle.Top, Height = 42, WrapContents = false };
        private readonly FlowLayoutPanel _favoriteActions = new() { Dock = DockStyle.Bottom, Height = 42, WrapContents = true };
        private RankedQueue? solo, flex;

        private class RawGameStat
        {
            public string Mode = "";
            public double Kda;
            public bool Win;
            public int ChampionId;
        }

        public BattleQueryForm() : this(
            AppCompositionRoot.PlayerProfileService,
            AppCompositionRoot.ProfileIconService,
            AppCompositionRoot.MatchHistoryService,
            AppCompositionRoot.RankedStatsService,
            AppCompositionRoot.FavoritePlayerStore,
            AppCompositionRoot.TextExportService)
        {
        }

        /// <summary>战绩查询页只依赖应用服务，不直接解析 LCU JSON 或调用静态数据接口。</summary>
        internal BattleQueryForm(
            IPlayerProfileService playerProfileService,
            IProfileIconService profileIconService,
            IMatchHistoryService matchHistoryService,
            IRankedStatsService rankedStatsService,
            IFavoritePlayerStore favoritePlayerStore,
            ITextExportService textExportService)
        {
            _playerProfileService = playerProfileService;
            _profileIconService = profileIconService;
            _matchHistoryService = matchHistoryService;
            _rankedStatsService = rankedStatsService;
            _favoritePlayerStore = favoritePlayerStore;
            _textExportService = textExportService;
            _favorites = _favoritePlayerStore.Load();
            InitializeComponent();
            Resize += (_, _) => UpdateResponsiveLayout();
            stackMatches.SizeChanged += (_, _) => ResizeMatchRows();
            panelPlayer.SizeChanged += (_, _) => LayoutPlayerPanel();
            ConfigureCopyablePlayerIdentity();
            InitializeModernLayout();
        }

        private void InitializeModernLayout()
        {
            Controls.Clear();
            panelSearch.Controls.Clear();
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(15, 12, 15, 10) };
            _rootLayout = root;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));

            inpSearch.Width = 420;
            inpSearch.Height = 34;
            _searchActions.Controls.Add(inpSearch);
            _searchActions.Controls.Add(btnSearch);
            var myHistory = new AntdUI.Button { Text = "我的战绩", Size = new Size(92, 34), Margin = new Padding(6, 1, 0, 0) };
            myHistory.Click += async (_, _) =>
            {
                PlayerProfile? self = await _playerProfileService.GetCurrentAsync();
                if (!string.IsNullOrWhiteSpace(self?.Puuid)) await PerformSearchAsync(self.Puuid);
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
            _viewSwitch.SelectIndexChanged += (_, e) =>
            {
                if (e.Value == 0) BtnViewRecord_Click(null, EventArgs.Empty);
                else BtnViewStats_Click(null, EventArgs.Empty);
            };
            _viewSwitch.SelectIndex = 0;
            var viewBar = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 8, BorderWidth = 1, Margin = new Padding(0, 0, 0, 8) };
            _viewSwitch.Dock = DockStyle.Left;
            viewBar.Controls.Add(_viewSwitch);
            var pageSizeArea = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 145, WrapContents = false };
            pageSizeArea.Controls.Add(new AntdUI.Label { Text = "每页", Width = 42, Height = 34 });
            pageSizeArea.Controls.Add(_pageSizeSelect);
            viewBar.Controls.Add(pageSizeArea);

            panelContent.Dock = DockStyle.Fill;
            panelContent.Margin = Padding.Empty;
            lblStatus.Dock = DockStyle.Fill;
            root.Controls.Add(_searchSurface, 0, 0);
            root.Controls.Add(_profileSurface, 0, 1);
            root.Controls.Add(viewBar, 0, 2);
            root.Controls.Add(panelContent, 0, 3);
            root.Controls.Add(lblStatus, 0, 4);
            Controls.Add(root);

            var historyHeader = new AntdUI.Panel { Dock = DockStyle.Top, Height = 51, Radius = 0, Padding = new Padding(10, 5, 10, 0) };
            _historyTitle.Dock = DockStyle.Top;
            _historyTitle.Height = 23;
            _historyTitle.Text = "最近对局";
            _historyTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _historyHint.Dock = DockStyle.Fill;
            _historyHint.Text = "每局直接展示双方玩家、KDA 与开黑标记；海克斯强化显示中文名";
            historyHeader.Controls.Add(_historyHint);
            historyHeader.Controls.Add(_historyTitle);
            panelHistory.Controls.Add(historyHeader);
            historyHeader.SendToBack();
            stackMatches.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                Text = "输入 Riot ID（名称#TAG）或 PUUID 查询战绩",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = UiTheme.Palette.TextSecondary
            });
            ApplyTheme(UiTheme.Palette);
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceMuted;
            _searchSurface.BackColor = palette.SurfaceRaised;
            _profileSurface.BackColor = palette.SurfaceRaised;
            _playerPlaceholder.ForeColor = palette.TextSecondary;
            panelPlayer.BackColor = palette.SurfaceRaised;
            panelContent.BackColor = palette.SurfaceMuted;
            panelHistory.BackColor = palette.SurfaceMuted;
            panelStats.BackColor = palette.SurfaceMuted;
            stackMatches.BackColor = palette.SurfaceMuted;
            _historyTitle.ForeColor = palette.TextPrimary;
            _historyHint.ForeColor = palette.TextSecondary;
            lblStatus.ForeColor = palette.TextSecondary;
        }

        private void UpdateHistoryHeader(int totalGames = 0)
        {
            _historyTitle.Text = totalGames > 0 ? $"最近对局 · {totalGames} 场" : "最近对局";
            _historyHint.Text = totalGames > 0
                ? "双方玩家、KDA 与开黑标记直接显示；有禁用时展示禁用英雄，海克斯大乱斗展示强化。"
                : "输入 Riot ID（名称#TAG）或 PUUID 查询；每局直接展示双方玩家。";
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

        private void CopyPlayerIdentity(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            try
            {
                Clipboard.SetText(value);
                lblStatus.Text = $"已复制{label}：{value}";
                if (ParentForm != null)
                {
                    AntdUI.Message.success(ParentForm, $"已复制{label}");
                }
            }
            catch
            {
                lblStatus.Text = $"复制{label}失败";
                if (ParentForm != null)
                {
                    AntdUI.Message.error(ParentForm, $"复制{label}失败，请重试");
                }
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshFavoriteList(null);
            UpdateResponsiveLayout();
            _ = InitializeDefaultSearchAsync();
        }

        private void UpdateResponsiveLayout()
        {
            if (IsDisposed) return;
            _viewSwitch.Width = ClientSize.Width < 550 ? 195 : 250;
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
            bool narrow = width < 620;
            _searchActions.WrapContents = width < 430;
            _searchActions.Height = width < 430 ? 78 : 42;
            _favoriteActions.Height = narrow ? 76 : 42;
            if (_rootLayout != null)
                _rootLayout.RowStyles[0].Height = width < 430 ? 202 : narrow ? 166 : 128;
            int available = width - btnSearch.Width - 118;
            inpSearch.Width = Math.Clamp(available, 160, 620);
        }

        /// <summary>
        /// 玩家概览优先保留资料区域，剩余空间再分配给排位面板，防止窄窗口中两个区域重叠。
        /// </summary>
        private void LayoutPlayerPanel()
        {
            if (panelPlayer.ClientSize.Width <= 0) return;
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
            avatarPlayer.SetBounds(8, 8, 96, 102);
            if (width < 650)
            {
                if (_rootLayout != null) _rootLayout.RowStyles[1].Height = 254;
                panelPlayerInfo.SetBounds(112, 8, Math.Max(120, width - 120), 102);
                panelRanked.SetBounds(8, 116, Math.Max(200, width - 16), 108);
            }
            else
            {
                if (_rootLayout != null) _rootLayout.RowStyles[1].Height = 130;
                int rankedWidth = Math.Clamp(width - 112 - 245, 240, 472);
                panelPlayerInfo.SetBounds(112, 8, Math.Max(160, width - 120 - rankedWidth), 102);
                panelRanked.SetBounds(width - rankedWidth - 8, 8, rankedWidth, 102);
            }
            int identityWidth = panelPlayerInfo.ClientSize.Width;
            lblPlayerName.SetBounds(0, 0, identityWidth, 40);
            lblPlayerTag.SetBounds(0, 40, identityWidth, 28);
            lblPlayerLevel.SetBounds(0, 68, identityWidth, 28);
            int rankedTextWidth = panelRanked.ClientSize.Width;
            lblSoloTitle.SetBounds(0, 0, rankedTextWidth, 26);
            lblSoloStats.SetBounds(0, 26, rankedTextWidth, 24);
            lblFlexTitle.SetBounds(0, 50, rankedTextWidth, 26);
            lblFlexStats.SetBounds(0, 76, rankedTextWidth, 24);
        }

        private int GetMatchRowWidth(int contentHeight)
        {
            int scrollbarWidth = contentHeight > stackMatches.ClientSize.Height
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            return Math.Max(1, stackMatches.ClientSize.Width - MatchListHorizontalInset - scrollbarWidth);
        }

        /// <summary>
        /// 查询结果为手工定位控件；容器宽度改变后必须重新设置每行宽度和滚动范围。
        /// </summary>
        private void ResizeMatchRows()
        {
            if (_resizingMatchRows) return;
            var rows = stackMatches.Controls.OfType<MatchHistoryCard>().ToList();
            if (rows.Count == 0 || stackMatches.ClientSize.Width <= 0) return;
            _resizingMatchRows = true;
            try
            {
                int height = 8 + rows.Sum(row => row.Height + 8) + 6;
                int width = GetMatchRowWidth(height);
                foreach (MatchHistoryCard row in rows) row.Width = width;
                height = 8 + rows.Sum(row => row.Height + 8) + 6;
                width = GetMatchRowWidth(height);
                foreach (MatchHistoryCard row in rows) row.Width = width;
                int y = 8;
                foreach (MatchHistoryCard row in rows)
                {
                    row.Location = new Point(10, y);
                    y += row.Height + 8;
                }
                stackMatches.AutoScrollMinSize = new Size(0, y + 6);
            }
            finally { _resizingMatchRows = false; }
        }

        /// <summary>
        /// 默认在搜索框填入当前登录玩家 puuid。
        /// </summary>
        private async Task InitializeDefaultSearchAsync()
        {
            try
            {
                PlayerProfile? self = await _playerProfileService.GetCurrentAsync();
                if (!string.IsNullOrEmpty(self?.Puuid) && !IsDisposed)
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

        private async void BtnSearch_Click(object? sender, EventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// 执行搜索；可从其他界面传入 puuid / 名称#TAG 直接跳转查询。
        /// </summary>
        public async Task PerformSearchAsync(string? input = null)
        {
            if (_searchBusy) return;
            input = (input ?? inpSearch.Text).Trim();
            if (string.IsNullOrEmpty(input))
            {
                AntdUI.Message.warn(ParentForm!, "请输入 puuid 或 名称#TAG");
                return;
            }
            inpSearch.Text = input;

            _searchBusy = true;
            lblStatus.Text = "正在搜索...";
            btnSearch.Enabled = false;
            try
            {
                string? puuid = await ResolvePuuidAsync(input);
                if (string.IsNullOrEmpty(puuid))
                {
                    lblStatus.Text = "未找到该玩家";
                    AntdUI.Message.error(ParentForm!, "未找到该玩家");
                    return;
                }

                _statsLoaded = false;
                await LoadPlayerDataAsync(puuid);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"查询失败：{ex.Message}";
                RuntimeDiagnostics.Report("战绩查询", "查询失败", ex.Message);
            }
            finally
            {
                _searchBusy = false;
                btnSearch.Enabled = true;
            }
        }

        /// <summary>
        /// 从其他界面（首页战绩卡、对局详情等）跳转到战绩查询并搜索指定玩家。
        /// </summary>
        public static async Task QueryPlayerAsync(string? puuid)
        {
            if (string.IsNullOrEmpty(puuid)) return;
            var main = Program.GameMain;
            if (main == null) return;

            main.ShowBattleQueryPage();
            await GameMain.battleQueryForm.PerformSearchAsync(puuid);
        }

        /// <summary>
        /// 解析输入为 puuid：支持 名称#TAG、长字符串 puuid，最后回退到当前玩家历史战绩模糊匹配。
        /// </summary>
        private async Task<string?> ResolvePuuidAsync(string input)
        {
            if (input.Contains('#'))
            {
                var parts = input.Split('#', 2);
                string name = parts[0].Trim();
                string tag = parts[1].Trim();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(tag))
                {
                    PlayerProfile? player = await _playerProfileService.FindByRiotIdAsync(name, tag);
                    if (!string.IsNullOrEmpty(player?.Puuid)) return player.Puuid;
                }
            }
            else if (input.Length >= 30)
            {
                return input;
            }

            return await ScanMatchHistoryForPuuidAsync(input);
        }

        private async Task<string?> ScanMatchHistoryForPuuidAsync(string keyword)
        {
            try
            {
                PlayerProfile? self = await _playerProfileService.GetCurrentAsync();
                if (string.IsNullOrEmpty(self?.Puuid)) return null;

                var history = await _matchHistoryService.GetPageAsync(self.Puuid, 0, 199);
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
            catch
            {
                // 兜底失败不影响主流程
            }
            return null;
        }

        // ── 视图切换 ──

        private void BtnViewRecord_Click(object? sender, EventArgs e)
        {
            panelHistory.Visible = true;
            panelStats.Visible = false;
            SetViewButtonState(true);
        }

        private async void BtnViewStats_Click(object? sender, EventArgs e)
        {
            panelHistory.Visible = false;
            panelStats.Visible = true;
            SetViewButtonState(false);

            if (_currentPlayer != null && !_statsLoaded)
            {
                await LoadAndShowStatsAsync();
            }
            else if (_statsLoaded)
            {
                RebuildStatsCharts("全部");
            }
        }

        private void SetViewButtonState(bool recordSelected)
        {
            btnViewRecord.Type = recordSelected ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            btnViewRecord.Font = new Font("Microsoft YaHei UI", 9F, recordSelected ? FontStyle.Bold : FontStyle.Regular);
            btnViewStats.Type = recordSelected ? AntdUI.TTypeMini.Default : AntdUI.TTypeMini.Primary;
            btnViewStats.Font = new Font("Microsoft YaHei UI", 9F, recordSelected ? FontStyle.Regular : FontStyle.Bold);
        }

        // ── 加载玩家数据 ──

        private async Task LoadPlayerDataAsync(string puuid)
        {
            lblStatus.Text = "正在加载玩家数据...";

            _currentPlayer = await _playerProfileService.GetByPuuidAsync(puuid);
            if (_currentPlayer == null) { lblStatus.Text = "获取玩家信息失败，请确认客户端已启动并登录。"; return; }

            _matchHistoryService.ClearDetailCache();
            _statsLoaded = false;

            avatarPlayer.Visible = true;
            try
            {
                byte[]? iconBytes = await _profileIconService.GetProfileIconAsync(_currentPlayer.ProfileIconId);
                Image? profileImage = CopyToImage(iconBytes);
                if (profileImage != null) avatarPlayer.Image = profileImage;
            }
            catch { }

            lblPlayerName.Text = _currentPlayer.GameName;
            lblPlayerName.Visible = true;
            lblPlayerTag.Text = $"#{_currentPlayer.TagLine}";
            lblPlayerTag.Visible = true;
            lblPlayerLevel.Text = $"等级: {_currentPlayer.SummonerLevel}";
            lblPlayerLevel.Visible = true;
            panelPlayer.Visible = true;
            _playerPlaceholder.Visible = false;

            solo = null;
            flex = null;
            var rankedTask = LoadRankedDataAsync(puuid);
            var matchTask = LoadMatchHistoryAsync(puuid);
            await Task.WhenAll(rankedTask, matchTask);

            RefreshFavoriteState();
            lblStatus.Text = _currentPlayer.RiotId;
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

        private async Task LoadRankedDataAsync(string puuid)
        {
            try
            {
                var rankedData = await _rankedStatsService.GetAsync(puuid);
                if (rankedData == null) return;
                solo = rankedData.GetQueue(RankedQueues.Solo5x5);
                flex = rankedData.GetQueue(RankedQueues.Flex5x5);

                if (solo != null)
                {
                    lblSoloTitle.Visible = true;
                    lblSoloTitle.Text = "单双排";
                    lblSoloStats.Text = FormatRanked(solo);
                    lblSoloStats.Visible = true;
                }
                if (flex != null)
                {
                    lblFlexTitle.Visible = true;
                    lblFlexTitle.Text = "灵活组排";
                    lblFlexStats.Text = FormatRanked(flex);
                    lblFlexStats.Visible = true;
                }
            }
            catch { }
        }

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
                return $"{TierToChinese(entry.Tier)} {entry.Division} · {entry.LeaguePoints}LP · {entry.WinRate}% ({entry.Wins}W/{entry.Losses}L){extra}";
            if (entry.IsProvisional)
                return $"定位赛 ({entry.ProvisionalGamesRemaining}/{entry.ProvisionalGameThreshold})";
            return "暂无排位数据";
        }

        private async Task LoadMatchHistoryAsync(string puuid)
        {
            try
            {
                // 拉取该玩家全部对局（分页合并），而不是只取前若干场
                _matchHistory = await _matchHistoryService.GetAllAsync(puuid);
                if (_matchHistory?.Games?.Games == null || _matchHistory.Games.Games.Count == 0)
                {
                    lblStatus.Text = "暂无比赛记录";
                    UpdateHistoryHeader();
                    return;
                }

                _matchHistory.Games.Games = _matchHistory.Games.Games.OrderByDescending(g => g.GameCreation).ToList();

                pagination.Total = _matchHistory.Games.Games.Count;
                pagination.PageSize = _pageSize;
                pagination.Current = 1;
                pagination.Visible = true;
                _currentPage = 1;
                await RenderMatchPageAsync();
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"战绩加载失败：{ex.Message}";
                RuntimeDiagnostics.Report("战绩查询", "战绩加载失败", ex.Message);
            }
        }

        private async Task RenderMatchPageAsync()
        {
            if (_matchHistory?.Games?.Games == null || _currentPlayer == null) return;

            await _pageLoadGate.WaitAsync();
            try
            {
                ClearMatchControls();
                // 加载中先显示微光占位，避免空白闪烁
                var shimmer = new ShimmerPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(246, 248, 251)
                };
                stackMatches.Controls.Add(shimmer);

                int start = (_currentPage - 1) * _pageSize;
                int end = Math.Min(start + _pageSize, _matchHistory.Games.Games.Count);
                if (start >= end) return;
                var pageGames = _matchHistory.Games.Games.GetRange(start, end - start);

                // 并行加载本页详情；缓存策略由战绩应用服务负责。
                var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
                var tasks = pageGames.Select(async head =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var detail = await _matchHistoryService.GetDetailAsync(head.GameId);
                        if (detail == null) return null;
                        var gamer = detail.GetParticipant(_currentPlayer.Puuid);
                        if (gamer == null) return null;

                        var rec = new MatchHistoryCard(detail, _currentPlayer.Puuid, AppCompositionRoot.GameAssetService)
                        {
                            Width = Math.Max(1, stackMatches.ClientSize.Width - MatchListHorizontalInset),
                            Margin = new Padding(0, 0, 0, 8)
                        };
                        return rec;
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var records = await Task.WhenAll(tasks);
                if (IsDisposed) return;

                ClearMatchControls(); // 移除微光
                int y = 8;
                int index = 0;
                foreach (var rec in records)
                {
                    if (rec == null) continue;
                    rec.Location = new Point(10, y);
                    stackMatches.Controls.Add(rec);
                    UiTheme.Apply(rec);
                    rec.SizeChanged += (_, _) => ResizeMatchRows();
                    y += rec.Height + 8;
                    index++;
                }
                stackMatches.AutoScrollMinSize = new Size(0, y + 6);
                ResizeMatchRows();

                int totalPages = Math.Max(1, (int)Math.Ceiling((double)_matchHistory.Games.Games.Count / _pageSize));
                lblStatus.Text = $"共 {_matchHistory.Games.Games.Count} 场 · 第 {_currentPage}/{totalPages} 页";
                UpdateHistoryHeader(_matchHistory.Games.Games.Count);
            }
            finally
            {
                _pageLoadGate.Release();
            }
        }

        private void ClearMatchControls()
        {
            foreach (Control control in stackMatches.Controls.Cast<Control>().ToArray())
            {
                stackMatches.Controls.Remove(control);
                control.Dispose();
            }
        }

        private void Pagination_ValueChanged(object? sender, AntdUI.PagePageEventArgs e)
        {
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
                if (_matchHistory != null && !_statsLoaded)
                {
                    _currentPage = 1;
                    pagination.PageSize = _pageSize;
                    pagination.Current = 1;
                    _ = RenderMatchPageAsync();
                }
            }
        }

        // ── 统计看板 ──

        private async Task LoadAndShowStatsAsync()
        {
            if (_matchHistory?.Games?.Games == null || _matchHistory.Games.Games.Count == 0)
            {
                panelStats.Controls.Clear();
                panelStats.Controls.Add(new Label()
                {
                    Text = "无比赛数据可供统计",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Microsoft YaHei UI", 12F)
                });
                return;
            }

            panelStats.Controls.Clear();
            lblStatus.Text = "正在计算统计数据...";

            var recentGames = _matchHistory.Games.Games.Take(StatsLoadLimit).ToList();
            _rawGameStats = new List<RawGameStat>();
            int totalWins = 0, totalLosses = 0;
            var championStats = new Dictionary<int, (int games, int wins)>();
            var kdaTrend = new List<(double kda, bool win)>();

            // 并行拉取对局详情，避免逐场串行等待
            var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
            var detailTasks = recentGames.Select(async head =>
            {
                await semaphore.WaitAsync();
                try { return await _matchHistoryService.GetDetailAsync(head.GameId); }
                finally { semaphore.Release(); }
            }).ToList();
            var details = await Task.WhenAll(detailTasks);

            int totalGames = 0;
            for (int i = 0; i < recentGames.Count; i++)
            {
                var detail = details[i];
                if (detail == null || _currentPlayer == null) continue;
                var gamer = detail.GetParticipant(_currentPlayer.Puuid);
                if (gamer?.stats == null) continue;

                totalGames++;
                bool win = gamer.IsWin();
                if (win) totalWins++; else totalLosses++;

                double gameKda = gamer.GetKdaRatio();
                kdaTrend.Add((gameKda, win));
                string mode = detail.GetModeText();
                _rawGameStats.Add(new RawGameStat { Mode = mode, Kda = gameKda, Win = win, ChampionId = gamer.championId });

                var cur = championStats.GetValueOrDefault(gamer.championId);
                championStats[gamer.championId] = (cur.games + 1, cur.wins + (win ? 1 : 0));

                if (i % 20 == 0 || i == recentGames.Count - 1)
                    lblStatus.Text = $"正在计算统计数据... ({i + 1}/{recentGames.Count})";
            }

            _statsLoaded = true;
            RebuildStatsCharts("全部");
        }

        /// <summary>
        /// 根据模式筛选重建统计图表。
        /// </summary>
        private void RebuildStatsCharts(string filter)
        {
            if (_rawGameStats == null || _rawGameStats.Count == 0) return;

            var filtered = string.IsNullOrEmpty(filter) || filter == "全部"
                ? _rawGameStats
                : _rawGameStats.Where(s => s.Mode == filter).ToList();

            if (filtered.Count == 0)
            {
                panelStats.Controls.Clear();
                panelStats.Controls.Add(new Label { Text = "该模式暂无数据", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                return;
            }

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
            modeSelect.Items.Add("全部");
            foreach (string mode in _rawGameStats.Select(s => s.Mode).Distinct().OrderBy(mode => mode))
                modeSelect.Items.Add(mode);
            modeSelect.SelectedValue = filter;
            modeSelect.SelectedIndexChanged += (_, _) =>
                RebuildStatsCharts(modeSelect.SelectedValue?.ToString() ?? "全部");
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
            lblStatus.Text = "筛选: " + filter + "  " + filtered.Count + " 场 · 胜率 " + winRate + "%";
        }

        private static AntdUI.Panel StatTile(string title, string value, ThemePalette palette)
        {
            var card = new AntdUI.Panel
            {
                Width = 180, Height = 78, Radius = 8, BorderWidth = 1,
                Margin = new Padding(0, 0, 9, 0), BackColor = palette.SurfaceRaised
            };
            card.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Top, Height = 37, Text = value,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = palette.TextPrimary, Padding = new Padding(10, 4, 0, 0)
            });
            card.Controls.Add(new AntdUI.Label
            {
                Dock = DockStyle.Top, Height = 25, Text = title,
                ForeColor = palette.TextSecondary, Padding = new Padding(10, 7, 0, 0)
            });
            return card;
        }

        // ── 收藏玩家（新增功能） ──

        private void BtnFavorite_Click(object? sender, EventArgs e)
        {
            if (_currentPlayer == null || string.IsNullOrEmpty(_currentPlayer.Puuid))
            {
                AntdUI.Message.warn(ParentForm!, "请先查询一名玩家再收藏");
                return;
            }

            var existing = _favorites.FirstOrDefault(f => f.Puuid == _currentPlayer.Puuid);
            if (existing != null)
            {
                _favorites.Remove(existing);
                AntdUI.Message.success(ParentForm!, $"已取消收藏 {_currentPlayer.GameName}");
            }
            else
            {
                _favorites.Add(new FavoritePlayer
                {
                    Puuid = _currentPlayer.Puuid,
                    GameName = _currentPlayer.GameName,
                    TagLine = _currentPlayer.TagLine,
                    SummonerLevel = _currentPlayer.SummonerLevel.ToString(),
                    AddedAt = DateTime.Now
                });
                AntdUI.Message.success(ParentForm!, $"已收藏 {_currentPlayer.GameName}");
            }

            _favoritePlayerStore.Save(_favorites);
            RefreshFavoriteState();
        }

        private async void BtnLoadFavorite_Click(object? sender, EventArgs e)
        {
            string input;
            if (cboFavorites.SelectedIndex >= 0 && cboFavorites.SelectedIndex < _favorites.Count)
            {
                input = _favorites[cboFavorites.SelectedIndex].Puuid;
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
                AntdUI.Message.warn(ParentForm!, "请选择收藏玩家或输入 Riot ID");
                return;
            }

            inpSearch.Text = input;
            await PerformSearchAsync(input);
        }

        private void RefreshFavoriteList(string? selectedPuuid)
        {
            cboFavorites.Items.Clear();
            _favoriteSelect.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < _favorites.Count; i++)
            {
                var fav = _favorites[i];
                cboFavorites.Items.Add($"{fav.GameName}#{fav.TagLine} ({fav.SummonerLevel})");
                _favoriteSelect.Items.Add($"{fav.GameName}#{fav.TagLine} ({fav.SummonerLevel})");
                if (fav.Puuid == selectedPuuid) selectedIndex = i;
            }
            cboFavorites.SelectedIndex = selectedIndex >= 0 ? selectedIndex : -1;
            _favoriteSelect.SelectedIndex = selectedIndex >= 0 ? selectedIndex : -1;
            btnLoadFavorite.Enabled = true;
        }

        private void RefreshFavoriteState()
        {
            bool isFavorite = _currentPlayer != null && _favorites.Any(f => f.Puuid == _currentPlayer.Puuid);
            btnFavorite.Text = isFavorite ? "★ 已收藏" : "☆ 收藏";
            RefreshFavoriteList(_currentPlayer?.Puuid);
        }

        // ── 导出战绩 CSV（新增功能） ──

        private async void BtnExport_Click(object? sender, EventArgs e)
        {
            if (_currentPlayer == null || _matchHistory?.Games?.Games == null || _matchHistory.Games.Games.Count == 0)
            {
                AntdUI.Message.warn(ParentForm!, "没有可导出的战绩数据");
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

            btnExport.Enabled = false;
            try
            {
                lblStatus.Text = "正在导出战绩...";
                await ExportMatchHistoryAsync(dialog.FileName);
                lblStatus.Text = $"已导出 {_matchHistory.Games.Games.Count} 场战绩";
                AntdUI.Message.success(ParentForm!, "导出完成");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "导出失败";
                AntdUI.Message.error(ParentForm!, $"导出失败: {ex.Message}");
            }
            finally
            {
                btnExport.Enabled = true;
            }
        }

        private async Task ExportMatchHistoryAsync(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("GameId,模式,日期,时长,英雄,结果,KDA,击杀,死亡,助攻,补刀,伤害,金币,视野,装备");
            var games = _matchHistory!.Games!.Games;
            if (games == null) return;

            var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
            var tasks = games.Select(async head =>
            {
                await semaphore.WaitAsync();
                try { return await _matchHistoryService.GetDetailAsync(head.GameId); }
                finally { semaphore.Release(); }
            }).ToList();
            var details = await Task.WhenAll(tasks);

            for (int i = 0; i < games.Count; i++)
            {
                var head = games[i];
                var detail = details[i];
                if (detail == null || _currentPlayer == null) continue;
                var gamer = detail.GetParticipant(_currentPlayer.Puuid);
                if (gamer?.stats == null) continue;

                var s = gamer.stats;
                int cs = s.totalMinionsKilled + s.neutralMinionsKilled;
                string date = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "";
                string champ = GetChampionDisplayName(gamer.championId);
                int[] items = { s.item0, s.item1, s.item2, s.item3, s.item4, s.item5, s.item6 };
                string itemsText = string.Join("|", items.Where(id => id > 0));

                var fields = new[]
                {
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

            // 带 BOM 的 UTF-8，方便 Excel 直接打开中文
            await _textExportService.SaveUtf8WithBomAsync(path, sb.ToString());
        }

        /// <summary>英雄显示名由应用目录提供，未知 ID 保留稳定的兜底文本。</summary>
        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        private static string EscapeCsv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

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
