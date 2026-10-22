using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
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
        private int _championId;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IProfileIconService _profileIconService;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IGameAssetService _gameAssetService;
        private readonly IRankedStatsService? _rankedStatsService;
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<RankedOverview?> Value)> RankedCache = new(StringComparer.Ordinal);
        private readonly AntdUI.Label _rankLabel = new()
        {
            Name = "lblRank", BackColor = Color.Transparent, Visible = false,
            Font = new Font("Microsoft YaHei UI", 8F), TextAlign = ContentAlignment.MiddleLeft
        };
        private readonly string _position;
        private readonly bool _isBot;
        private readonly bool _isAlly;
        private readonly bool _teamKnown;
        private readonly int _currentQueueId;
        private readonly string _currentGameMode;
        private readonly Color _teamColor;

        /// <summary>单次获取摘要；只有最近十场的展示行读取详情。</summary>
        private const int HistoryFetchCount = MatchDataPolicy.SummaryLimit;
        private static readonly TimeSpan MaximumLoadDuration = MatchDataPolicy.LoadTimeout;
        private string _loadingStage = "战绩列表";
        private static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<PlayerProfile?> Value)> PlayerProfileCache = new(StringComparer.Ordinal);
        private static readonly object CacheSync = new();
        internal static (int Entries, long Bytes) MeasureSharedCaches()
        {
            lock (CacheSync)
                return CacheDataSize.Measure(RankedCache.Values.Select(value => value.Value.IsCompletedSuccessfully ? (object?)value.Value.Result : null)
                    .Concat(PlayerProfileCache.Values.Select(value => value.Value.IsCompletedSuccessfully ? (object?)value.Value.Result : null)));
        }
        internal static void ClearSharedCaches()
        {
            lock (CacheSync) { RankedCache.Clear(); PlayerProfileCache.Clear(); PlayerHistoryRepository.ClearAll(); }
        }
        private Image? _ownedProfileImage;
        private Image? _ownedChampionImage;
        private ToolTip? _premadeTip;
        private ToolTip? _copyTip;
        private readonly PlayerIdentityActions _identityActions;
        private readonly ToolTip _performanceTip = new();
        private readonly CancellationTokenSource _lifetimeCancellation = new();

        private bool _recentPerformancePublished;
        private bool _loadStarted;
        private bool _loading;
        private bool _headerLoadStarted;
        private RecentModePerformanceAssessment? _cachedAssessment;
        private PlayerHistorySnapshot? _historySnapshot;
        private bool _layingOutMatchRows;

        /// <summary>当前卡片对应玩家的 puuid（供开黑检测结果回填）。</summary>
        public string? Puuid => _playerPuuid;
        /// <summary>将玩家近期战绩区域滚动到顶部。</summary>
        public void ScrollMatchesToTop() => panelMatches.AutoScrollPosition = Point.Empty;

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
        private double _glowStartAlpha;

        /// <summary>初始化 LivePlayerForm 的实例状态，并保存传入的依赖或数据。</summary>
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
                AppCompositionRoot.GameAssetService,
                AppCompositionRoot.RankedStatsService)
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
            IGameAssetService gameAssetService,
            IRankedStatsService? rankedStatsService = null)
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
            _rankedStatsService = rankedStatsService;
            headerPanel.Controls.Add(_rankLabel);
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
            lblChampionNow.Text = championId > 0 ? $"当前: {GetChampionDisplayName(championId)}" : "";

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

            // 玩家身份区域统一支持单击复制和双击查询，不再占用头部按钮空间。
            _copyTip = new ToolTip();
            _identityActions = new PlayerIdentityActions(() => _isBot ? null : _playerPuuid,
                () => ParentForm ?? FindForm());
            foreach (Control target in new Control[] { picProfile, lblName, lblSub, picCurrent, lblChampionNow, _rankLabel })
            {
                _identityActions.Attach(target);
                _copyTip.SetToolTip(target, "单击复制玩家 ID；双击查询该玩家战绩");
            }
            if (!string.IsNullOrEmpty(_playerPuuid))
            {
                _copyTip.SetToolTip(this, $"PUUID: {_playerPuuid}");
            }

            components ??= new System.ComponentModel.Container();
            _glowTimer = new System.Windows.Forms.Timer(components) { Interval = 15 };
            _glowTimer.Tick += (_, _) => GlowTick();
            HandleDestroyed += (_, _) => StopGlow();
            VisibleChanged += (_, _) => { if (!Visible) StopGlow(); };
            Disposed += (_, _) =>
            {
                _lifetimeCancellation.Cancel();
                _lifetimeCancellation.Dispose();
                _glowTimer.Dispose();
                _ownedProfileImage?.Dispose();
                _ownedChampionImage?.Dispose();
                _copyTip?.Dispose();
                _identityActions.Dispose();
                _premadeTip?.Dispose();
                _performanceTip.Dispose();
            };

            this.MouseEnter += (_, _) => StartGlow(true);
            this.MouseLeave += (_, _) => UpdateGlowFromPointer();
            foreach (Control child in Controls)
            {
                child.MouseEnter += (_, _) => StartGlow(true);
                child.MouseLeave += (_, _) => UpdateGlowFromPointer();
            }

            this.Resize += (_, _) => RecalcHeaderLayout();
            panelMatches.SizeChanged += (_, _) => ResizeMatchRows();
            panelMatches.Layout += (_, _) => ResizeMatchRows();
            panelMatches.VisibleChanged += (_, _) => { if (panelMatches.Visible) ResizeMatchRows(); };
            RecalcHeaderLayout();
            this.Load += async (_, _) =>
            {
                ApplyDeferredVisibility();
                // Reparenting a loaded card can raise Load again. Keep its existing request and rows.
                if (_loadStarted) return;
                await EnsureDataLoadedAsync();
            };
            UiTheme.Apply(this);
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceRaised;
            headerPanel.BackColor = palette.SurfaceMuted;
            panelMatches.BackColor = palette.Surface;
            Padding = new Padding(2);
            if (palette.IsDark)
            {
                BackColor = headerPanel.BackColor = Color.FromArgb(21, 32, 40);
                panelMatches.BackColor = Color.FromArgb(17, 27, 34);
            }
            lblName.ForeColor = palette.TextPrimary;
            _rankLabel.ForeColor = palette.Accent;
            _rankLabel.BackColor = palette.SurfaceMuted;
            _historyHeading!.ForeColor = palette.TextPrimary;
            lblSub.ForeColor = palette.TextSecondary;
            lblChampionNow.ForeColor = palette.Accent;
            _kdaLabel.ForeColor = palette.TextPrimary;
            _encounterLabel.ForeColor = _encounterLabel.Text?.StartsWith("近期遇到", StringComparison.Ordinal) == true
                ? palette.Accent : palette.TextSecondary;
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

        /// <summary>句柄创建后执行依赖原生窗口的初始化。</summary>
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
            lblTeamTag.Visible = _teamKnown;
            picCurrent.Visible = _championId > 0;
            RecalcHeaderLayout();
        }

        /// <summary>
        /// 根据卡片宽度自适应排列头部控件（窄卡片时名称收缩、右侧控件贴边）。
        /// </summary>
        private void RecalcHeaderLayout() => LayoutReferenceHeader();

        /// <summary>按容器宽度更新近期战绩行的大小。</summary>
        private void ResizeMatchRows()
        {
            if (_layingOutMatchRows || IsDisposed) return;
            _layingOutMatchRows = true;
            try
            {
                MatchListScrolling.LayoutRows(panelMatches,
                    panelMatches.Controls.Cast<Control>().ToArray(), 8, 5);
                // ResumeLayout can reveal the vertical scrollbar after the first width pass.
                MatchListScrolling.LayoutRows(panelMatches,
                    panelMatches.Controls.Cast<Control>().ToArray(), 8, 5);
            }
            finally { _layingOutMatchRows = false; }
        }

        /// <summary>展示玩家数据加载失败的状态。</summary>
        private void ShowLoadFailure(string status, string detail)
        {
            if (RecentEncounterHistory == null)
            {
                RecentEncounterUnavailable = true;
                OverviewChanged?.Invoke(this, EventArgs.Empty);
            }
            bool hasRows = panelMatches.Controls.OfType<RecentMatchRow>().Any();
            lblSummary.Text = hasRows
                ? (lblSummary.Text?.StartsWith("战绩加载中", StringComparison.Ordinal) == true ? "部分战绩已加载" : "评分暂不可用")
                : status;
            lblSummary.ForeColor = UiTheme.Palette.TextSecondary;
            if (_cachedAssessment is { SampleSize: > 0 } assessment)
            {
                lblSummary.Text = RecentPerformanceLabelFormatter.GetText(assessment);
                ShowRecentAssessment(assessment);
            }
            _performanceTip.SetToolTip(lblSummary, detail);
            // 评分失败或超时时，已显示的战绩仍然可用。
            if (!hasRows)
            {
                ControlLifetime.ClearAndDispose(panelMatches);
                panelMatches.Controls.Add(new AntdUI.Label
                {
                    Text = detail,
                    AutoSize = true,
                    ForeColor = UiTheme.Palette.TextSecondary,
                    Padding = new Padding(8)
                });
                var retry = new AntdUI.Button
                {
                    Name = "btnRetryHistory", Text = "重新加载", Size = new Size(100, 32),
                    Location = new Point(8, 64)
                };
                retry.Click += async (_, _) => await StartLoadAsync();
                panelMatches.Controls.Add(retry);
            }
            PublishRecentPerformance(null);
            RuntimeDiagnostics.Report("对局玩家战绩", status, detail);
        }

        private Task? _initialLoad;
        internal Task EnsureDataLoadedAsync() => _loadStarted ? _initialLoad ?? Task.CompletedTask : _initialLoad = StartLoadAsync();

        private async Task StartLoadAsync()
        {
            if (_loading || IsDisposed) return;
            _loadStarted = true;
            _loading = true;
            if (RecentEncounterHistory == null)
            {
                RecentEncounterUnavailable = false;
                OverviewChanged?.Invoke(this, EventArgs.Empty);
            }
            _recentPerformancePublished = false;
            _loadingStage = "战绩列表";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(MaximumLoadDuration);
            try
            {
                await Task.WhenAll(LoadAsync(timeout.Token), LoadRankAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed)
                    ShowLoadFailure("获取超时", $"{_loadingStage}超过 {MaximumLoadDuration.TotalMinutes:0} 分钟；已加载数据保留，可重新加载。");
            }
            catch (Exception ex)
            {
                if (!IsDisposed) ShowLoadFailure("获取失败", $"对局数据获取失败：{ex.Message}");
            }
            finally { _loading = false; }
        }

        /// <summary>
        /// 设置/清除开黑标记（由对局页开黑检测结果回填）。
        /// </summary>
        /// <param name="groupIndex">开黑组号（1 起），null 表示不在任何开黑小组。</param>
        /// <param name="memberNames">同组玩家名称（用于悬停提示）。</param>
        public void SetPremadeGroup(int? groupIndex, List<string>? memberNames, bool isInferred = false)
        {
            _layoutPremadeNote = groupIndex.HasValue ? $"{(isInferred ? "疑似开黑" : "开黑")}{groupIndex.Value}" : "";
            if (!groupIndex.HasValue)
            {
                lblPremadeTag.Visible = false;
                RecalcHeaderLayout();
                return;
            }

            var color = PremadeColors[(groupIndex.Value - 1) % PremadeColors.Length];
            lblPremadeTag.Text = $"{(isInferred ? "疑似开黑" : "开黑")}{groupIndex.Value}";
            lblPremadeTag.ForeColor = color;
            lblPremadeTag.BackColor = Color.FromArgb(
                255,
                Math.Min(255, color.R + 95),
                Math.Min(255, color.G + 95),
                Math.Min(255, color.B + 95));

            _premadeTip ??= new ToolTip();
            string tooltip = memberNames is { Count: > 0 }
                ? $"本局同组：{string.Join("、", memberNames)}（房间标识相同）"
                : "本局房间标识相同";
            if (isInferred) tooltip = $"疑似开黑：{string.Join("、", memberNames ?? [])}；近期20场至少2次同队，不能确认本局组队。";
            _premadeTip.SetToolTip(lblPremadeTag, tooltip);

            lblPremadeTag.Visible = true;
            RecalcHeaderLayout();
        }

        /// <summary>读取并展示玩家排位信息。</summary>
        private async Task LoadRankAsync(CancellationToken cancellationToken)
        {
            if (_rankedStatsService == null || _isBot || string.IsNullOrWhiteSpace(_playerPuuid)) return;
            string text = UiLanguage.IsEnglish ? "Rank unavailable" : "段位未知";
            string tooltip = text;
            try
            {
                (DateTime CachedAt, Task<RankedOverview?> Value) cached;
                lock (CacheSync)
                {
                    if (!RankedCache.TryGetValue(_playerPuuid, out cached) ||
                        DateTime.UtcNow - cached.CachedAt > PlayerCacheTtl || cached.Value.IsFaulted || cached.Value.IsCanceled ||
                        (cached.Value.IsCompletedSuccessfully && (cached.Value.Result == null || cached.Value.Result.Queues.Count == 0)))
                    {
                        cached = (DateTime.UtcNow, _rankedStatsService.GetAsync(_playerPuuid));
                        RankedCache[_playerPuuid] = cached;
                    }
                }
                var overview = await cached.Value.WaitAsync(cancellationToken);
                if (overview != null)
                {
                    var solo = overview.GetQueue(RankedQueues.Solo5x5);
                    var queue = RankedDisplayRules.HasRank(solo) ? solo : overview.GetQueue(RankedQueues.Flex5x5);
                    text = RankedDisplayFormatter.FormatCompact(queue, UiLanguage.IsEnglish);
                    tooltip = RankedDisplayRules.HasRank(queue)
                        ? $"{(queue?.QueueType == RankedQueues.Flex5x5 ? "灵活组排" : "单双排")} · {text} · {queue!.LeaguePoints} 胜点"
                        : text;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch { /* 排位接口不可用时不影响资料和战绩。 */ }
            if (IsDisposed) return;
            _rankLabel.Text = text;
            _rankLabel.Visible = true;
            _performanceTip.SetToolTip(_rankLabel, tooltip);
            RecalcHeaderLayout();
            OverviewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>异步加载当前对象负责的数据。</summary>
        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            if (_isBot || string.IsNullOrEmpty(_playerPuuid))
            {
                await RenderBotHeaderAsync(cancellationToken);
                return;
            }

            ShowShimmer();

            if (!_headerLoadStarted)
            {
                _headerLoadStarted = true;
                _ = LoadPlayerHeaderAsync();
                _ = LoadHeaderAssetsAsync();
            }

            await LoadHistoryAsync(cancellationToken);
        }

        /// <summary>资料和图像独立加载，不占用战绩列表的完成条件。</summary>
        private async Task LoadPlayerHeaderAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(MaximumLoadDuration);
            CancellationToken cancellationToken = timeout.Token;

            // ── 玩家信息（名称/等级/头像） ──
            string displayName = lblName.Text ?? "未知玩家";
            string? tagLine = "";
            int? level = null;
            int profileIconId = 0;
            try
            {
                PlayerProfile? info = await GetPlayerProfileAsync(_playerPuuid!, cancellationToken);
                if (info != null)
                {
                    if (!string.IsNullOrEmpty(info.GameName)) displayName = info.GameName;
                    tagLine = info.TagLine;
                    level = info.SummonerLevel;
                    profileIconId = info.ProfileIconId;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch
            {
                // 玩家信息获取失败时使用兜底名称
            }

            if (IsDisposed) return;
            PlayerLevel = level > 0 ? level : null;
            OverviewChanged?.Invoke(this, EventArgs.Empty);
            lblName.Text = displayName;
            RecalcHeaderLayout();
            string positionText = GetPositionText(_position);
            lblSub.Text = !level.HasValue
                ? (string.IsNullOrEmpty(tagLine) ? positionText : $"#{tagLine} {positionText}")
                : $"Lv.{level}  #{tagLine} {positionText}".Trim();

            // 图像下载不阻塞战绩请求；其失败也不应让已获取的战绩消失。
            try { await LoadProfileIconAsync(profileIconId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        private async Task LoadHistoryAsync(CancellationToken cancellationToken)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            MatchHistoryResponse? history;
            try { history = await GetRecentHistoryAsync(_playerPuuid!, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                if (!IsDisposed) ShowLoadFailure("获取失败", $"战绩列表获取失败：{error.Message}");
                return;
            }
            if (history?.Games?.Games == null || IsDisposed) return;
            cancellationToken.ThrowIfCancellationRequested();
            SetRecentEncounterHistory(history);
            RenderVisibleHistory(history);
            await CompleteAssessmentAsync(history);
            _loadingStage = "摘要已显示";
            RuntimeDiagnostics.Report("对局摘要列表", "已显示", $"{LayoutHistory.Count} 行 · {timer.ElapsedMilliseconds}ms");
        }

        private void RenderVisibleHistory(MatchHistoryResponse history)
        {
            if (history.Games == null) return;
            var games = LayoutHistory;
            var iconTasks = new List<Task>();
            panelMatches.SuspendLayout();
            try
            {
                ControlLifetime.ClearAndDispose(panelMatches);
                if (games.Count == 0 && history.Games.MissingSummaryCount > 0) panelMatches.Controls.Add(new AntdUI.Label
                {
                    Text = history.Games.DataWarning, Height = 48,
                    ForeColor = UiTheme.Palette.TextSecondary, Padding = new Padding(8)
                });
                foreach (var game in games)
                {
                    var row = new RecentMatchRow(_gameAssetService, _matchHistoryService)
                    {
                        Width = Math.Max(100, panelMatches.ClientSize.Width - 18), Height = RecentMatchRow.RowHeight
                    };
                    row.UseReferenceLayout();
                    panelMatches.Controls.Add(row);
                    iconTasks.Add(row.SetSummaryAsync(game, _playerPuuid!));
                }
                if (games.Count == 0 && history.Games.MissingSummaryCount == 0) panelMatches.Controls.Add(new AntdUI.Label
                {
                    Text = HistoryFilter == LiveHistoryFilter.CurrentMode ? "已获取摘要中暂无当前模式战绩" : "暂无战绩", AutoSize = true, ForeColor = UiTheme.Palette.TextSecondary, Padding = new Padding(8)
                });
            }
            finally { panelMatches.ResumeLayout(); }
            if (_historyHeading != null)
                _historyHeading.Text = $"{(HistoryFilter == LiveHistoryFilter.CurrentMode ? "当前模式" : "最近战绩")} · {games.Count} 场" + (games.Count > 0 ? " · 向下滚动查看" : "");
            ResizeMatchRows();
            // 文字立即显示，图标各自异步补齐，不阻塞评级和卡片完成。
            _ = Task.WhenAll(iconTasks);
            var results = games.Select(game => game.GetParticipant(_playerPuuid!)?.stats)
                .Where(stats => stats?.HasWinResult == true).ToArray();
            RecentWins = results.Count(stats => stats!.Win);
            RecentSampleSize = results.Length;
            OverviewChanged?.Invoke(this, EventArgs.Empty);
        }
        private async Task CompleteAssessmentAsync(MatchHistoryResponse matchlists)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                var assessment = await ApplyLivePerformanceTagAsync(matchlists, timeout.Token);
                if (!IsDisposed) PublishRecentPerformance(assessment);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed)
                    if (!UseLoadedAssessmentOnFailure("摘要评分计算超时"))
                        ShowLoadFailure("评分暂不可用", "近期战绩已加载；最近50场评分请求超时，不影响战绩查看。");
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !UseLoadedAssessmentOnFailure("摘要评分计算失败"))
                    ShowLoadFailure("评分暂不可用", $"近期战绩已加载；评分请求失败：{ex.Message}");
            }
        }

        private async Task LoadHeaderAssetsAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(MaximumLoadDuration);
            try
            {
                await LoadCurrentChampionAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }

        /// <summary>读取指定玩家资料，供玩家卡片展示。</summary>
        private Task<PlayerProfile?> GetPlayerProfileAsync(string puuid, CancellationToken cancellationToken) =>
            GetCachedAsync(PlayerProfileCache, puuid,
                () => _playerProfileService.GetByPuuidAsync(puuid, cancellationToken)).WaitAsync(cancellationToken);

        /// <summary>读取指定玩家的近期战绩。</summary>
        private async Task<MatchHistoryResponse?> GetRecentHistoryAsync(string puuid, CancellationToken cancellationToken)
        {
            var snapshot = await PlayerHistoryRepository.For(_matchHistoryService).GetBatchAsync(puuid, cancellationToken);
            if (!snapshot.Available) throw new IOException(snapshot.Error ?? "战绩摘要暂不可用");
            _historySnapshot = snapshot;
            return snapshot.Data;
        }
        /// <summary>优先使用缓存，并按需读取缺失数据。</summary>
        private static Task<T?> GetCachedAsync<TKey, T>(
            ConcurrentDictionary<TKey, (DateTime CachedAt, Task<T?> Value)> cache,
            TKey key,
            Func<Task<T?>> loader, Func<T, bool>? valid = null) where TKey : notnull
        {
            lock (CacheSync)
            {
            if (cache.TryGetValue(key, out var cached) &&
                (!cached.Value.IsCompleted || DateTime.UtcNow - cached.CachedAt < PlayerCacheTtl) &&
                !cached.Value.IsFaulted && !cached.Value.IsCanceled &&
                !(cached.Value.IsCompletedSuccessfully && (cached.Value.Result is null ||
                    (valid != null && !valid(cached.Value.Result)))))
                return cached.Value;

            Task<T?> task = loader();
            DateTime cacheTime = DateTime.UtcNow;
            cache[key] = (cacheTime, task);
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted || completed.IsCanceled || completed.Result is null ||
                    (valid != null && !valid(completed.Result)))
                    cache.TryRemove(new KeyValuePair<TKey, (DateTime CachedAt, Task<T?> Value)>(key, (cacheTime, task)));
            }, TaskScheduler.Default);
            return task;
            }
        }

        /// <summary>评分和胜负只读取已获取摘要，不随详情成功数量变化。</summary>
        private Task<RecentModePerformanceAssessment> ApplyLivePerformanceTagAsync(
            MatchHistoryResponse firstPage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statistics = PlayerAnalysisCoordinator.Analyze(firstPage, _playerPuuid!, _currentQueueId, _currentGameMode,
                LolGameModeNames.GetModeText(_currentQueueId.ToString(), _currentGameMode));
            var assessment = statistics.Assessment with { Detail = $"{_historySnapshot?.ScopeText}。{statistics.Assessment.Detail}" };
            OverviewChanged?.Invoke(this, EventArgs.Empty);

            string label = RecentPerformanceLabelFormatter.GetText(assessment);
            ShowRecentAssessment(assessment);
            if (!IsDisposed)
            {
                lblSummary.Text = label;
                lblSummary.ForeColor = assessment.Label switch
                {
                    RecentPerformanceLabel.Upper => Color.FromArgb(102, 187, 106),
                    RecentPerformanceLabel.Human => Color.FromArgb(190, 128, 235),
                    RecentPerformanceLabel.Lower => Color.FromArgb(239, 83, 80),
                    _ => UiTheme.Palette.TextSecondary
                };
            }
            string recentRecord = RecentSampleSize > 0
                ? $"近{RecentSampleSize}场 {RecentWins}胜{RecentSampleSize - RecentWins}负"
                : "暂无战绩";
            _performanceTip.SetToolTip(lblSummary,
                $"已获取摘要中，最近50场{(assessment.UsesAllModes ? "各模式" : "同模式")}有效对局 {assessment.SampleSize} 场。" +
                (assessment.SampleSize == 0 ? $"暂不分档。{recentRecord}" : assessment.Detail));
            return Task.FromResult(assessment);
        }

        /// <summary>通知外部订阅者当前玩家的近期表现结果。</summary>
        private void PublishRecentPerformance(RecentModePerformanceAssessment? assessment)
        {
            if (_recentPerformancePublished || IsDisposed || string.IsNullOrWhiteSpace(_playerPuuid)) return;
            _recentPerformancePublished = true;
            _cachedAssessment = assessment;
            OverviewChanged?.Invoke(this, EventArgs.Empty);
            RecentPerformanceReady?.Invoke(this, new PlayerRecentPerformanceEventArgs(
                _playerPuuid,
                lblName.Text,
                _isAlly,
                assessment));
        }

        /// <summary>判断当前卡片是否可继续用于指定玩家上下文。</summary>
        internal bool CanReuseFor(int queueId, string? gameMode, bool isAlly, bool teamKnown, bool isBot) =>
            !IsDisposed && _isBot == isBot && _isAlly == isAlly && _teamKnown == teamKnown &&
            _currentQueueId == queueId &&
            (queueId > 0 || string.Equals(_currentGameMode, gameMode ?? "", StringComparison.OrdinalIgnoreCase));

        /// <summary>向订阅者重新发布已加载的近期表现结果。</summary>
        internal void ReplayRecentPerformance()
        {
            if (!_recentPerformancePublished || IsDisposed || string.IsNullOrWhiteSpace(_playerPuuid)) return;
            RecentPerformanceReady?.Invoke(this, new PlayerRecentPerformanceEventArgs(
                _playerPuuid, lblName.Text, _isAlly, _cachedAssessment));
        }

        /// <summary>切换数据加载期间的骨架占位展示。</summary>
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

        /// <summary>展示机器人或不可查询玩家的基础信息。</summary>
        private async Task RenderBotHeaderAsync(CancellationToken cancellationToken)
        {
            lblName.Text = string.IsNullOrEmpty(lblName.Text) ? "机器人" : lblName.Text;
            lblSub.Text = "机器人";
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

        /// <summary>读取并展示玩家召唤师头像。</summary>
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
                    OverviewChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // 头像加载失败不影响卡片
            }
        }

        /// <summary>读取并展示当前英雄图像。</summary>
        private async Task LoadCurrentChampionAsync(CancellationToken cancellationToken)
        {
            int championId = _championId;
            if (championId <= 0) return;
            try
            {
                lblChampionNow.Text = $"当前: {GetChampionDisplayName(_championId)}";
                lblChampionNow.Visible = true;
                RecalcHeaderLayout();
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(championId, cancellationToken)
                    .WaitAsync(cancellationToken));
                if (icon != null && !IsDisposed && _championId == championId) ReplaceChampionImage(icon);
                else icon?.Dispose();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // 当前英雄加载失败不影响卡片
            }
        }

        /// <summary>同一玩家的英雄晚到或换选时更新卡片，不重载战绩和公告。</summary>
        internal async Task UpdateCurrentChampionAsync(int championId)
        {
            if (IsDisposed || championId <= 0 || championId == _championId) return;
            _championId = championId;
            picCurrent.Image = null;
            _ownedChampionImage?.Dispose();
            _ownedChampionImage = null;
            lblChampionNow.Text = $"当前: {GetChampionDisplayName(championId)}";
            _copyTip?.SetToolTip(lblChampionNow, lblChampionNow.Text + "\n单击复制玩家 ID；双击查询该玩家战绩");
            picCurrent.Visible = true;
            RecalcHeaderLayout();
            try { await LoadCurrentChampionAsync(_lifetimeCancellation.Token); }
            catch (OperationCanceledException) { /* 卡片关闭时停止加载。 */ }
        }

        /// <summary>将应用服务的二进制英雄资源解码为当前 WinForms 控件所需的位图。</summary>
        private void ReplaceChampionImage(Image image)
        {
            picCurrent.Image = image;
            _ownedChampionImage?.Dispose();
            _ownedChampionImage = image;
        }

        /// <summary>将资源二进制转换为独立图像对象。</summary>
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

        /// <summary>根据英雄标识取得展示名称。</summary>
        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        /// <summary>将玩家分路标识转换为显示文本。</summary>
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

        /// <summary>绘制当前控件的自定义外观。</summary>
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

        /// <summary>启动玩家卡片的高亮动画。</summary>
        private void StartGlow(bool hovering)
        {
            if (IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
            // 子控件间移动不重启动画；反向过渡从当前亮度开始，避免边框突然跳亮。
            if (_glowTarget == hovering) return;
            _glowTarget = hovering;
            _glowStartAlpha = _glowAlpha;
            _glowT = 0;
            _glowTimer.Start();
        }

        private void StopGlow()
        {
            _glowTimer.Stop();
            _glowTarget = false;
            _glowAlpha = 0;
        }

        /// <summary>离开子控件时检查整张卡片，仍在卡片内则保留悬停状态。</summary>
        private void UpdateGlowFromPointer()
        {
            if (!IsDisposed && !Disposing && IsHandleCreated && Visible)
                StartGlow(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }

        /// <summary>推进高亮动画并触发重绘。</summary>
        private void GlowTick()
        {
            if (IsDisposed || Disposing || !IsHandleCreated || !Visible)
            {
                _glowTimer.Stop();
                return;
            }
            _glowT = Math.Min(1, _glowT + 0.12);
            double eased = UiAnimation.EaseOutCubic(_glowT);
            _glowAlpha = _glowStartAlpha + ((_glowTarget ? 1 : 0) - _glowStartAlpha) * eased;
            Invalidate();
            if (_glowT >= 1) _glowTimer.Stop();
        }

        private double _glowAlpha;

    }

    /// <summary>玩家卡片完成同队列近期 KDA 计算后，提供给对局页汇总的一项结果。</summary>
    public sealed class PlayerRecentPerformanceEventArgs : EventArgs
    {
        /// <summary>初始化 PlayerRecentPerformanceEventArgs 的实例状态，并保存传入的依赖或数据。</summary>
        public PlayerRecentPerformanceEventArgs(
            string puuid,
            string? displayName,
            bool isAlly,
            RecentModePerformanceAssessment? assessment)
        {
            Puuid = puuid;
            DisplayName = displayName ?? "未知玩家";
            IsAlly = isAlly;
            Assessment = assessment;
        }

        public string Puuid { get; }
        public string DisplayName { get; }
        public bool IsAlly { get; }
        public RecentModePerformanceAssessment? Assessment { get; }
    }
}
