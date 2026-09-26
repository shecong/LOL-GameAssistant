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

        private const int MaximumPerformanceHistoryPages = 3;

        /// <summary>最多统计最近 20 场同队列战绩；满 8 场即可按 KDA 分档。</summary>
        private const int MaximumPerformanceSampleSize = 20;

        private const int MinimumPerformanceSampleSize = RecentModePerformanceEvaluator.RequiredSampleSize;
        private static readonly TimeSpan MaximumLoadDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<PlayerProfile?> Value)> PlayerProfileCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<MatchHistoryResponse?> Value)> RecentHistoryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<long, (DateTime CachedAt, Task<MatchDetail?> Value)> MatchDetailCache = new();
        private static readonly SemaphoreSlim GlobalMatchDetailLoadGate = new(8, 8);
        private Image? _ownedProfileImage;
        private Image? _ownedChampionImage;
        private ToolTip? _premadeTip;
        private ToolTip? _copyTip;
        private readonly ToolTip _performanceTip = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();

        private readonly Button _historyButton = new()
        {
            Text = "查战绩",
            Size = new Size(60, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(25, 118, 210),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false
        };

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
            _historyButton.FlatAppearance.BorderSize = 0;
            _historyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _historyButton.Visible = false;
            _historyButton.Click += (_, _) => OpenMatchHistory();
            headerPanel.Controls.Add(_historyButton);

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
            _copyTip.SetToolTip(_historyButton, "打开此玩家的战绩查询");
            if (!string.IsNullOrEmpty(_playerPuuid))
            {
                _copyTip.SetToolTip(this, $"PUUID: {_playerPuuid}");
            }

            _glowTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _glowTimer.Tick += (_, _) => GlowTick();
            Disposed += (_, _) =>
            {
                _lifetimeCancellation.Cancel();
                _lifetimeCancellation.Dispose();
                _glowTimer.Dispose();
                _ownedProfileImage?.Dispose();
                _ownedChampionImage?.Dispose();
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                timeout.CancelAfter(MaximumLoadDuration);
                try
                {
                    await LoadAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed)
                        ShowLoadFailure("获取超时", "对局数据获取超过 1 分钟，已取消本次加载。");
                }
                catch (Exception ex)
                {
                    if (!IsDisposed) ShowLoadFailure("获取失败", $"对局数据获取失败：{ex.Message}");
                }
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
            _historyButton.Visible = _showCopyButton;
            lblTeamTag.Visible = _teamKnown;
        }

        /// <summary>
        /// 根据卡片宽度自适应排列头部控件（窄卡片时名称收缩、右侧控件贴边）。
        /// </summary>
        private void RecalcHeaderLayout()
        {
            const int textLeft = 56;
            const int copyWidth = 60;
            const int historyWidth = 60;
            const int currentIconWidth = 28;
            const int tagWidth = 36;
            const int premadeWidth = 46;
            const int gap = 6;

            int right = Math.Max(textLeft + copyWidth + historyWidth + 2 * gap, ClientSize.Width - 12);
            int copyLeft = Math.Max(textLeft, right - copyWidth);
            int historyLeft = Math.Max(textLeft, copyLeft - gap - historyWidth);
            int currentIconLeft = Math.Max(textLeft, historyLeft - gap - currentIconWidth);
            btnCopy.Location = new Point(copyLeft, 8);
            _historyButton.Location = new Point(historyLeft, 8);
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

        private void ShowLoadFailure(string status, string detail)
        {
            lblSummary.Text = status;
            lblSummary.ForeColor = UiTheme.Palette.TextSecondary;
            _performanceTip.SetToolTip(lblSummary, detail);
            ControlLifetime.ClearAndDispose(panelMatches);
            panelMatches.Controls.Add(new AntdUI.Label
            {
                Text = detail,
                AutoSize = true,
                ForeColor = UiTheme.Palette.TextSecondary,
                Padding = new Padding(8)
            });
            PublishRecentPerformance(CreateInsufficientPerformanceAssessment());
            RuntimeDiagnostics.Report("对局玩家战绩", status, detail);
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

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            if (_isBot || string.IsNullOrEmpty(_playerPuuid))
            {
                await RenderBotHeaderAsync(cancellationToken);
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
                PlayerProfile? info = await GetPlayerProfileAsync(_playerPuuid, cancellationToken);
                if (info != null)
                {
                    if (!string.IsNullOrEmpty(info.GameName)) displayName = info.GameName;
                    tagLine = info.TagLine;
                    level = info.SummonerLevel;
                    profileIconId = info.ProfileIconId;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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

            await LoadProfileIconAsync(profileIconId, cancellationToken);
            await LoadCurrentChampionAsync(cancellationToken);

            // ── 近 10 场战绩 ──
            MatchHistoryResponse? matchlists;
            try
            {
                matchlists = await GetRecentHistoryAsync(_playerPuuid, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
                await GlobalMatchDetailLoadGate.WaitAsync(cancellationToken);
                try
                {
                    var detail = await GetMatchDetailAsync(head.GameId, cancellationToken);
                    if (detail == null || string.IsNullOrEmpty(_playerPuuid))
                        return (detail: (MatchDetail?)null, gamer: (MatchParticipant?)null);
                    var gamer = detail.GetParticipant(_playerPuuid);
                    return (detail, gamer);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    return (detail: (MatchDetail?)null, gamer: (MatchParticipant?)null);
                }
                finally
                {
                    GlobalMatchDetailLoadGate.Release();
                }
            }).ToList();

            var results = (await Task.WhenAll(tasks).WaitAsync(cancellationToken))
                .Where(r => r.detail != null && r.gamer != null)
                .Select(r => (detail: r.detail!, gamer: r.gamer!))
                .ToList();

            if (IsDisposed) return;

            int wins = results.Count(r => r.gamer.IsWin());
            int losses = results.Count - wins;
            double rate = results.Count > 0 ? Math.Round((double)wins / results.Count * 100, 1) : 0;
            IReadOnlyList<MatchHistoryGame> performanceHistory = await GetPerformanceHistoryAsync(matchlists, cancellationToken);
            RecentModePerformanceAssessment assessment = await ApplyLivePerformanceTagAsync(
                performanceHistory, results, wins, losses, rate, cancellationToken);
            if (IsDisposed) return;
            PublishRecentPerformance(assessment);

            // 清掉加载微光，手工定位渲染战绩行（新→旧）
            ControlLifetime.ClearAndDispose(panelMatches);
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

        private Task<PlayerProfile?> GetPlayerProfileAsync(string puuid, CancellationToken cancellationToken) =>
            GetCachedAsync(PlayerProfileCache, puuid,
                () => _playerProfileService.GetByPuuidAsync(puuid, cancellationToken)).WaitAsync(cancellationToken);

        private Task<MatchHistoryResponse?> GetRecentHistoryAsync(string puuid, CancellationToken cancellationToken) =>
            GetCachedAsync(RecentHistoryCache, puuid,
                () => _matchHistoryService.GetPageAsync(puuid, 0, HistoryFetchCount - 1, cancellationToken))
                .WaitAsync(cancellationToken);

        private Task<MatchDetail?> GetMatchDetailAsync(long gameId, CancellationToken cancellationToken) =>
            GetCachedAsync(MatchDetailCache, gameId,
                () => _matchHistoryService.GetDetailAsync(gameId, cancellationToken: cancellationToken))
                .WaitAsync(cancellationToken);

        private async Task<IReadOnlyList<MatchHistoryGame>> GetPerformanceHistoryAsync(
            MatchHistoryResponse firstPage, CancellationToken cancellationToken)
        {
            var games = (firstPage.Games?.Games ?? []).ToList();
            if (string.IsNullOrWhiteSpace(_playerPuuid)) return games;
            var seen = games.Select(game => game.GameId).ToHashSet();
            int offset = games.Count;
            for (int pageNumber = 1; pageNumber < MaximumPerformanceHistoryPages &&
                 games.Count(game => IsComparableMode(game) && game.IsCompletedGame()) < MaximumPerformanceSampleSize;
                 pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (firstPage.Games?.GameCount is > 0 && offset >= firstPage.Games.GameCount) break;
                MatchHistoryResponse? page;
                try
                {
                    page = await _matchHistoryService.GetPageAsync(_playerPuuid,
                        offset, offset + HistoryFetchCount - 1, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { break; }
                var next = page?.Games?.Games;
                if (next == null || next.Count == 0) break;
                offset += next.Count;
                int added = 0;
                foreach (MatchHistoryGame game in next)
                    if (seen.Add(game.GameId)) { games.Add(game); added++; }
                if (added == 0) break;
            }
            return games;
        }

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
                if (completed.IsFaulted || completed.IsCanceled || completed.Result is null)
                    cache.TryRemove(key, out _);
            }, TaskScheduler.Default);
            return task;
        }

        /// <summary>
        /// 评分只统计与当前队列相同的近期已结束对局，并排除重开局；
        /// 优先使用战绩摘要；摘要中的 KDA 全为零时，用单局详情核实，避免把缺失字段当成绩。
        /// 少于 <see cref="MinimumPerformanceSampleSize"/> 场时标注样本不足，不推断玩家表现。
        /// </summary>
        private async Task<RecentModePerformanceAssessment> ApplyLivePerformanceTagAsync(
            IReadOnlyList<MatchHistoryGame> history,
            IReadOnlyList<(MatchDetail detail, MatchParticipant gamer)> results,
            int allWins,
            int allLosses,
            double allRate,
            CancellationToken cancellationToken)
        {
            var comparable = history
                .Where(IsComparableMode)
                .Where(game => game.IsCompletedGame())
                .OrderByDescending(game => game.GameCreation)
                .Take(HistoryFetchCount)
                .ToList();

            var validSamples = new List<MatchParticipantStats>();
            foreach (MatchHistoryGame[] batch in comparable.Chunk(MaximumPerformanceSampleSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = await Task.WhenAll(batch.Select(async game =>
                {
                    MatchParticipant? summary = game.GetParticipant(_playerPuuid);
                    MatchParticipantStats? stats = summary?.stats;
                    if (RecentKdaStatsResolver.NeedsDetail(stats))
                    {
                        await GlobalMatchDetailLoadGate.WaitAsync(cancellationToken);
                        try
                        {
                            MatchDetail? detail = await GetMatchDetailAsync(game.GameId, cancellationToken);
                            stats = RecentKdaStatsResolver.Resolve(stats,
                                detail?.GetParticipant(_playerPuuid)?.stats);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch
                        {
                            stats = null;
                        }
                        finally
                        {
                            GlobalMatchDetailLoadGate.Release();
                        }
                    }
                    return stats;
                })).WaitAsync(cancellationToken);

                validSamples.AddRange(samples.Where(stats => stats != null).Select(stats => stats!));
                if (validSamples.Count >= MaximumPerformanceSampleSize) break;
            }
            validSamples = validSamples.Take(MaximumPerformanceSampleSize).ToList();
            var assessments = validSamples.Select(stats => new MatchPerformanceAssessment(
                MatchPerformanceTier.Medium, 0, "", stats.kills, stats.deaths, stats.assists)).ToList();
            var wins = validSamples.Select(stats => stats.Win).ToList();

            RecentModePerformanceAssessment assessment = comparable.Count == 0
                ? CreateInsufficientPerformanceAssessment()
                : RecentModePerformanceEvaluator.Evaluate(
                    comparable[0].GetModeText(), assessments, wins,
                    MinimumPerformanceSampleSize, MaximumPerformanceSampleSize);

            string label = RecentPerformanceLabelFormatter.GetText(assessment);
            lblSummary.Text = assessment.SampleSize == 0 ? label : $"{label} · KDA {assessment.Kda:F2}";
            lblSummary.ForeColor = assessment.Label switch
            {
                RecentPerformanceLabel.Upper => Color.FromArgb(27, 94, 32),
                RecentPerformanceLabel.Human => Color.FromArgb(123, 31, 162),
                RecentPerformanceLabel.Lower => Color.FromArgb(183, 28, 28),
                _ => Color.FromArgb(85, 85, 85)
            };
            string recentRecord = results.Count > 0
                ? $"近{results.Count}场 {allWins}胜{allLosses}负 · {allRate}%"
                : "暂无战绩";
            _performanceTip.SetToolTip(lblSummary,
                $"同模式已结束 {comparable.Count} 场，可读取 KDA {assessment.SampleSize} 场。" +
                (assessment.SampleSize == 0 ? $"暂不分档。{recentRecord}" : assessment.Detail));
            return assessment;
        }

        private RecentModePerformanceAssessment CreateInsufficientPerformanceAssessment() =>
            RecentModePerformanceEvaluator.Evaluate(
                string.IsNullOrWhiteSpace(_currentGameMode) && _currentQueueId <= 0
                    ? "当前队列"
                    : LolGameModeNames.GetModeText(_currentQueueId > 0 ? _currentQueueId.ToString() : "", _currentGameMode),
                Array.Empty<MatchPerformanceAssessment>(),
                Array.Empty<bool>(),
                MinimumPerformanceSampleSize, MaximumPerformanceSampleSize);

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
            return MatchModeComparer.IsSameMode(_currentQueueId, _currentGameMode, game);
        }

        private void ShowShimmer()
        {
            ControlLifetime.ClearAndDispose(panelMatches);
            var shimmer = new ShimmerPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 248, 251)
            };
            panelMatches.Controls.Add(shimmer);
        }

        private async Task RenderBotHeaderAsync(CancellationToken cancellationToken)
        {
            lblName.Text = string.IsNullOrEmpty(lblName.Text) ? "机器人" : lblName.Text;
            lblSub.Text = "机器人";
            btnCopy.Visible = false;
            _historyButton.Visible = false;
            lblSummary.Text = "";
            if (_championId > 0)
            {
                lblChampionNow.Text = $"当前: {GetChampionDisplayName(_championId)}";
                lblChampionNow.Visible = true;
                RecalcHeaderLayout();
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(_championId, cancellationToken)
                    .WaitAsync(cancellationToken));
                if (icon != null && !IsDisposed) ReplaceChampionImage(icon);
                else icon?.Dispose();
            }
            ControlLifetime.ClearAndDispose(panelMatches);
            panelMatches.Controls.Add(new AntdUI.Label
            {
                Text = "机器人没有战绩数据",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(8)
            });
        }

        private async Task LoadProfileIconAsync(int profileIconId, CancellationToken cancellationToken)
        {
            if (profileIconId <= 0) return;
            try
            {
                byte[]? imageBytes = await _profileIconService.GetProfileIconAsync(profileIconId, cancellationToken)
                    .WaitAsync(cancellationToken);
                if (imageBytes is { Length: > 0 } && !IsDisposed)
                {
                    using var ms = new MemoryStream(imageBytes);
                    using var temp = Image.FromStream(ms);
                    _ownedProfileImage?.Dispose();
                    _ownedProfileImage = new Bitmap(temp);
                    picProfile.Image = _ownedProfileImage;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // 头像加载失败不影响卡片
            }
        }

        private async Task LoadCurrentChampionAsync(CancellationToken cancellationToken)
        {
            if (_championId <= 0) return;
            try
            {
                lblChampionNow.Text = $"当前: {GetChampionDisplayName(_championId)}";
                lblChampionNow.Visible = true;
                RecalcHeaderLayout();
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(_championId, cancellationToken)
                    .WaitAsync(cancellationToken));
                if (icon != null && !IsDisposed) ReplaceChampionImage(icon);
                else icon?.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // 当前英雄加载失败不影响卡片
            }
        }

        /// <summary>将应用服务的二进制英雄资源解码为当前 WinForms 控件所需的位图。</summary>
        private void ReplaceChampionImage(Image image)
        {
            picCurrent.Image = image;
            _ownedChampionImage?.Dispose();
            _ownedChampionImage = image;
        }

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

        /// <summary>大厅、选人与对局卡片共享的快速战绩入口。</summary>
        private void OpenMatchHistory()
        {
            if (string.IsNullOrWhiteSpace(_playerPuuid)) return;
            _ = BattleQueryForm.QueryPlayerAsync(_playerPuuid);
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