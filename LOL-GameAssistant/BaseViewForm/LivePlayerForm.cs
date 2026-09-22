using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Helper;
using System.Collections.Concurrent;
using System.Drawing.Drawing2D;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局玩家卡片：圆角描边 + 悬停发光，玩家信息 + 当前英雄/位置 + 近 10 场战绩（英雄头像、滑入动效）。
    /// </summary>
    public partial class LivePlayerForm : UserControl, IThemeAware
    {
        private readonly string? _playerPuuid;
        private readonly int _championId;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IProfileIconService _profileIconService;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IGameAssetService _gameAssetService;
        private readonly string _position;
        private readonly bool _isBot;
        private readonly bool _isAlly;
        private readonly bool _teamKnown;
        private readonly int _currentQueueId;
        private readonly string _currentGameMode;
        private readonly Color _teamColor;
        private const int RecentGamesCount = 10;

        /// <summary>评分取数范围：先拉 100 场摘要，再按同队列筛出评分样本。</summary>
        private const int HistoryFetchCount = 100;

        /// <summary>对局页/选人公告的评分样本下限，比其它页面的默认样本更稳。</summary>
        public const int PerformanceSampleSize = 20;
        private static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<PlayerProfile?> Value)> PlayerProfileCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<MatchHistoryResponse?> Value)> RecentHistoryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<long, (DateTime CachedAt, Task<MatchDetail?> Value)> MatchDetailCache = new();
        private static readonly SemaphoreSlim GlobalMatchDetailLoadGate = new(8, 8);
        private Image? _ownedProfileImage;
        private ToolTip? _premadeTip;
        private ToolTip? _copyTip;
        private readonly ToolTip _performanceTip = new();
        private readonly bool _showCopyButton;
        private bool _recentPerformancePublished;

        /// <summary>当前卡片对应玩家的 puuid（供开黑检测结果回填）。</summary>
        public string? Puuid => _playerPuuid;

        /// <summary>近期同队列 KDA 已完成计算；选人页据此汇总十名玩家并发送一次聊天公告。</summary>
        public event EventHandler<PlayerRecentPerformanceEventArgs>? RecentPerformanceReady;

        /// <summary>开黑小组配色（按组号轮换）。</summary>
        private static readonly Color[] PremadeColors =
        {
            Color.FromArgb(230, 126, 34),
            Color.FromArgb(142, 68, 173),
            Color.FromArgb(22, 160, 133),
            Color.FromArgb(231, 76, 60),
            Color.FromArgb(52, 152, 219)
        };

        private readonly System.Windows.Forms.Timer _glowTimer;
        private bool _glowTarget;
        private double _glowT;

        public LivePlayerForm(
            string? playerPuuid,
            string? fallbackName = null,
            int championId = 0,
            string? position = null,
            bool isBot = false,
            bool isAlly = false,
            bool teamKnown = false,
            int currentQueueId = 0,
            string? currentGameMode = null)
            : this(
                playerPuuid,
                fallbackName,
                championId,
                position,
                isBot,
                isAlly,
                teamKnown,
                currentQueueId,
                currentGameMode,
                AppCompositionRoot.PlayerProfileService,
                AppCompositionRoot.ProfileIconService,
                AppCompositionRoot.MatchHistoryService,
                AppCompositionRoot.GameAssetService)
        {
        }

        /// <summary>实时玩家卡片通过应用服务查询资料、战绩与只读资源。</summary>
        internal LivePlayerForm(
            string? playerPuuid,
            string? fallbackName,
            int championId,
            string? position,
            bool isBot,
            bool isAlly,
            bool teamKnown,
            int currentQueueId,
            string? currentGameMode,
            IPlayerProfileService playerProfileService,
            IProfileIconService profileIconService,
            IMatchHistoryService matchHistoryService,
            IGameAssetService gameAssetService)
        {
            InitializeComponent();
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _playerPuuid = playerPuuid;
            _championId = championId;
            _playerProfileService = playerProfileService;
            _profileIconService = profileIconService;
            _matchHistoryService = matchHistoryService;
            _gameAssetService = gameAssetService;
            _position = position ?? "";
            _isBot = isBot;
            _isAlly = isAlly;
            _teamKnown = teamKnown;
            _currentQueueId = currentQueueId;
            _currentGameMode = currentGameMode ?? "";
            _teamColor = !teamKnown || isAlly
                ? Color.FromArgb(30, 136, 229)
                : Color.FromArgb(211, 47, 47);
            BackColor = Color.FromArgb(252, 252, 253);

            lblName.Text = string.IsNullOrEmpty(fallbackName) ? "未知玩家" : fallbackName!;
            lblSub.Text = _isBot ? "机器人" : "";

            // AntdUI 控件的 Visible setter 会立刻 CreateControl()：构造期卡片还没有父窗口，
            // 句柄会先挂在临时 parking window 上，挂到队伍面板后还要再建一次。
            // 既白白多耗一份窗口句柄，也是"创建窗口句柄时出错"的现场。
            // 因此构造期只设成安全默认值（设 false 不会建句柄），真实状态等句柄建立后再应用。
            btnCopy.Visible = false;
            _showCopyButton = !_isBot && !string.IsNullOrEmpty(_playerPuuid);

            // 队友/对手标识：同队显示“队友”（蓝色），异队显示“对手”（红色）
            lblTeamTag.Text = isAlly ? "队友" : "对手";
            lblTeamTag.ForeColor = _teamColor;
            lblTeamTag.BackColor = Color.FromArgb(
                isAlly ? 226 : 253,
                isAlly ? 240 : 236,
                isAlly ? 253 : 236);
            if (teamKnown)
            {
                headerPanel.BackColor = Color.FromArgb(
                    isAlly ? 235 : 253,
                    isAlly ? 244 : 240,
                    isAlly ? 252 : 240);
            }

            // 复制按钮悬停提示：显示可复制的完整 ID
            _copyTip = new ToolTip();
            _copyTip.SetToolTip(btnCopy, "复制该玩家 PUUID（可用于精确查询）");
            if (!string.IsNullOrEmpty(_playerPuuid))
            {
                _copyTip.SetToolTip(this, $"PUUID: {_playerPuuid}");
            }

            _glowTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _glowTimer.Tick += (_, _) => GlowTick();
            Disposed += (_, _) =>
            {
                _glowTimer.Dispose();
                _ownedProfileImage?.Dispose();
                _copyTip?.Dispose();
                _premadeTip?.Dispose();
                _performanceTip.Dispose();
            };

            this.MouseEnter += (_, _) => StartGlow(true);
            this.MouseLeave += (_, _) => StartGlow(false);
            foreach (Control child in Controls)
            {
                child.MouseEnter += (_, _) => StartGlow(true);
                child.MouseLeave += (_, _) => StartGlow(false);
            }

            this.Resize += (_, _) => RecalcHeaderLayout();
            panelMatches.SizeChanged += (_, _) => ResizeMatchRows();
            RecalcHeaderLayout();
            this.Load += async (_, _) =>
            {
                ApplyDeferredVisibility();
                await LoadAsync();
            };
            UiTheme.Apply(this);
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceRaised;
            headerPanel.BackColor = palette.SurfaceMuted;
            panelMatches.BackColor = palette.Surface;
            lblName.ForeColor = palette.TextPrimary;
            lblSub.ForeColor = palette.TextSecondary;
            lblChampionNow.ForeColor = palette.Accent;
            if (!string.IsNullOrWhiteSpace(lblSummary.Text))
            {
                lblSummary.ForeColor = lblSummary.Text.StartsWith("人机", StringComparison.Ordinal)
                    ? Color.FromArgb(156, 39, 176)
                    : lblSummary.Text.StartsWith("下等马", StringComparison.Ordinal)
                    ? Color.FromArgb(239, 83, 80)
                    : lblSummary.Text.StartsWith("上等马", StringComparison.Ordinal)
                        ? Color.FromArgb(102, 187, 106)
                        : palette.TextSecondary;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDeferredVisibility();
        }

        /// <summary>
        /// 卡片挂到队伍面板、句柄建立之后再设置子控件可见性。
        /// 构造期控件还没有父窗口，此时设 Visible 会让 AntdUI 提前建一份句柄（挂在临时窗口上），
        /// 挂载后还要再建一次，既多耗句柄也是"创建窗口句柄时出错"的现场。
        /// </summary>
        private void ApplyDeferredVisibility()
        {
            if (IsDisposed) return;
            btnCopy.Visible = _showCopyButton;
            lblTeamTag.Visible = _teamKnown;
        }

        /// <summary>
        /// 根据卡片宽度自适应排列头部控件（窄卡片时名称收缩、右侧控件贴边）。
        /// </summary>
        private void RecalcHeaderLayout()
        {
            const int textLeft = 56;
            const int copyWidth = 60;
            const int currentIconWidth = 28;
            const int tagWidth = 36;
            const int premadeWidth = 46;
            const int gap = 6;

            int right = Math.Max(textLeft + copyWidth + gap, ClientSize.Width - 12);
            int copyLeft = Math.Max(textLeft, right - copyWidth);
            int currentIconLeft = Math.Max(textLeft, copyLeft - gap - currentIconWidth);
            btnCopy.Location = new Point(copyLeft, 8);
            picCurrent.Location = new Point(currentIconLeft, 10);

            // 顶行优先保证玩家名称；宽度不足时隐藏战绩汇总，避免文字彼此覆盖。
            int summaryRight = currentIconLeft - gap;
            int summaryWidth = Math.Min(135, Math.Max(0, summaryRight - textLeft - 88));
            bool showSummary = summaryWidth >= 78;
            lblSummary.Visible = showSummary;
            if (showSummary)
            {
                lblSummary.Location = new Point(summaryRight - summaryWidth, 8);
                lblSummary.Width = summaryWidth;
            }

            int nameRight = showSummary ? summaryRight - summaryWidth - gap : summaryRight;
            lblName.Location = new Point(textLeft, 8);
            lblName.Width = Math.Max(0, nameRight - textLeft);

            // 第二行将玩家信息、当前英雄和队伍标签按可用空间从左到右分配。
            int championRight = currentIconLeft - gap;
            if (_teamKnown)
            {
                int teamTagLeft = championRight - tagWidth;
                lblTeamTag.Location = new Point(teamTagLeft, 32);
                lblTeamTag.Size = new Size(tagWidth, 20);
                championRight = teamTagLeft - gap;
            }
            if (lblPremadeTag.Visible)
            {
                int premadeTagLeft = championRight - premadeWidth;
                lblPremadeTag.Location = new Point(premadeTagLeft, 32);
                lblPremadeTag.Size = new Size(premadeWidth, 20);
                championRight = premadeTagLeft - gap;
            }

            int rowTwoSpace = Math.Max(0, championRight - textLeft);
            int subWidth = Math.Min(160, Math.Max(0, rowTwoSpace / 2));
            int championLeft = textLeft + subWidth + gap;
            int championWidth = Math.Max(0, championRight - championLeft);
            bool showChampion = championWidth >= 70 && !string.IsNullOrEmpty(lblChampionNow.Text);

            lblSub.Location = new Point(textLeft, 32);
            lblSub.Width = showChampion ? subWidth : rowTwoSpace;
            lblChampionNow.Visible = showChampion;
            if (showChampion)
            {
                lblChampionNow.Location = new Point(championLeft, 32);
                lblChampionNow.Width = championWidth;
            }
        }

        private void ResizeMatchRows()
        {
            int width = Math.Max(100, panelMatches.ClientSize.Width - 18);
            foreach (var row in panelMatches.Controls.OfType<RecentMatchRow>())
            {
                row.Width = width;
            }
        }

        /// <summary>
        /// 设置/清除开黑标记（由对局页开黑检测结果回填）。
        /// </summary>
        /// <param name="groupIndex">开黑组号（1 起），null 表示不在任何开黑小组。</param>
        /// <param name="memberNames">同组玩家名称（用于悬停提示）。</param>
        public void SetPremadeGroup(int? groupIndex, List<string>? memberNames)
        {
            if (!groupIndex.HasValue)
            {
                lblPremadeTag.Visible = false;
                RecalcHeaderLayout();
                return;
            }

            var color = PremadeColors[(groupIndex.Value - 1) % PremadeColors.Length];
            lblPremadeTag.Text = $"开黑{groupIndex.Value}";
            lblPremadeTag.ForeColor = color;
            lblPremadeTag.BackColor = Color.FromArgb(
                255,
                Math.Min(255, color.R + 95),
                Math.Min(255, color.G + 95),
                Math.Min(255, color.B + 95));

            _premadeTip ??= new ToolTip();
            string tooltip = memberNames is { Count: > 0 }
                ? $"开黑小组：{string.Join("、", memberNames)}（近期多次同队）"
                : "开黑小组（近期多次同队）";
            _premadeTip.SetToolTip(lblPremadeTag, tooltip);

            lblPremadeTag.Visible = true;
            RecalcHeaderLayout();
        }

        private async Task LoadAsync()
        {
            if (_isBot || string.IsNullOrEmpty(_playerPuuid))
            {
                await RenderBotHeaderAsync();
                return;
            }

            ShowShimmer();

            // ── 玩家信息（名称/等级/头像） ──
            string displayName = lblName.Text ?? "未知玩家";
            string? tagLine = "";
            int? level = null;
            int profileIconId = 0;
            try
            {
                PlayerProfile? info = await GetPlayerProfileAsync(_playerPuuid);
                if (info != null)
                {
                    if (!string.IsNullOrEmpty(info.GameName)) displayName = info.GameName;
                    tagLine = info.TagLine;
                    level = info.SummonerLevel;
                    profileIconId = info.ProfileIconId;
                }
            }
            catch
            {
                // 玩家信息获取失败时使用兜底名称
            }

            if (IsDisposed) return;
            lblName.Text = displayName;
            string positionText = GetPositionText(_position);
            lblSub.Text = !level.HasValue
                ? (string.IsNullOrEmpty(tagLine) ? positionText : $"#{tagLine} {positionText}")
                : $"Lv.{level}  #{tagLine} {positionText}".Trim();

            await LoadProfileIconAsync(profileIconId);
            await LoadCurrentChampionAsync();

            // ── 近 10 场战绩 ──
            MatchHistoryResponse? matchlists;
            try
            {
                matchlists = await GetRecentHistoryAsync(_playerPuuid);
            }
            catch
            {
                if (!IsDisposed) PublishRecentPerformance(CreateInsufficientPerformanceAssessment());
                return;
            }
            if (matchlists?.Games?.Games == null || IsDisposed)
            {
                if (!IsDisposed) PublishRecentPerformance(CreateInsufficientPerformanceAssessment());
                return;
            }

            var games = matchlists.Games.Games
                .OrderByDescending(g => g.GameCreation)
                .Take(RecentGamesCount)
                .ToList();

            // 并发加载每场详情
            var tasks = games.Select(async head =>
            {
                await GlobalMatchDetailLoadGate.WaitAsync();
                try
                {
                    var detail = await GetMatchDetailAsync(head.GameId);
                    if (detail == null || string.IsNullOrEmpty(_playerPuuid))
                        return (detail: (MatchDetail?)null, gamer: (MatchParticipant?)null);
                    var gamer = detail.GetParticipant(_playerPuuid);
                    return (detail, gamer);
                }
                catch
                {
                    return (detail: (MatchDetail?)null, gamer: (MatchParticipant?)null);
                }
                finally
                {
                    GlobalMatchDetailLoadGate.Release();
                }
            }).ToList();

            var results = (await Task.WhenAll(tasks))
                .Where(r => r.detail != null && r.gamer != null)
                .Select(r => (detail: r.detail!, gamer: r.gamer!))
                .ToList();

            if (IsDisposed) return;

            int wins = results.Count(r => r.gamer.IsWin());
            int losses = results.Count - wins;
            double rate = results.Count > 0 ? Math.Round((double)wins / results.Count * 100, 1) : 0;
            RecentModePerformanceAssessment assessment = ApplyLivePerformanceTag(
                matchlists.Games.Games, results, wins, losses, rate);
            PublishRecentPerformance(assessment);

            // 清掉加载微光，手工定位渲染战绩行（新→旧）
            panelMatches.Controls.Clear();
            int y = 0;
            for (int i = 0; i < results.Count; i++)
            {
                var (detail, gamer) = results[i];
                var row = new RecentMatchRow
                {
                    Location = new Point(0, y),
                    Width = Math.Max(100, panelMatches.ClientSize.Width - 18),
                    Height = RecentMatchRow.RowHeight
                };
                panelMatches.Controls.Add(row);
                UiTheme.Apply(row);
                y += RecentMatchRow.RowHeight;
                UiAnimation.SlideIn(row, -16, 220, i * 35);
                _ = row.SetDataAsync(detail, gamer, _playerPuuid);
            }
            panelMatches.AutoScrollMinSize = new Size(panelMatches.ClientSize.Width, y);
            ResizeMatchRows();
            UiTheme.Apply(this);
        }

        private Task<PlayerProfile?> GetPlayerProfileAsync(string puuid) =>
            GetCachedAsync(PlayerProfileCache, puuid, () => _playerProfileService.GetByPuuidAsync(puuid));

        private Task<MatchHistoryResponse?> GetRecentHistoryAsync(string puuid) =>
            GetCachedAsync(RecentHistoryCache, puuid, () => _matchHistoryService.GetPageAsync(puuid, 0, HistoryFetchCount - 1));

        private Task<MatchDetail?> GetMatchDetailAsync(long gameId) =>
            GetCachedAsync(MatchDetailCache, gameId, () => _matchHistoryService.GetDetailAsync(gameId));

        private static Task<T?> GetCachedAsync<TKey, T>(
            ConcurrentDictionary<TKey, (DateTime CachedAt, Task<T?> Value)> cache,
            TKey key,
            Func<Task<T?>> loader) where TKey : notnull
        {
            if (cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CachedAt < PlayerCacheTtl)
                return cached.Value;

            Task<T?> task = loader();
            cache[key] = (DateTime.UtcNow, task);
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted || completed.IsCanceled) cache.TryRemove(key, out _);
            }, TaskScheduler.Default);
            return task;
        }

        /// <summary>
        /// 评分只统计与当前队列相同的近期已结束对局，并排除重开局；
        /// 数据取自战绩摘要（已含本人 KDA 与胜负），因此不必为评分逐场拉详情。
        /// </summary>
        private RecentModePerformanceAssessment ApplyLivePerformanceTag(
            IReadOnlyList<MatchHistoryGame> history,
            IReadOnlyList<(MatchDetail detail, MatchParticipant gamer)> results,
            int allWins,
            int allLosses,
            double allRate)
        {
            var comparable = history
                .Where(IsComparableMode)
                .Where(game => game.IsCompletedGame())
                .OrderByDescending(game => game.GameCreation)
                .Take(PerformanceSampleSize)
                .ToList();

            if (comparable.Count == 0)
            {
                lblSummary.Text = results.Count > 0
                    ? $"近{results.Count}场 {allWins}胜{allLosses}负 · {allRate}%"
                    : "暂无战绩";
                _performanceTip.SetToolTip(lblSummary, "未识别到当前队列，暂不进行上/中/下等马判定。");
                return CreateInsufficientPerformanceAssessment();
            }

            var assessments = new List<MatchPerformanceAssessment>();
            var wins = new List<bool>();
            foreach (MatchHistoryGame game in comparable)
            {
                MatchParticipant? gamer = game.GetParticipant(_playerPuuid);
                if (gamer?.stats == null) continue;
                // 摘要里只有本人数据，缺少队内对比，因此这里只承载 KDA 供近期汇总使用。
                assessments.Add(new MatchPerformanceAssessment(
                    MatchPerformanceTier.Medium, 0, "",
                    gamer.stats.kills, gamer.stats.deaths, gamer.stats.assists));
                wins.Add(gamer.stats.Win);
            }

            RecentModePerformanceAssessment assessment = RecentModePerformanceEvaluator.Evaluate(
                comparable[0].GetModeText(), assessments, wins, PerformanceSampleSize);
            if (!assessment.HasEnoughSample)
            {
                lblSummary.Text = $"样本不足 {assessment.SampleSize}/{PerformanceSampleSize} · KDA {assessment.Kda:F2}";
                lblSummary.ForeColor = UiTheme.Palette.TextSecondary;
                _performanceTip.SetToolTip(lblSummary, assessment.Detail);
                return assessment;
            }

            string label = RecentPerformanceLabelFormatter.GetText(assessment);
            lblSummary.Text = $"{label} · KDA {assessment.Kda:F2}";
            lblSummary.ForeColor = assessment.Label switch
            {
                RecentPerformanceLabel.Upper => Color.FromArgb(27, 94, 32),
                RecentPerformanceLabel.Human => Color.FromArgb(123, 31, 162),
                RecentPerformanceLabel.Lower => Color.FromArgb(183, 28, 28),
                _ => Color.FromArgb(85, 85, 85)
            };
            _performanceTip.SetToolTip(lblSummary, assessment.Detail);
            return assessment;
        }

        private RecentModePerformanceAssessment CreateInsufficientPerformanceAssessment() =>
            RecentModePerformanceEvaluator.Evaluate(
                string.IsNullOrWhiteSpace(_currentGameMode) ? "当前队列" : _currentGameMode,
                Array.Empty<MatchPerformanceAssessment>(),
                Array.Empty<bool>(),
                PerformanceSampleSize);

        private void PublishRecentPerformance(RecentModePerformanceAssessment assessment)
        {
            if (_recentPerformancePublished || IsDisposed || string.IsNullOrWhiteSpace(_playerPuuid)) return;
            _recentPerformancePublished = true;
            RecentPerformanceReady?.Invoke(this, new PlayerRecentPerformanceEventArgs(
                _playerPuuid,
                lblName.Text,
                _isAlly,
                assessment));
        }

        private bool IsComparableMode(MatchHistoryGame game)
        {
            if (_currentQueueId > 0)
                return game.QueueId == _currentQueueId;
            return !string.IsNullOrWhiteSpace(_currentGameMode) &&
                   string.Equals(game.GameMode, _currentGameMode, StringComparison.OrdinalIgnoreCase);
        }

        private void ShowShimmer()
        {
            panelMatches.Controls.Clear();
            var shimmer = new ShimmerPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 248, 251)
            };
            panelMatches.Controls.Add(shimmer);
        }

        private async Task RenderBotHeaderAsync()
        {
            lblName.Text = string.IsNullOrEmpty(lblName.Text) ? "机器人" : lblName.Text;
            lblSub.Text = "机器人";
            btnCopy.Visible = false;
            lblSummary.Text = "";
            if (_championId > 0)
            {
                lblChampionNow.Text = $"当前: {GetChampionDisplayName(_championId)}";
                lblChampionNow.Visible = true;
                RecalcHeaderLayout();
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(_championId));
                if (icon != null && !IsDisposed) picCurrent.Image = icon;
                else icon?.Dispose();
            }
            panelMatches.Controls.Clear();
            panelMatches.Controls.Add(new AntdUI.Label
            {
                Text = "机器人没有战绩数据",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(8)
            });
        }

        private async Task LoadProfileIconAsync(int profileIconId)
        {
            if (profileIconId <= 0) return;
            try
            {
                byte[]? imageBytes = await _profileIconService.GetProfileIconAsync(profileIconId);
                if (imageBytes is { Length: > 0 } && !IsDisposed)
                {
                    using var ms = new MemoryStream(imageBytes);
                    using var temp = Image.FromStream(ms);
                    _ownedProfileImage?.Dispose();
                    _ownedProfileImage = new Bitmap(temp);
                    picProfile.Image = _ownedProfileImage;
                }
            }
            catch
            {
                // 头像加载失败不影响卡片
            }
        }

        private async Task LoadCurrentChampionAsync()
        {
            if (_championId <= 0) return;
            try
            {
                lblChampionNow.Text = $"当前: {GetChampionDisplayName(_championId)}";
                lblChampionNow.Visible = true;
                RecalcHeaderLayout();
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(_championId));
                if (icon != null && !IsDisposed) picCurrent.Image = icon;
                else icon?.Dispose();
            }
            catch
            {
                // 当前英雄加载失败不影响卡片
            }
        }

        /// <summary>将应用服务的二进制英雄资源解码为当前 WinForms 控件所需的位图。</summary>
        private static Image? ToImage(GameAsset? asset)
        {
            try
            {
                if (asset == null || asset.IsEmpty) return null;
                using var stream = new MemoryStream(asset.Content);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        private static string GetPositionText(string position)
        {
            return position.ToLowerInvariant() switch
            {
                "top" => "上路",
                "jungle" => "打野",
                "middle" => "中路",
                "bottom" => "下路",
                "utility" or "support" => "辅助",
                "fill" => "补位",
                _ => position
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // 展开动画会把卡片高度插值到 0，此时 Width-3 / Height-3 为 0 或负数，
            // GDI+ 不接受空矩形，会抛 ArgumentException 打断绘制。
            if (Width <= 3 || Height <= 3) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = GradientPanel.RoundedRect(rect, 12);

            // 悬停发光外圈
            if (_glowAlpha > 0.02)
            {
                using var glow = new Pen(
                    Color.FromArgb((int)(_glowAlpha * 70), _teamColor.R, _teamColor.G, _teamColor.B), 5);
                g.DrawPath(glow, path);
            }

            using var border = new Pen(
                Color.FromArgb((int)(80 + _glowAlpha * 90), _teamColor.R, _teamColor.G, _teamColor.B), 1.5f);
            g.DrawPath(border, path);
        }

        private void StartGlow(bool hovering)
        {
            _glowTarget = hovering;
            _glowT = 0;
            _glowTimer.Start();
        }

        private void GlowTick()
        {
            _glowT = Math.Min(1, _glowT + 0.12);
            double eased = UiAnimation.EaseOutCubic(_glowT);
            if (_glowTarget)
            {
                _glowAlpha = eased;
            }
            else
            {
                _glowAlpha = 1 - eased;
            }
            Invalidate();
            if (_glowT >= 1) _glowTimer.Stop();
        }

        private double _glowAlpha;

        private void BtnCopy_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_playerPuuid)) return;
            try
            {
                Clipboard.SetText(_playerPuuid);
                string preview = _playerPuuid.Length > 16
                    ? _playerPuuid[..16] + "..."
                    : _playerPuuid;
                AntdUI.Message.success(
                    ParentForm ?? FindForm() ?? Program.GameMain,
                    $"已复制玩家 ID（{preview}）");
            }
            catch
            {
                // 剪贴板被占用时忽略
            }
        }
    }

    /// <summary>玩家卡片完成同队列近期 KDA 计算后，提供给对局页汇总的一项结果。</summary>
    public sealed class PlayerRecentPerformanceEventArgs : EventArgs
    {
        public PlayerRecentPerformanceEventArgs(
            string puuid,
            string? displayName,
            bool isAlly,
            RecentModePerformanceAssessment assessment)
        {
            Puuid = puuid;
            DisplayName = displayName ?? "未知玩家";
            IsAlly = isAlly;
            Assessment = assessment;
        }

        public string Puuid { get; }
        public string DisplayName { get; }
        public bool IsAlly { get; }
        public RecentModePerformanceAssessment Assessment { get; }
    }
}
