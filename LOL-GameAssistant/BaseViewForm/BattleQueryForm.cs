using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;
using static LOL_GameAssistant.Entity.LolRankedDataParser;
using static LOL_GameAssistant.Entity.PlayerModel;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class BattleQueryForm : UserControl
    {
        private Plyaer? _currentPlayer;
        private GameHeadModel.MatchHistoryResponse? _matchHistory;
        private int _currentPage = 1;
        private int _pageSize = 10;
        private const int StatsLoadLimit = 100;
        private const int DetailLoadConcurrency = 6;

        private List<FavoritePlayer> _favorites = FavoriteStore.Load();

        private List<RawGameStat>? _rawGameStats;
        private bool _statsLoaded;
        private bool _searchBusy;

        private readonly SemaphoreSlim _pageLoadGate = new(1, 1);
        private RankedEntry? solo, flex;

        private class RawGameStat
        {
            public string Mode = "";
            public double Kda;
            public bool Win;
            public int ChampionId;
        }

        public BattleQueryForm()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshFavoriteList(null);
            _ = InitializeDefaultSearchAsync();
        }

        /// <summary>
        /// 默认在搜索框填入当前登录玩家 puuid。
        /// </summary>
        private async Task InitializeDefaultSearchAsync()
        {
            try
            {
                string me = await Assets_api.GetUser();
                if (string.IsNullOrEmpty(me)) return;
                var self = JsonConvert.DeserializeObject<Plyaer>(me);
                if (!string.IsNullOrEmpty(self?.puuid) && !IsDisposed)
                {
                    inpSearch.Text = self.puuid;
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

            if (!EnsureLcuConnection())
            {
                AntdUI.Message.error(ParentForm!, "未检测到 LOL 客户端");
                return;
            }

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
                    string json = await Assets_api.SearchSummonerByRiotId(name, tag);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var player = JsonConvert.DeserializeObject<Plyaer>(json);
                        if (!string.IsNullOrEmpty(player?.puuid)) return player.puuid;
                    }
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
                string me = await Assets_api.GetUser();
                if (string.IsNullOrEmpty(me)) return null;
                var self = JsonConvert.DeserializeObject<Plyaer>(me);
                if (string.IsNullOrEmpty(self?.puuid)) return null;

                var history = await Game_Api.GetUserGame(self.puuid, "0", "199");
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

        // ── 连接 ──

        private bool EnsureLcuConnection()
        {
            if (string.IsNullOrEmpty(HttpClentHelper.Port) || string.IsNullOrEmpty(HttpClentHelper.Token))
            {
                (string? port, string? token) = GetlolLcu.GetAuth();
                if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
                    return false;
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
            }
            return true;
        }

        // ── 加载玩家数据 ──

        private async Task LoadPlayerDataAsync(string puuid)
        {
            lblStatus.Text = "正在加载玩家数据...";

            string json = await Assets_api.GetUser(puuid);
            if (string.IsNullOrEmpty(json)) { lblStatus.Text = "获取玩家信息失败"; return; }

            _currentPlayer = JsonConvert.DeserializeObject<Plyaer>(json);
            if (_currentPlayer == null) { lblStatus.Text = "解析玩家信息失败"; return; }

            Game_Api.ClearGameDetailCache();
            _statsLoaded = false;

            avatarPlayer.Visible = true;
            try
            {
                var s = await Assets_api.GetImg(_currentPlayer.profileIconId);
                if (s != null && s != Stream.Null) avatarPlayer.Image = await CopyToImageAsync(s);
            }
            catch { }

            lblPlayerName.Text = _currentPlayer.gameName ?? "未知";
            lblPlayerName.Visible = true;
            lblPlayerTag.Text = $"#{_currentPlayer.tagLine}";
            lblPlayerTag.Visible = true;
            lblPlayerLevel.Text = $"等级: {_currentPlayer.summonerLevel}";
            lblPlayerLevel.Visible = true;
            panelPlayer.Visible = true;

            solo = null;
            flex = null;
            var rankedTask = LoadRankedDataAsync(puuid);
            var matchTask = LoadMatchHistoryAsync(puuid);
            await Task.WhenAll(rankedTask, matchTask);

            RefreshFavoriteState();
            lblStatus.Text = $"{_currentPlayer.gameName}#{_currentPlayer.tagLine}";
        }

        private static async Task<Image?> CopyToImageAsync(Stream stream)
        {
            try
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
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
                var rankedData = await Game_Api.GetRankedStatsAsync(puuid);
                if (rankedData == null) return;
                var parser = new LolRankedDataParser();
                solo = parser.GetQueueData(rankedData, QueueTypes.RANKED_SOLO_5x5);
                flex = parser.GetQueueData(rankedData, QueueTypes.RANKED_FLEX_SR);

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

        private static string FormatRanked(RankedEntry entry)
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
                _matchHistory = await Game_Api.GetAllUserGamesAsync(puuid);
                if (_matchHistory?.Games?.Games == null || _matchHistory.Games.Games.Count == 0)
                {
                    lblStatus.Text = "暂无比赛记录";
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
            catch { }
        }

        private async Task RenderMatchPageAsync()
        {
            if (_matchHistory?.Games?.Games == null || _currentPlayer == null) return;

            await _pageLoadGate.WaitAsync();
            try
            {
                stackMatches.Controls.Clear();
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

                // 并行加载本页详情（复用 Game_Api 内存缓存）
                var semaphore = new SemaphoreSlim(DetailLoadConcurrency, DetailLoadConcurrency);
                var tasks = pageGames.Select(async head =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var detail = await Game_Api.GetGameDetail(head.GameId);
                        if (detail == null) return null;
                        var gamer = detail.GetParticipant(_currentPlayer.puuid);
                        if (gamer == null) return null;

                        // 紧凑战绩行：胜负配色 + 圆角 + 悬停动效
                        var rec = new RecentMatchRow
                        {
                            Width = Math.Max(700, stackMatches.ClientSize.Width - 30),
                            Height = RecentMatchRow.RowHeight
                        };
                        await rec.SetDataAsync(detail, gamer, _currentPlayer.puuid);
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

                stackMatches.Controls.Clear(); // 移除微光
                int y = 8;
                int index = 0;
                foreach (var rec in records)
                {
                    if (rec == null) continue;
                    rec.Location = new Point(10, y);
                    stackMatches.Controls.Add(rec);
                    // 逐行错峰入场，列表更灵动
                    UiAnimation.SlideIn(rec, -16, 220, index * 30);
                    y += rec.Height + 8;
                    index++;
                }
                stackMatches.AutoScrollMinSize = new Size(stackMatches.ClientSize.Width, y + 6);

                int totalPages = Math.Max(1, (int)Math.Ceiling((double)_matchHistory.Games.Games.Count / _pageSize));
                lblStatus.Text = $"共 {_matchHistory.Games.Games.Count} 场 · 第 {_currentPage}/{totalPages} 页";
            }
            finally
            {
                _pageLoadGate.Release();
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
                try { return await Game_Api.GetGameDetail(head.GameId); }
                finally { semaphore.Release(); }
            }).ToList();
            var details = await Task.WhenAll(detailTasks);

            int totalGames = 0;
            for (int i = 0; i < recentGames.Count; i++)
            {
                var detail = details[i];
                if (detail == null || _currentPlayer == null) continue;
                var gamer = detail.GetParticipant(_currentPlayer.puuid);
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

            panelStats.Controls.Clear();

            // 筛选按钮栏
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5), BackColor = Color.WhiteSmoke };
            var allModes = _rawGameStats.Select(s => s.Mode).Distinct().OrderBy(m => m).ToList();
            var filterTexts = new List<string> { "全部" };
            filterTexts.AddRange(allModes);
            foreach (var modeText in filterTexts)
            {
                var btn = new Button
                {
                    Text = modeText,
                    AutoSize = true,
                    Height = 26,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = modeText == filter ? Color.DodgerBlue : SystemColors.Control,
                    ForeColor = modeText == filter ? Color.White : SystemColors.ControlText,
                    Margin = new Padding(2)
                };
                var capturedMode = modeText;
                btn.Click += (_, _) =>
                {
                    foreach (Control c in btnPanel.Controls)
                    {
                        if (c is Button b)
                        {
                            b.BackColor = b.Text == capturedMode ? Color.DodgerBlue : SystemColors.Control;
                            b.ForeColor = b.Text == capturedMode ? Color.White : SystemColors.ControlText;
                        }
                    }
                    RebuildStatsCharts(capturedMode);
                };
                btnPanel.Controls.Add(btn);
            }
            panelStats.Controls.Add(btnPanel);

            var chartContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            if (kdaList.Count > 1)
            {
                var trendData = kdaList.TakeLast(30).ToList();
                var kdaPanel = new Panel { Dock = DockStyle.Top, Height = 210 };
                kdaPanel.Paint += (s, e) => ChartDrawer.DrawKdaTrend(e.Graphics, kdaPanel.ClientRectangle, trendData, "KDA 趋势（近 N 场 · 绿=胜 红=负）");
                chartContainer.Controls.Add(kdaPanel);
            }

            if (champStats.Count > 0)
            {
                var champData = champStats
                    .OrderByDescending(x => x.Value.g).Take(10)
                    .Select(x => (ChampionMap.GetChampion(x.Key)?.RealName ?? ("英雄" + x.Key.ToString()), x.Value.g, Math.Round((double)x.Value.w / x.Value.g * 100, 1)))
                    .ToList();
                var champPanel = new Panel { Dock = DockStyle.Top, Height = Math.Max(80, champData.Count * 25 + 30) };
                champPanel.Paint += (s, e) => ChartDrawer.DrawChampionBars(e.Graphics, champPanel.ClientRectangle, champData, "常用英雄 Top 10");
                chartContainer.Controls.Add(champPanel);
            }

            var sb = new StringBuilder();
            sb.Append("筛选: ").Append(filter).Append("  |  场次: ").Append(filtered.Count);
            sb.Append("  |  胜场: ").Append(totalWins).Append("  |  负场: ").Append(totalLosses).Append("  |  胜率: ").Append(winRate).Append("%");
            sb.Append("  |  平均KDA: ").Append(Math.Round(overallKda, 2));
            var summaryLabel = new Label
            {
                Text = sb.ToString(),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(10, 0, 0, 0)
            };
            chartContainer.Controls.Add(summaryLabel);

            panelStats.Controls.Add(chartContainer);
            lblStatus.Text = "筛选: " + filter + "  " + filtered.Count + " 场 · 胜率 " + winRate + "%";
        }

        // ── 收藏玩家（新增功能） ──

        private void BtnFavorite_Click(object? sender, EventArgs e)
        {
            if (_currentPlayer == null || string.IsNullOrEmpty(_currentPlayer.puuid))
            {
                AntdUI.Message.warn(ParentForm!, "请先查询一名玩家再收藏");
                return;
            }

            var existing = _favorites.FirstOrDefault(f => f.Puuid == _currentPlayer.puuid);
            if (existing != null)
            {
                _favorites.Remove(existing);
                AntdUI.Message.success(ParentForm!, $"已取消收藏 {_currentPlayer.gameName}");
            }
            else
            {
                _favorites.Add(new FavoritePlayer
                {
                    Puuid = _currentPlayer.puuid,
                    GameName = _currentPlayer.gameName ?? "",
                    TagLine = _currentPlayer.tagLine ?? "",
                    SummonerLevel = _currentPlayer.summonerLevel,
                    AddedAt = DateTime.Now
                });
                AntdUI.Message.success(ParentForm!, $"已收藏 {_currentPlayer.gameName}");
            }

            FavoriteStore.Save(_favorites);
            RefreshFavoriteState();
        }

        private void BtnLoadFavorite_Click(object? sender, EventArgs e)
        {
            if (cboFavorites.SelectedIndex < 0 || cboFavorites.SelectedIndex >= _favorites.Count) return;
            var fav = _favorites[cboFavorites.SelectedIndex];
            inpSearch.Text = fav.Puuid;
            _ = PerformSearchAsync();
        }

        private void RefreshFavoriteList(string? selectedPuuid)
        {
            cboFavorites.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < _favorites.Count; i++)
            {
                var fav = _favorites[i];
                cboFavorites.Items.Add($"{fav.GameName}#{fav.TagLine} ({fav.SummonerLevel})");
                if (fav.Puuid == selectedPuuid) selectedIndex = i;
            }
            cboFavorites.SelectedIndex = selectedIndex >= 0 ? selectedIndex : -1;
        }

        private void RefreshFavoriteState()
        {
            bool isFavorite = _currentPlayer != null && _favorites.Any(f => f.Puuid == _currentPlayer.puuid);
            btnFavorite.Text = isFavorite ? "★ 已收藏" : "☆ 收藏";
            RefreshFavoriteList(_currentPlayer?.puuid);
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
                FileName = $"战绩_{_currentPlayer.gameName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
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
                try { return await Game_Api.GetGameDetail(head.GameId); }
                finally { semaphore.Release(); }
            }).ToList();
            var details = await Task.WhenAll(tasks);

            for (int i = 0; i < games.Count; i++)
            {
                var head = games[i];
                var detail = details[i];
                if (detail == null || _currentPlayer == null) continue;
                var gamer = detail.GetParticipant(_currentPlayer.puuid);
                if (gamer?.stats == null) continue;

                var s = gamer.stats;
                int cs = s.totalMinionsKilled + s.neutralMinionsKilled;
                string date = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "";
                string champ = ChampionMap.GetChampion(gamer.championId)?.RealName ?? $"英雄{gamer.championId}";
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
            await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string EscapeCsv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string TierToChinese(string tier) => tier switch
        {
            "IRON" => "黑铁", "BRONZE" => "青铜", "SILVER" => "白银", "GOLD" => "黄金",
            "PLATINUM" => "铂金", "DIAMOND" => "钻石", "MASTER" => "超凡大师",
            "GRANDMASTER" => "宗师", "CHALLENGER" => "最强王者", _ => tier
        };
    }
}
