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
        private const int RecentGamesCount = 10;

        /// <summary>近期战绩列表的首次请求大小；表现评分会继续读取最近 30 天。</summary>
        private const int HistoryFetchCount = 100;
        private static readonly TimeSpan MaximumLoadDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan PlayerCacheTtl = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<PlayerProfile?> Value)> PlayerProfileCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<MatchHistoryResponse?> Value)> RecentHistoryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<long, (DateTime CachedAt, Task<MatchDetail?> Value)> MatchDetailCache = new();
        private static readonly SemaphoreSlim GlobalMatchDetailLoadGate = new(20, 20);
        private const int DetailWorkersPerPlayer = 2;
        private static readonly TimeSpan DetailRequestTimeout = TimeSpan.FromSeconds(5);
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
            RecalcHeaderLayout();
            this.Load += async (_, _) =>
            {
                ApplyDeferredVisibility();
                // Reparenting a loaded card can raise Load again. Keep its existing request and rows.
                if (_loadStarted) return;
                await StartLoadAsync();
            };
            UiTheme.Apply(this);
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceRaised;
            headerPanel.BackColor = palette.SurfaceMuted;
            panelMatches.BackColor = palette.Surface;
            lblName.ForeColor = palette.TextPrimary;
            _rankLabel.ForeColor = palette.Accent;
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
        private void RecalcHeaderLayout()
        {
            const int textLeft = 56;
            const int currentIconWidth = 36;
            const int tagWidth = 36;
            int premadeWidth = Math.Clamp(TextRenderer.MeasureText(lblPremadeTag.Text, lblPremadeTag.Font).Width + 10, 46, 104);
            const int gap = 6;

            int right = Math.Max(textLeft, ClientSize.Width - 12);
            int currentIconLeft = Math.Max(textLeft, right - currentIconWidth);
            picCurrent.Location = new Point(currentIconLeft, 4);
            picCurrent.Size = new Size(currentIconWidth, currentIconWidth);

            // 顶行优先保留段位和英雄头像；战绩汇总只使用玩家名称与段位之外的剩余空间。
            int summaryRight = picCurrent.Visible ? currentIconLeft - gap : right;
            int availableTopWidth = Math.Max(0, summaryRight - textLeft);
            int rankWidth = _rankLabel.Visible ? Math.Min(
                TextRenderer.MeasureText(_rankLabel.Text, _rankLabel.Font).Width + 4,
                Math.Max(0, availableTopWidth - 88 - gap)) : 0;
            int summaryWidth = Math.Min(135, Math.Max(0, availableTopWidth - 88 - rankWidth - 2 * gap));
            bool showSummary = summaryWidth >= 78;
            lblSummary.Visible = showSummary;
            if (showSummary)
            {
                lblSummary.Location = new Point(summaryRight - summaryWidth, 8);
                lblSummary.Width = summaryWidth;
            }

            int nameRight = showSummary ? summaryRight - summaryWidth - gap : summaryRight;
            lblName.Location = new Point(textLeft, 8);
            int availableNameWidth = Math.Max(0, nameRight - textLeft);
            lblName.Width = Math.Max(0, availableNameWidth - (rankWidth > 0 ? rankWidth + gap : 0));
            if (rankWidth > 0)
            {
                int actualNameWidth = Math.Min(lblName.Width, TextRenderer.MeasureText(lblName.Text, lblName.Font).Width);
                _rankLabel.Bounds = new Rectangle(textLeft + actualNameWidth + gap, 8, rankWidth, lblName.Height);
            }

            // 第二行将玩家信息、当前英雄和队伍标签按可用空间从左到右分配。
            // 第二行位于放大的英雄头像下方，可使用完整宽度展示玩家信息和队伍标签。
            int championRight = right;
            if (_teamKnown)
            {
                int teamTagLeft = championRight - tagWidth;
                lblTeamTag.Location = new Point(teamTagLeft, 40);
                lblTeamTag.Size = new Size(tagWidth, 20);
                championRight = teamTagLeft - gap;
            }
            if (lblPremadeTag.Visible)
            {
                int premadeTagLeft = championRight - premadeWidth;
                lblPremadeTag.Location = new Point(premadeTagLeft, 40);
                lblPremadeTag.Size = new Size(premadeWidth, 20);
                championRight = premadeTagLeft - gap;
            }

            int rowTwoSpace = Math.Max(0, championRight - textLeft);
            int subWidth = string.IsNullOrEmpty(lblChampionNow.Text)
                ? rowTwoSpace : Math.Min(160, Math.Max(0, rowTwoSpace - 116));
            int championLeft = textLeft + subWidth + gap;
            int championWidth = Math.Max(0, championRight - championLeft);
            bool showChampion = championWidth >= 70 && !string.IsNullOrEmpty(lblChampionNow.Text);

            lblSub.Location = new Point(textLeft, 40);
            lblSub.Width = showChampion ? subWidth : rowTwoSpace;
            lblChampionNow.Visible = showChampion;
            if (showChampion)
            {
                lblChampionNow.Location = new Point(championLeft, 40);
                lblChampionNow.Width = championWidth;
            }
        }

        /// <summary>按容器宽度更新近期战绩行的大小。</summary>
        private void ResizeMatchRows()
        {
            if (_layingOutMatchRows || IsDisposed) return;
            _layingOutMatchRows = true;
            try
            {
                MatchListScrolling.LayoutRows(panelMatches,
                    panelMatches.Controls.OfType<RecentMatchRow>().Cast<Control>().ToArray(), 4, 2);
            }
            finally { _layingOutMatchRows = false; }
        }

        /// <summary>展示玩家数据加载失败的状态。</summary>
        private void ShowLoadFailure(string status, string detail)
        {
            bool hasRows = panelMatches.Controls.OfType<RecentMatchRow>().Any();
            lblSummary.Text = hasRows
                ? (lblSummary.Text?.StartsWith("战绩加载中", StringComparison.Ordinal) == true ? "部分战绩已加载" : "评分暂不可用")
                : status;
            lblSummary.ForeColor = UiTheme.Palette.TextSecondary;
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

        private async Task StartLoadAsync()
        {
            if (_loading || IsDisposed) return;
            _loadStarted = true;
            _loading = true;
            _recentPerformancePublished = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(MaximumLoadDuration);
            try
            {
                await Task.WhenAll(LoadAsync(timeout.Token), LoadRankAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!_lifetimeCancellation.IsCancellationRequested && !IsDisposed)
                    ShowLoadFailure("获取超时", "对局数据获取超过 1 分钟，可重新加载。");
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
                if (!RankedCache.TryGetValue(_playerPuuid, out var cached) ||
                    DateTime.UtcNow - cached.CachedAt > PlayerCacheTtl || cached.Value.IsFaulted || cached.Value.IsCanceled)
                {
                    cached = (DateTime.UtcNow, _rankedStatsService.GetAsync(_playerPuuid));
                    RankedCache[_playerPuuid] = cached;
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
                _ = LoadHeaderAssetsAsync(0);
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
            // ── 近 10 场战绩 ──
            MatchHistoryResponse? matchlists;
            try
            {
                matchlists = await GetRecentHistoryAsync(_playerPuuid!, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (!IsDisposed) ShowLoadFailure("获取失败", $"战绩列表获取失败：{ex.Message}");
                return;
            }
            if (matchlists?.Games?.Games == null || IsDisposed)
            {
                if (!IsDisposed) ShowLoadFailure("获取失败", "战绩接口暂未返回数据，可重新加载。");
                return;
            }

            var games = matchlists.Games.Games
                .OrderByDescending(g => g.GameCreation)
                .Take(RecentGamesCount)
                .ToList();

            // 每位玩家只排入两个请求，避免首张卡片把全部全局槽位占满。
            var loaded = new (MatchDetail detail, MatchParticipant gamer)?[games.Count];
            var rows = new SortedDictionary<int, RecentMatchRow>();
            int nextIndex = 0;
            async Task LoadWorkerAsync()
            {
                while (nextIndex < games.Count)
                {
                    int index = nextIndex++;
                    var result = await LoadRecentDetailAsync(games[index].GameId, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsDisposed) return;
                    if (result == null) continue;
                    loaded[index] = result;
                    if (rows.Count == 0) ControlLifetime.ClearAndDispose(panelMatches);
                    var row = new RecentMatchRow
                    {
                        Width = Math.Max(100, panelMatches.ClientSize.Width - 18),
                        Height = RecentMatchRow.RowHeight
                    };
                    rows.Add(index, row);
                    panelMatches.Controls.Add(row);
                    // 返回顺序可能不同，始终按比赛时间排列，且保留现有行和滚动位置。
                    int order = 0;
                    foreach (var item in rows.Values) panelMatches.Controls.SetChildIndex(item, order++);
                    UiTheme.Apply(row);
                    _ = row.SetDataAsync(result.Value.detail, result.Value.gamer, _playerPuuid, championAndModeOnly: true);
                    ResizeMatchRows();
                    lblSummary.Text = $"战绩加载中 · {rows.Count}/{games.Count}";
                }
            }
            await Task.WhenAll(Enumerable.Range(0, DetailWorkersPerPlayer).Select(_ => LoadWorkerAsync()))
                .WaitAsync(cancellationToken);
            var results = loaded.Where(result => result.HasValue).Select(result => result!.Value).ToList();

            if (IsDisposed) return;

            int wins = results.Count(r => r.gamer.IsWin());
            int losses = results.Count - wins;
            double rate = results.Count > 0 ? Math.Round((double)wins / results.Count * 100, 1) : 0;
            if (results.Count == 0)
            {
                if (games.Count > 0)
                {
                    ShowLoadFailure("获取失败", "战绩列表已获取，但对局详情暂不可用，可重新加载。");
                    return;
                }
                ControlLifetime.ClearAndDispose(panelMatches);
                panelMatches.Controls.Add(new AntdUI.Label
                {
                    Text = "暂无战绩", AutoSize = true,
                    ForeColor = UiTheme.Palette.TextSecondary, Padding = new Padding(8)
                });
            }
            lblSummary.Text = "评分中";
            RecentModePerformanceAssessment assessment = await ApplyLivePerformanceTagAsync(
                matchlists, results, wins, losses, rate, cancellationToken);
            if (!IsDisposed) PublishRecentPerformance(assessment);
        }

        /// <summary>单场请求超时或失败最多重试一次，不让一场详情耗尽整张卡片的时间。</summary>
        private async Task<(MatchDetail detail, MatchParticipant gamer)?> LoadRecentDetailAsync(
            long gameId, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await GlobalMatchDetailLoadGate.WaitAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(DetailRequestTimeout);
                try
                {
                    var detail = await GetMatchDetailAsync(gameId, timeout.Token);
                    var gamer = detail?.GetParticipant(_playerPuuid);
                    if (detail != null && gamer != null) return (detail, gamer);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Report("对局详情", "重试", $"对局 {gameId}：{ex.Message}");
                }
                finally { GlobalMatchDetailLoadGate.Release(); }
            }
            return null;
        }

        private async Task LoadHeaderAssetsAsync(int profileIconId)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(MaximumLoadDuration);
            try
            {
                await Task.WhenAll(LoadProfileIconAsync(profileIconId, timeout.Token),
                    LoadCurrentChampionAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }

        /// <summary>读取指定玩家资料，供玩家卡片展示。</summary>
        private Task<PlayerProfile?> GetPlayerProfileAsync(string puuid, CancellationToken cancellationToken) =>
            GetCachedAsync(PlayerProfileCache, puuid,
                () => _playerProfileService.GetByPuuidAsync(puuid, cancellationToken)).WaitAsync(cancellationToken);

        /// <summary>读取指定玩家的近期战绩。</summary>
        private Task<MatchHistoryResponse?> GetRecentHistoryAsync(string puuid, CancellationToken cancellationToken) =>
            GetCachedAsync(RecentHistoryCache, puuid,
                () => _matchHistoryService.GetPageAsync(puuid, 0, HistoryFetchCount - 1, cancellationToken))
                .WaitAsync(cancellationToken);

        /// <summary>读取指定对局的完整详情。</summary>
        private Task<MatchDetail?> GetMatchDetailAsync(long gameId, CancellationToken cancellationToken) =>
            GetCachedAsync(MatchDetailCache, gameId,
                () => _matchHistoryService.GetDetailAsync(gameId, cancellationToken: cancellationToken))
                .WaitAsync(cancellationToken);

        /// <summary>优先使用缓存，并按需读取缺失数据。</summary>
        private static Task<T?> GetCachedAsync<TKey, T>(
            ConcurrentDictionary<TKey, (DateTime CachedAt, Task<T?> Value)> cache,
            TKey key,
            Func<Task<T?>> loader) where TKey : notnull
        {
            if (cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CachedAt < PlayerCacheTtl &&
                !cached.Value.IsFaulted && !cached.Value.IsCanceled &&
                !(cached.Value.IsCompletedSuccessfully && cached.Value.Result is null))
                return cached.Value;

            Task<T?> task = loader();
            DateTime cacheTime = DateTime.UtcNow;
            cache[key] = (cacheTime, task);
            _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted || completed.IsCanceled || completed.Result is null)
                    cache.TryRemove(new KeyValuePair<TKey, (DateTime CachedAt, Task<T?> Value)>(key, (cacheTime, task)));
            }, TaskScheduler.Default);
            return task;
        }

        /// <summary>评分读取最近 30 天全部同模式有效对局。</summary>
        private async Task<RecentModePerformanceAssessment> ApplyLivePerformanceTagAsync(
            MatchHistoryResponse firstPage,
            IReadOnlyList<(MatchDetail detail, MatchParticipant gamer)> results,
            int allWins,
            int allLosses,
            double allRate,
            CancellationToken cancellationToken)
        {
            var service = new RecentModePerformanceService(_matchHistoryService);
            RecentModePerformanceAssessment assessment = await service.EvaluateAsync(
                _playerPuuid!, _currentQueueId, _currentGameMode,
                LolGameModeNames.GetModeText(
                    _currentQueueId > 0 ? _currentQueueId.ToString() : "", _currentGameMode),
                firstPage, cancellationToken).WaitAsync(cancellationToken);

            string label = RecentPerformanceLabelFormatter.GetText(assessment);
            lblSummary.Text = assessment.SampleSize == 0 ? label : $"{label} · KDA {assessment.Kda:F2} · {assessment.SampleSize}场";
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
                $"最近 30 天同模式有效对局 {assessment.SampleSize} 场。" +
                (assessment.SampleSize == 0 ? $"暂不分档。{recentRecord}" : assessment.Detail));
            return assessment;
        }

        /// <summary>为样本不足的玩家构建明确的评估状态。</summary>
        private RecentModePerformanceAssessment CreateInsufficientPerformanceAssessment() =>
            RecentModePerformanceEvaluator.Evaluate(
                string.IsNullOrWhiteSpace(_currentGameMode) && _currentQueueId <= 0
                    ? "当前队列"
                    : LolGameModeNames.GetModeText(_currentQueueId > 0 ? _currentQueueId.ToString() : "", _currentGameMode),
                Array.Empty<MatchPerformanceAssessment>(),
                Array.Empty<bool>());

        /// <summary>通知外部订阅者当前玩家的近期表现结果。</summary>
        private void PublishRecentPerformance(RecentModePerformanceAssessment? assessment)
        {
            if (_recentPerformancePublished || IsDisposed || string.IsNullOrWhiteSpace(_playerPuuid)) return;
            _recentPerformancePublished = true;
            _cachedAssessment = assessment;
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
