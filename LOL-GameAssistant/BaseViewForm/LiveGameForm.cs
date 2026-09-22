using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using GameFlowPhase = LOL_GameAssistant.Domain.LeagueClient.GameFlowPhase;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局页：渐变信息栏（呼吸状态点）+ 蓝方/红方渐变队头 + 玩家卡片（展开动效）。
    /// </summary>
    public partial class LiveGameForm : UserControl, IThemeAware
    {
        private System.Windows.Forms.Timer? _autoRefreshTimer;
        private bool _refreshing;
        private string _lastSignature = "";
        private string? _myPuuid;
        private readonly ILobbyService _lobbyService;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IPremadeDetectionService _premadeDetectionService;
        private readonly IChampionSelectService _championSelectService;

        // 选人聊天只在一套确定的十人阵容全部完成近期战绩计算后发送一次，避免卡片异步完成时刷屏。
        private readonly Dictionary<string, PlayerRecentPerformanceEventArgs> _champSelectAssessments = new(StringComparer.Ordinal);
        private readonly HashSet<string> _expectedChampSelectAssessmentPuuids = new(StringComparer.Ordinal);
        private string _champSelectAssessmentSignature = "";
        private bool _champSelectAssessmentSent;

        // 缓存的是整局阵容的检测任务，而不是只记录“已经检测过”。这样强制刷新重建卡片后，
        // 已完成的结果能立即重新应用；尚在执行的任务也会被复用，不会重复拉取十人的近期战绩。
        private readonly Dictionary<string, Task<PremadeDetectionResult>> _premadeResultCache = new(StringComparer.Ordinal);

        private string _activePremadeCacheKey = "";
        private int _premadeCacheGeneration;
        private string _teamTitleBase1 = "蓝方";
        private string _teamTitleBase2 = "红方";
        private readonly AntdUI.Label _teamQueueTag1;
        private readonly AntdUI.Label _teamQueueTag2;
        private readonly ToolTip _teamQueueTip = new();
        private const int PlayerCardHeight = 470;
        private const int PlayerCardPreferredWidth = 450;
        private const int PlayerCardSingleColumnMaxWidth = 640;
        private const int PlayerCardMinimumWidth = 360;
        private const int PlayerCardHorizontalMargin = 10;
        private const int PlayerCardVerticalMargin = 10;

        public LiveGameForm() : this(
            AppCompositionRoot.LobbyService,
            AppCompositionRoot.PlayerProfileService,
            AppCompositionRoot.PremadeDetectionService,
            AppCompositionRoot.ChampionSelectService)
        {
        }

        /// <summary>实时对局页通过应用服务读取游戏流程与当前玩家资料。</summary>
        internal LiveGameForm(
            ILobbyService lobbyService,
            IPlayerProfileService playerProfileService,
            IPremadeDetectionService premadeDetectionService,
            IChampionSelectService championSelectService)
        {
            _lobbyService = lobbyService;
            _playerProfileService = playerProfileService;
            _premadeDetectionService = premadeDetectionService;
            _championSelectService = championSelectService;
            InitializeComponent();
            _teamQueueTag1 = CreateTeamQueueTag();
            _teamQueueTag2 = CreateTeamQueueTag();
            headerTeam1.Controls.Add(_teamQueueTag1);
            headerTeam2.Controls.Add(_teamQueueTag2);
            _teamQueueTag1.BringToFront();
            _teamQueueTag2.BringToFront();
            headerTeam1.Resize += (_, _) => LayoutTeamQueueTags();
            headerTeam2.Resize += (_, _) => LayoutTeamQueueTags();
            this.Load += LiveGameForm_Load;
            Resize += (_, _) => LayoutPlayerCards();
            this.Disposed += (_, _) =>
            {
                _autoRefreshTimer?.Dispose();
                _teamQueueTip.Dispose();
            };
            LayoutTeamQueueTags();
            UiTheme.Apply(this);
        }

        private void LiveGameForm_Load(object? sender, EventArgs e)
        {
            lblGameInfo.Text = "暂无对局信息，进入对局后自动展示";
        }

        private static AntdUI.Label CreateTeamQueueTag()
        {
            return new AntdUI.Label
            {
                AutoSize = false,
                BackColor = Color.FromArgb(238, 238, 238),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 90, 90),
                Text = "检测中",
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
        }

        /// <summary>Keep the team headers readable in both palettes; transparent headers previously rendered white text on a light surface.</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Surface;
            rootGrid.BackColor = palette.Surface;
            column1.BackColor = palette.Surface;
            column2.BackColor = palette.Surface;
            panelTeam1.BackColor = palette.SurfaceMuted;
            panelTeam2.BackColor = palette.SurfaceMuted;
            infoBar.StartColor = palette.SurfaceRaised;
            infoBar.EndColor = palette.SurfaceMuted;
            infoBar.BorderColor = palette.Border;
            headerTeam1.StartColor = palette.BlueHeader;
            headerTeam1.EndColor = ControlPaint.Dark(palette.BlueHeader, .12f);
            headerTeam1.BorderColor = Color.FromArgb(120, palette.BlueHeader);
            headerTeam2.StartColor = palette.RedHeader;
            headerTeam2.EndColor = ControlPaint.Dark(palette.RedHeader, .12f);
            headerTeam2.BorderColor = Color.FromArgb(120, palette.RedHeader);
            lblTeamTitle1.ForeColor = Color.White;
            lblTeamTitle2.ForeColor = Color.White;
            lblGameInfo.ForeColor = palette.TextPrimary;
        }

        private void LayoutTeamQueueTags()
        {
            LayoutTeamQueueTag(headerTeam1, lblTeamTitle1, _teamQueueTag1);
            LayoutTeamQueueTag(headerTeam2, lblTeamTitle2, _teamQueueTag2);
        }

        private static void LayoutTeamQueueTag(GradientPanel header, Label title, AntdUI.Label tag)
        {
            if (header.ClientSize.Width <= 0) return;

            int width = Math.Clamp(TextRenderer.MeasureText(tag.Text, tag.Font).Width + 16, 48, 132);
            tag.Size = new Size(width, 22);
            tag.Location = new Point(Math.Max(4, header.ClientSize.Width - width - 8), 6);
            title.Padding = new Padding(0, 0, width + 16, 0);
        }

        private void SetTeamQueueTag(AntdUI.Label tag, string status, string detail)
        {
            ThemePalette palette = UiTheme.Palette;
            tag.Text = status;
            (tag.BackColor, tag.ForeColor) = status switch
            {
                "单排" => (palette.IsDark ? palette.SurfaceMuted : Color.FromArgb(238, 238, 238), palette.TextPrimary),
                "检测中" => (palette.IsDark ? Color.FromArgb(25, 64, 94) : Color.FromArgb(227, 242, 253), palette.IsDark ? Color.FromArgb(144, 202, 249) : Color.FromArgb(25, 118, 210)),
                "未知" => (palette.IsDark ? Color.FromArgb(83, 58, 22) : Color.FromArgb(255, 243, 224), palette.IsDark ? Color.FromArgb(255, 204, 128) : Color.FromArgb(191, 104, 0)),
                _ => (palette.IsDark ? Color.FromArgb(84, 65, 17) : Color.FromArgb(255, 236, 179), palette.IsDark ? Color.FromArgb(255, 213, 79) : Color.FromArgb(148, 96, 0))
            };
            tag.Visible = true;
            _teamQueueTip.SetToolTip(tag, $"{status}：{detail}");
            LayoutTeamQueueTags();
        }

        /// <summary>
        /// 配置对局数据自动刷新（由设置模块调用）。
        /// </summary>
        public void ConfigureAutoRefresh(bool enabled, int seconds)
        {
            if (_autoRefreshTimer == null)
            {
                _autoRefreshTimer = new System.Windows.Forms.Timer();
                _autoRefreshTimer.Tick += (_, _) => AutoRefreshTick();
            }

            _autoRefreshTimer.Interval = Math.Max(10, seconds) * 1000;
            _autoRefreshTimer.Enabled = enabled;
        }

        private async void AutoRefreshTick()
        {
            // 仅在对局标签页可见时刷新，避免后台频繁请求
            if (Program.GameMain.IsLiveGameTabActive)
            {
                await AddView();
            }
        }

        /// <summary>
        /// 清空阵容缓存，下一次刷新强制重建（游戏结束后调用）。
        /// </summary>
        public void ResetRosterCache()
        {
            _lastSignature = "";
            ResetChampSelectAssessments();
            _activePremadeCacheKey = "";
            _premadeResultCache.Clear();
            unchecked { _premadeCacheGeneration++; }
            _teamQueueTag1.Visible = false;
            _teamQueueTag2.Visible = false;
        }

        /// <summary>保存选人 KDA 公告设置后重新读取当前阵容，让新开关立即生效。</summary>
        public void RefreshChampSelectKdaAnnouncement()
        {
            if (IsDisposed || GameMain.gameFlowPhase != GameFlowPhase.ChampSelect) return;
            _ = AddView(force: true);
        }

        /// <summary>
        /// 刷新对局信息；<paramref name="force"/> 为 true 时强制重建玩家卡片。
        /// </summary>
        public async Task AddView(bool force = false)
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                // 缓存的 gameFlowPhase 只由 WebSocket 事件驱动，而 LCU 订阅时不会补发当前阶段，
                // 所以启动/重连时若已经在大厅或对局中，这个值会停在默认值，
                // 表现就是"点刷新没反应"。用户主动刷新、或当前阶段不可渲染时，主动问一次 LCU。
                var phase = GameMain.gameFlowPhase;
                if (force || !IsRenderablePhase(phase))
                {
                    string? livePhase = await _lobbyService.GetGameFlowPhaseAsync();
                    Program.GameMain?.ApplyGameFlowPhase(livePhase);
                    if (Enum.TryParse(livePhase, true, out GameFlowPhase parsed)) phase = parsed;
                }

                if (phase == GameFlowPhase.ChampSelect)
                {
                    LobbySnapshot? gameInfo = await _lobbyService.GetLobbyAsync();
                    ChampionSelectionSnapshot? selection = await _championSelectService.GetSessionAsync();
                    if (selection != null)
                    {
                        string? selectionMyPuuid = selection.MyTeam
                            .FirstOrDefault(member => member.CellId == selection.LocalPlayerCellId)?.Puuid;
                        selectionMyPuuid ??= gameInfo?.LocalPlayerPuuid;
                        selectionMyPuuid ??= await GetMyPuuidAsync();
                        string mode = gameInfo?.GameMode ?? "选人阶段";
                        int queueId = gameInfo?.QueueId ?? 0;
                        SetGameInfo(mode, queueId);
                        RenderTeamsCore(
                            selection.MyTeam.Select(member => (
                                member.Puuid,
                                string.IsNullOrWhiteSpace(member.Puuid) ? $"玩家 {member.CellId}" : "加载玩家信息…",
                                member.ChampionId,
                                member.AssignedPosition,
                                false)).ToList(),
                            selection.TheirTeam.Select(member => (
                                member.Puuid,
                                string.IsNullOrWhiteSpace(member.Puuid) ? $"玩家 {member.CellId}" : "加载玩家信息…",
                                member.ChampionId,
                                member.AssignedPosition,
                                false)).ToList(),
                            force,
                            selectionMyPuuid,
                            queueId,
                            mode);
                        return;
                    }

                    // 极短暂的选人会话切换期可能拿不到 session，继续用大厅数据兜底展示。
                    if (gameInfo == null)
                    {
                        lblGameInfo.Text = "未获取到选人阵容";
                        return;
                    }

                    SetGameInfo(gameInfo.GameMode, gameInfo.QueueId);
                    // 大厅/选人阶段优先取本地成员 puuid，判断我方队伍
                    string? myPuuid = string.IsNullOrEmpty(gameInfo.LocalPlayerPuuid)
                        ? await GetMyPuuidAsync()
                        : gameInfo.LocalPlayerPuuid;
                    RenderTeams(
                        gameInfo.Team100,
                        gameInfo.Team200,
                        force,
                        myPuuid,
                        gameInfo.QueueId,
                        gameInfo.GameMode);
                }
                else if (phase == GameFlowPhase.Lobby)
                {
                    LobbySnapshot? gameInfo = await _lobbyService.GetLobbyAsync();
                    if (gameInfo == null)
                    {
                        lblGameInfo.Text = "未获取到大厅信息";
                        return;
                    }

                    SetGameInfo(gameInfo.GameMode, gameInfo.QueueId);
                    string? myPuuid = string.IsNullOrEmpty(gameInfo.LocalPlayerPuuid)
                        ? await GetMyPuuidAsync()
                        : gameInfo.LocalPlayerPuuid;
                    RenderTeams(
                        gameInfo.Team100,
                        gameInfo.Team200,
                        force,
                        myPuuid,
                        gameInfo.QueueId,
                        gameInfo.GameMode);
                }
                else if (phase == GameFlowPhase.InProgress)
                {
                    ActiveGameSnapshot? session = await _lobbyService.GetCurrentSessionAsync();
                    if (session == null)
                    {
                        lblGameInfo.Text = "未获取到对局信息";
                        return;
                    }

                    string liveGameMode = await AppCompositionRoot.LiveClientGameStateService.GetGameModeAsync() ?? "";
                    SetGameInfo(liveGameMode, 0);
                    // 对局中通过当前召唤师接口获取 puuid
                    string? myPuuid = await GetMyPuuidAsync();
                    RenderTeams(
                        session.TeamOne,
                        session.TeamTwo,
                        force,
                        myPuuid,
                        0,
                        liveGameMode);
                }
                else
                {
                    // 当前阶段拿不到阵容（匹配中、结算中、空闲等）。
                    // 至少要给出阶段提示，否则点了刷新界面毫无变化，看起来就像按钮坏了。
                    if (panelTeam1.Controls.Count == 0 && panelTeam2.Controls.Count == 0)
                    {
                        lblGameInfo.Text = $"{phase.GetChineseName()} · 当前阶段暂无对局数据";
                    }
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>
        /// 该阶段能否拿到阵容数据（其余阶段如匹配中/结算中/空闲都取不到）。
        /// </summary>
        private static bool IsRenderablePhase(GameFlowPhase phase) =>
            phase is GameFlowPhase.Lobby or GameFlowPhase.ChampSelect or GameFlowPhase.InProgress;

        /// <summary>
        /// 获取当前登录召唤师的 puuid（带缓存，用于判断队友/对手）。
        /// </summary>
        private async Task<string?> GetMyPuuidAsync()
        {
            if (!string.IsNullOrEmpty(_myPuuid)) return _myPuuid;
            try
            {
                _myPuuid = (await _playerProfileService.GetCurrentAsync())?.Puuid;
            }
            catch
            {
                // 获取失败时按未知队伍处理，不阻塞对局展示
            }
            return _myPuuid;
        }

        private void SetGameInfo(string mode, int queueId)
        {
            string phase = GameMain.gameFlowPhase.GetChineseName();
            string modeText = string.IsNullOrEmpty(mode) ? "" : $" · 模式: {mode}";
            string queueText = queueId > 0 ? $" · 队列: {queueId}" : "";
            lblGameInfo.Text = $"{phase}{modeText}{queueText}";
        }

        private void RenderTeams(
            IReadOnlyList<GameTeamMember> team1,
            IReadOnlyList<GameTeamMember> team2,
            bool force,
            string? myPuuid,
            int queueId,
            string? gameMode)
        {
            RenderTeamsCore(
                team1.Select(m => (m.Puuid, m.SummonerName, m.ChampionId, m.Position, m.IsBot)).ToList(),
                team2.Select(m => (m.Puuid, m.SummonerName, m.ChampionId, m.Position, m.IsBot)).ToList(),
                force,
                myPuuid,
                queueId,
                gameMode);
        }

        private void RenderTeamsCore(
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            bool force,
            string? myPuuid,
            int queueId,
            string? gameMode)
        {
            // 必须保留队伍归属：同一批玩家换边时，旧的开黑小组不能直接套用。
            // 不包含队列/模式，确保选人阶段进入游戏内时仍然沿用本局已经得到的结果。
            string signature = BuildPremadeCacheKey(team1, team2);

            // 阵容未变化时跳过重建，避免自动刷新反复销毁/重建控件
            if (!force && signature == _lastSignature && panelTeam1.Controls.Count > 0)
                return;
            _lastSignature = signature;
            PrepareChampSelectAssessments(signature, team1, team2, myPuuid);
            _activePremadeCacheKey = signature;
            int cacheGeneration = _premadeCacheGeneration;

            int count1 = team1.Count(m => !string.IsNullOrWhiteSpace(m.Puuid) || m.IsBot);
            int count2 = team2.Count(m => !string.IsNullOrWhiteSpace(m.Puuid) || m.IsBot);

            // 判断哪一队是我方，用于表头“我方/敌方”标识
            bool team1Mine = myPuuid != null && team1.Any(m => m.Puuid == myPuuid);
            bool team2Mine = myPuuid != null && team2.Any(m => m.Puuid == myPuuid);
            bool anyMine = team1Mine || team2Mine;
            string suffix1 = team1Mine ? " · 我方" : (anyMine ? " · 敌方" : "");
            string suffix2 = team2Mine ? " · 我方" : (anyMine ? " · 敌方" : "");
            lblTeamTitle1.Text = $"蓝方 ({count1}){suffix1}";
            lblTeamTitle2.Text = $"红方 ({count2}){suffix2}";
            SetTeamQueueTag(_teamQueueTag1, "检测中", "正在根据近期同队记录识别队伍类型");
            SetTeamQueueTag(_teamQueueTag2, "检测中", "正在根据近期同队记录识别队伍类型");

            panelTeam1.SuspendLayout();
            panelTeam2.SuspendLayout();
            try
            {
                // 必须 Dispose 而不是只 Clear：Controls.Clear() 只解除父子关系，
                // 卡片自带的 ToolTip（每个都是一份 native 窗口）、发光定时器、头像图片
                // 以及卡片自身的窗口句柄都会残留。自动刷新每 30 秒重建一次整组卡片，
                // 累积到进程 USER 对象上限后，程序就再也创建不了窗口（"创建窗口句柄时出错"）。
                DisposeChildren(panelTeam1);
                DisposeChildren(panelTeam2);

                AddPlayerCards(panelTeam1, team1, myPuuid, team1Mine, queueId, gameMode);
                AddPlayerCards(panelTeam2, team2, myPuuid, team2Mine, queueId, gameMode);
            }
            finally
            {
                panelTeam1.ResumeLayout();
                panelTeam2.ResumeLayout();
            }

            LayoutPlayerCards();

            // 卡片展开动效（交错延迟）
            int index = 0;
            foreach (Control card in panelTeam1.Controls)
            {
                if (card is LivePlayerForm)
                    UiAnimation.ExpandIn(card, 0, 300, index++ * 60);
            }
            foreach (Control card in panelTeam2.Controls)
            {
                if (card is LivePlayerForm)
                    UiAnimation.ExpandIn(card, 0, 300, index++ * 60);
            }

            _teamTitleBase1 = lblTeamTitle1.Text;
            _teamTitleBase2 = lblTeamTitle2.Text;
            _ = ApplyPremadeDetectionAsync(team1, team2, signature, cacheGeneration);
        }

        /// <summary>
        /// 生成本局开黑检测缓存键。蓝红两队分别排序，既能稳定命中缓存，又不会把换边阵容误判为同一局。
        /// </summary>
        private static string BuildPremadeCacheKey(
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2)
        {
            static string TeamKey(IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team) =>
                string.Join(",", team
                    .Select(member => member.Puuid)
                    .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
                    .OrderBy(puuid => puuid, StringComparer.Ordinal));

            return $"blue:{TeamKey(team1)}|red:{TeamKey(team2)}";
        }

        /// <summary>
        /// 按当前选人阵容建立一次性公告的等待清单，只登记我方玩家：
        /// 敌方战绩照常显示在卡片上，但不参与公告汇总，公告也不再等他们。
        /// 阵容未变时保留已完成结果，强制刷新不会重复发送；阵容变更时才丢弃上一套结果。
        /// </summary>
        private void PrepareChampSelectAssessments(
            string signature,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            string? myPuuid)
        {
            bool enabled = GameMain.gameFlowPhase == GameFlowPhase.ChampSelect &&
                AppCompositionRoot.ApplicationSettingsStore.Load().ChampSelectKdaAnnouncementEnabled;
            if (!enabled)
            {
                ResetChampSelectAssessments();
                return;
            }

            if (string.Equals(signature, _champSelectAssessmentSignature, StringComparison.Ordinal)) return;

            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> allyTeam =
                myPuuid != null && team1.Any(member => member.Puuid == myPuuid)
                    ? team1
                    : myPuuid != null && team2.Any(member => member.Puuid == myPuuid)
                        ? team2
                        : Enumerable.Empty<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)>();

            _champSelectAssessmentSignature = signature;
            _champSelectAssessmentSent = false;
            _champSelectAssessments.Clear();
            _expectedChampSelectAssessmentPuuids.Clear();
            foreach (string puuid in allyTeam
                         .Where(member => !member.IsBot)
                         .Select(member => member.Puuid)
                         .Where(puuid => !string.IsNullOrWhiteSpace(puuid)))
            {
                _expectedChampSelectAssessmentPuuids.Add(puuid);
            }
        }

        private void ResetChampSelectAssessments()
        {
            _champSelectAssessments.Clear();
            _expectedChampSelectAssessmentPuuids.Clear();
            _champSelectAssessmentSignature = "";
            _champSelectAssessmentSent = false;
        }

        private void OnPlayerRecentPerformanceReady(object? sender, PlayerRecentPerformanceEventArgs result)
        {
            if (IsDisposed || _champSelectAssessmentSent ||
                GameMain.gameFlowPhase != GameFlowPhase.ChampSelect ||
                !_expectedChampSelectAssessmentPuuids.Contains(result.Puuid)) return;

            _champSelectAssessments[result.Puuid] = result;
            // 敌方卡片也会发布结果，因此必须按我方名单逐个确认，不能用收到的条数判断。
            if (_expectedChampSelectAssessmentPuuids.Count == 0 ||
                !_expectedChampSelectAssessmentPuuids.All(puuid => _champSelectAssessments.ContainsKey(puuid))) return;

            string signature = _champSelectAssessmentSignature;
            string message = BuildChampSelectKdaAnnouncement();
            if (string.IsNullOrWhiteSpace(message)) return;

            // 先锁住本局，LCU 故障也不能在每次自动刷新时重复尝试、污染选人聊天。
            _champSelectAssessmentSent = true;
            _ = SendChampSelectKdaAnnouncementAsync(signature, message);
        }

        private string BuildChampSelectKdaAnnouncement()
        {
            var settings = AppCompositionRoot.ApplicationSettingsStore.Load();
            if (!settings.ChampSelectKdaAnnouncementEnabled) return "";

            var players = _champSelectAssessments.Values
                .Where(item => item.IsAlly)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string roster = string.Join("\n", players.Select(FormatChampSelectPerformance));
            string message = RemoveEnemyPlaceholder(settings.ChampSelectKdaAnnouncementTemplate)
                .Replace("{players}", roster, StringComparison.OrdinalIgnoreCase)
                .Replace("{allies}", string.IsNullOrWhiteSpace(roster) ? "我方：暂未获取" : $"我方：\n{roster}", StringComparison.OrdinalIgnoreCase)
                .Trim();
            return message.Length <= 1200 ? message : message[..1200];
        }

        /// <summary>
        /// 公告不再包含敌方，因此保存过的旧模板里 {enemies} 连同它独占的那一行一起删掉，
        /// 否则聊天里会留下一行空白。
        /// </summary>
        private static string RemoveEnemyPlaceholder(string template) => template
            .Replace("\r\n{enemies}", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\n{enemies}", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{enemies}\r\n", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{enemies}\n", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{enemies}", "", StringComparison.OrdinalIgnoreCase);

        private static string FormatChampSelectPerformance(PlayerRecentPerformanceEventArgs result)
        {
            RecentModePerformanceAssessment assessment = result.Assessment;
            string name = string.IsNullOrWhiteSpace(result.DisplayName) ? "未知玩家" : result.DisplayName.Trim();
            return $"{name}：{RecentPerformanceLabelFormatter.GetText(assessment)} {assessment.Score}分 · KDA {assessment.Kda:F2} · 胜率 {assessment.WinRate:F0}%";
        }

        private async Task SendChampSelectKdaAnnouncementAsync(string signature, string message)
        {
            var result = await AppCompositionRoot.ChampionSelectChatService.SendAsync(message);
            if (IsDisposed || signature != _champSelectAssessmentSignature) return;

            string summary = result.Succeeded
                ? "选人近期 KDA 评估已发送到聊天窗口。"
                : $"选人近期 KDA 评估未发送：{result.Message}";
            GameMain.infoMsg.AddMsg(summary);
        }

        /// <summary>
        /// 异步执行开黑检测：拉取每人近期战绩，统计同队次数后更新表头与卡片标记。
        /// 当前对局未结束时按阵容复用检测任务/结果，强制刷新只重绘界面，不重复发起检测。
        /// </summary>
        private async Task ApplyPremadeDetectionAsync(
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            string cacheKey,
            int cacheGeneration)
        {
            Task<PremadeDetectionResult> resultTask;
            if (!_premadeResultCache.TryGetValue(cacheKey, out resultTask!))
            {
                resultTask = _premadeDetectionService.DetectAsync(
                    team1.Select(member => new TeamMemberIdentity(member.Puuid, member.Name)).ToArray(),
                    team2.Select(member => new TeamMemberIdentity(member.Puuid, member.Name)).ToArray());
                _premadeResultCache[cacheKey] = resultTask;
            }

            try
            {
                var result = await resultTask;

                // 等待期间阵容/对局已变化则丢弃本次结果。
                // generation 防止上一局的异步任务在新一局恰好遇到相同阵容时误回填。
                if (IsDisposed || cacheGeneration != _premadeCacheGeneration ||
                    cacheKey != _activePremadeCacheKey) return;
                ApplyPremadeResult(result);
            }
            catch
            {
                // 失败结果不缓存，当前局下一次刷新可以重新尝试；但不要删除已经属于新一局的任务。
                if (_premadeResultCache.TryGetValue(cacheKey, out var cachedTask) &&
                    ReferenceEquals(cachedTask, resultTask))
                {
                    _premadeResultCache.Remove(cacheKey);
                }

                if (!IsDisposed && cacheGeneration == _premadeCacheGeneration &&
                    cacheKey == _activePremadeCacheKey)
                {
                    SetTeamQueueTag(_teamQueueTag1, "未知", "组队检测暂不可用");
                    SetTeamQueueTag(_teamQueueTag2, "未知", "组队检测暂不可用");
                }
            }
        }

        /// <summary>
        /// 将开黑检测结果应用到表头与玩家卡片。
        /// </summary>
        private void ApplyPremadeResult(PremadeDetectionResult result)
        {
            string summary1 = result.GetTeamSummary(0);
            string summary2 = result.GetTeamSummary(1);
            lblTeamTitle1.Text = string.IsNullOrEmpty(summary1)
                ? _teamTitleBase1
                : $"{_teamTitleBase1} · 开黑 {summary1}";
            lblTeamTitle2.Text = string.IsNullOrEmpty(summary2)
                ? _teamTitleBase2
                : $"{_teamTitleBase2} · 开黑 {summary2}";

            SetTeamQueueTag(_teamQueueTag1, result.GetTeamQueueStatus(0), result.GetTeamQueueDetail(0));
            SetTeamQueueTag(_teamQueueTag2, result.GetTeamQueueStatus(1), result.GetTeamQueueDetail(1));

            ApplyPremadeToPanel(panelTeam1, result);
            ApplyPremadeToPanel(panelTeam2, result);
        }

        /// <summary>
        /// 给单个队伍面板中的卡片设置/清除开黑标记。
        /// </summary>
        private static void ApplyPremadeToPanel(
            Control panel,
            PremadeDetectionResult result)
        {
            foreach (Control card in panel.Controls)
            {
                if (card is not LivePlayerForm player || player.Puuid == null) continue;
                var group = result.GroupByPuuid.GetValueOrDefault(player.Puuid);
                player.SetPremadeGroup(group?.Index, group?.Names?.ToList());
            }
        }

        /// <summary>
        /// 按队伍面板的实际可用宽度重排卡片：宽屏使用两列，空间不足时改为舒展的单列。
        /// 滚动条和卡片外边距均纳入计算，避免临界宽度下第二张卡片错误换行。
        /// </summary>
        private void LayoutPlayerCards()
        {
            if (IsDisposed) return;
            ResizePlayerCards(panelTeam1);
            ResizePlayerCards(panelTeam2);
        }

        private static void ResizePlayerCards(FlowLayoutPanel panel)
        {
            var cards = panel.Controls.OfType<LivePlayerForm>().ToList();
            if (cards.Count == 0 || panel.ClientSize.Width <= 0) return;

            int width = CalculatePlayerCardWidth(panel, cards.Count);
            panel.SuspendLayout();
            try
            {
                foreach (var card in cards)
                {
                    card.Width = width;
                    card.Height = PlayerCardHeight;
                }
            }
            finally
            {
                panel.ResumeLayout(true);
            }
        }

        private static int CalculatePlayerCardWidth(FlowLayoutPanel panel, int cardCount)
        {
            int contentWidth = panel.ClientSize.Width - panel.Padding.Horizontal;
            if (contentWidth <= 0) return PlayerCardMinimumWidth;

            // 优先尝试两列；若对局记录区域会产生纵向滚动条，预留其宽度。
            int twoColumnRows = (cardCount + 1) / 2;
            int twoColumnScrollbar = NeedsVerticalScrollbar(panel, twoColumnRows)
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int twoColumnWidth = (contentWidth - twoColumnScrollbar - 2 * PlayerCardHorizontalMargin) / 2;
            if (cardCount > 1 && twoColumnWidth >= PlayerCardMinimumWidth)
            {
                return Math.Min(PlayerCardPreferredWidth, twoColumnWidth);
            }

            // 单列时让卡片填充所在队列，避免旧逻辑在窗口变宽后仍停留在固定 450px。
            int singleColumnScrollbar = NeedsVerticalScrollbar(panel, cardCount)
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int singleColumnWidth = contentWidth - singleColumnScrollbar - PlayerCardHorizontalMargin;
            return Math.Max(PlayerCardMinimumWidth, Math.Min(PlayerCardSingleColumnMaxWidth, singleColumnWidth));
        }

        private static bool NeedsVerticalScrollbar(FlowLayoutPanel panel, int rows)
        {
            int contentHeight = panel.Padding.Vertical + rows * (PlayerCardHeight + PlayerCardVerticalMargin);
            return contentHeight > panel.ClientSize.Height;
        }

        /// <summary>
        /// 释放面板中的旧卡片：只 Clear 不 Dispose 会让卡片连同 ToolTip、定时器、
        /// 头像图片和窗口句柄一起泄漏，重建次数多了就会耗光窗口句柄。
        /// </summary>
        private static void DisposeChildren(Control parent)
        {
            var children = parent.Controls.Cast<Control>().ToArray();
            parent.Controls.Clear();
            foreach (var child in children)
            {
                child.Dispose();
            }
        }

        private void AddPlayerCards(
            FlowLayoutPanel panel,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> members,
            string? myPuuid,
            bool teamIsAlly,
            int currentQueueId,
            string? currentGameMode)
        {
            if (members.Count == 0)
            {
                panel.Controls.Add(new AntdUI.Label
                {
                    Text = "暂无玩家信息",
                    AutoSize = true,
                    Padding = new Padding(8)
                });
                return;
            }

            foreach (var member in members)
            {
                // 同一队 = 我方；未知我方 puuid 时不显示队友/对手标识
                bool isAlly = teamIsAlly;
                bool teamKnown = myPuuid != null;
                var card = new LivePlayerForm(
                    member.Puuid,
                    member.Name,
                    member.ChampionId,
                    member.Position,
                    member.IsBot,
                    isAlly,
                    teamKnown,
                    currentQueueId,
                    currentGameMode)
                {
                    Width = PlayerCardPreferredWidth,
                    Height = PlayerCardHeight,
                    Margin = new Padding(0, 0, PlayerCardHorizontalMargin, PlayerCardVerticalMargin)
                };
                UiTheme.Apply(card);
                card.RecentPerformanceReady += OnPlayerRecentPerformanceReady;
                panel.Controls.Add(card);
            }
        }
    }
}
