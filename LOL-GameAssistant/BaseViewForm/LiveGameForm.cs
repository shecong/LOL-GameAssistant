using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.LiveGame;
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
        private bool _layingOutPlayerCards;
        private string _lastSignature = "";
        private string? _myPuuid;
        private readonly ILobbyService _lobbyService;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IChampionSelectService _championSelectService;

        // 选人聊天只在一套确定的十人阵容全部完成近期战绩计算后发送一次，避免卡片异步完成时刷屏。
        private readonly Dictionary<string, PlayerRecentPerformanceEventArgs> _champSelectAssessments = new(StringComparer.Ordinal);

        private readonly HashSet<string> _expectedChampSelectAssessmentPuuids = new(StringComparer.Ordinal);
        private string _champSelectAssessmentSignature = "";
        private bool _champSelectAssessmentSent;

        private readonly Dictionary<string, PlayerRecentPerformanceEventArgs> _gameAssessments = new(StringComparer.Ordinal);
        private readonly HashSet<string> _expectedGameAssessmentPuuids = new(StringComparer.Ordinal);
        private readonly List<(string Puuid, string Name, string Team)> _gameAssessmentRoster = new();
        private string _gameAssessmentSignature = "";
        private bool _gameAssessmentSent;
        private bool _gameAssessmentSending;
        private int _gameAssessmentGeneration;
        private GameFlowPhase? _lastRenderedPhase;

        public bool NeedsGameAssessmentRoster =>
            !IsDisposed && Program.GameMain.gameFlowPhase == GameFlowPhase.InProgress &&
            _gameAssessmentRoster.Count == 0;

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
            AppCompositionRoot.ChampionSelectService)
        {
        }

        /// <summary>实时对局页通过应用服务读取游戏流程与当前玩家资料。</summary>
        internal LiveGameForm(
            ILobbyService lobbyService,
            IPlayerProfileService playerProfileService,
            IChampionSelectService championSelectService)
        {
            _lobbyService = lobbyService;
            _playerProfileService = playerProfileService;
            _championSelectService = championSelectService;
            InitializeComponent();
            var backToTop = new AntdUI.Button { Text = "回到顶部", Dock = DockStyle.Right, Width = 96 };
            backToTop.Click += (_, _) =>
            {
                panelTeam1.AutoScrollPosition = Point.Empty;
                panelTeam2.AutoScrollPosition = Point.Empty;
                foreach (LivePlayerForm card in panelTeam1.Controls.OfType<LivePlayerForm>()
                    .Concat(panelTeam2.Controls.OfType<LivePlayerForm>())) card.ScrollMatchesToTop();
            };
            lblGameInfo.AutoSize = false;
            lblGameInfo.Dock = DockStyle.Fill;
            lblGameInfo.TextAlign = ContentAlignment.MiddleLeft;
            infoBar.Controls.Add(backToTop);
            backToTop.BringToFront();
            panelTeam1.SizeChanged += (_, _) => LayoutPlayerCards();
            panelTeam2.SizeChanged += (_, _) => LayoutPlayerCards();
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

        private static void LayoutTeamQueueTag(GradientPanel header, AntdUI.Label title, AntdUI.Label tag)
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
                "未见组队" => (palette.IsDark ? palette.SurfaceMuted : Color.FromArgb(238, 238, 238), palette.TextPrimary),
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
            ResetGameAssessments();
            _lastRenderedPhase = null;
            _teamQueueTag1.Visible = false;
            _teamQueueTag2.Visible = false;
        }

        /// <summary>保存 KDA 发送设置后重新读取当前阵容，让选人和对局开关立即生效。</summary>
        public void RefreshKdaAnnouncements()
        {
            if (IsDisposed || Program.GameMain.gameFlowPhase is not (GameFlowPhase.ChampSelect or GameFlowPhase.InProgress)) return;
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
                var phase = Program.GameMain.gameFlowPhase;
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
                        ActiveGameSnapshot? flow = null;
                        // 选人阶段也尝试取得双方的组队标识；失败仍可显示选人阵容。
                        try { flow = await _lobbyService.GetCurrentSessionAsync(); }
                        catch { /* 会话切换时仍可使用大厅信息。 */ }
                        string mode = !string.IsNullOrWhiteSpace(gameInfo?.GameMode)
                            ? gameInfo.GameMode : flow?.GameMode ?? "选人阶段";
                        int queueId = gameInfo?.QueueId is > 0 ? gameInfo.QueueId : flow?.QueueId ?? 0;
                        SetGameInfo(mode, queueId);
                        RenderTeams(
                            MapSelectionMembers(selection.MyTeam, flow, gameInfo),
                            MapSelectionMembers(selection.TheirTeam, flow, gameInfo),
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
                    string currentMode = string.IsNullOrWhiteSpace(liveGameMode) ? session.GameMode : liveGameMode;
                    SetGameInfo(currentMode, session.QueueId);
                    // 对局中通过当前召唤师接口获取 puuid
                    string? myPuuid = await GetMyPuuidAsync();
                    RenderTeams(
                        session.TeamOne,
                        session.TeamTwo,
                        force,
                        myPuuid,
                        session.QueueId,
                        currentMode);
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

        private static IReadOnlyList<GameTeamMember> MapSelectionMembers(
            IReadOnlyList<ChampionSelectionMember> members,
            ActiveGameSnapshot? flow,
            LobbySnapshot? lobby)
        {
            var flowByPuuid = (flow?.TeamOne ?? Array.Empty<GameTeamMember>())
                .Concat(flow?.TeamTwo ?? Array.Empty<GameTeamMember>())
                .Where(member => !string.IsNullOrWhiteSpace(member.Puuid))
                .DistinctBy(member => member.Puuid, StringComparer.Ordinal)
                .ToDictionary(member => member.Puuid, StringComparer.Ordinal);
            var lobbyByPuuid = (lobby?.PartyMembers ?? Array.Empty<GameTeamMember>())
                .Concat(lobby?.Team100 ?? Array.Empty<GameTeamMember>())
                .Concat(lobby?.Team200 ?? Array.Empty<GameTeamMember>())
                .Where(member => !string.IsNullOrWhiteSpace(member.Puuid))
                .DistinctBy(member => member.Puuid, StringComparer.Ordinal)
                .ToDictionary(member => member.Puuid, StringComparer.Ordinal);

            return members.Select(member =>
            {
                string puuid = member.Puuid ?? "";
                flowByPuuid.TryGetValue(puuid, out GameTeamMember? flowMember);
                lobbyByPuuid.TryGetValue(puuid, out GameTeamMember? lobbyMember);
                return new GameTeamMember
                {
                    Puuid = puuid,
                    SummonerName = !string.IsNullOrWhiteSpace(flowMember?.SummonerName)
                        ? flowMember.SummonerName
                        : string.IsNullOrWhiteSpace(puuid) ? $"玩家 {member.CellId}" : "加载玩家信息…",
                    ChampionId = member.ChampionId,
                    Position = member.AssignedPosition,
                    PartyId = !string.IsNullOrWhiteSpace(flowMember?.PartyId)
                        ? flowMember.PartyId : lobbyMember?.PartyId ?? "",
                    TeamParticipantId = flowMember?.TeamParticipantId ?? 0
                };
            }).ToArray();
        }

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
            string phase = Program.GameMain.gameFlowPhase.GetChineseName();
            string modeName = mode == "选人阶段" ? mode :
                string.IsNullOrWhiteSpace(mode) && queueId <= 0 ? "" :
                LolGameModeNames.GetModeText(queueId > 0 ? queueId.ToString() : "", mode);
            string modeText = string.IsNullOrEmpty(modeName) ? "" : $" · 模式: {modeName}";
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
                gameMode,
                CurrentPartyDetector.Detect(team1, team2));
        }

        private void RenderTeamsCore(
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            bool force,
            string? myPuuid,
            int queueId,
            string? gameMode,
            CurrentPartyDetectionResult partyDetection)
        {
            string signature = BuildRosterSignature(team1, team2);

            // 阵容未变化时跳过重建，避免自动刷新反复销毁/重建控件
            if (!force && signature == _lastSignature && _lastRenderedPhase == Program.GameMain.gameFlowPhase &&
                panelTeam1.Controls.Count > 0)
            {
                // 名单相同时英雄仍可能从未知变为已选择，或在选人阶段换选。
                // 原地更新英雄，保留已加载的战绩和一次性公告状态。
                UpdateCurrentChampions(panelTeam1, team1);
                UpdateCurrentChampions(panelTeam2, team2);
                // 房间标识可能比阵容晚到达；同一阵容也要更新开黑标签。
                ApplyPremadeResult(partyDetection);
                return;
            }
            _lastSignature = signature;
            _lastRenderedPhase = Program.GameMain.gameFlowPhase;
            PrepareChampSelectAssessments(signature, team1, team2, myPuuid);
            PrepareGameAssessments(signature, team1, team2);

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
            ApplyPremadeResult(partyDetection);
        }

        internal static void UpdateCurrentChampions(
            Control panel,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> members)
        {
            var champions = members.Where(member => !string.IsNullOrWhiteSpace(member.Puuid))
                .DistinctBy(member => member.Puuid, StringComparer.Ordinal)
                .ToDictionary(member => member.Puuid, member => member.ChampionId, StringComparer.Ordinal);
            foreach (LivePlayerForm card in panel.Controls.OfType<LivePlayerForm>())
            {
                if (card.Puuid != null && champions.TryGetValue(card.Puuid, out int championId))
                    _ = card.UpdateCurrentChampionAsync(championId);
            }
        }

        /// <summary>
        /// 生成阵容签名；队伍分开记录，避免换边后沿用旧卡片。
        /// </summary>
        private static string BuildRosterSignature(
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
            bool enabled = Program.GameMain.gameFlowPhase == GameFlowPhase.ChampSelect &&
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

        private void PrepareGameAssessments(
            string signature,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2)
        {
            bool enabled = Program.GameMain.gameFlowPhase == GameFlowPhase.InProgress;
            if (!enabled)
            {
                ResetGameAssessments();
                return;
            }
            if (string.Equals(signature, _gameAssessmentSignature, StringComparison.Ordinal)) return;

            ResetGameAssessments();
            _gameAssessmentSignature = signature;
            foreach (var member in team1)
                AddGameAssessmentPlayer(member.Puuid, member.Name, member.IsBot, "蓝方");
            foreach (var member in team2)
                AddGameAssessmentPlayer(member.Puuid, member.Name, member.IsBot, "红方");
            if (_gameAssessmentRoster.Count > 0 &&
                AppCompositionRoot.ApplicationSettingsStore.Load().GameKdaAnnouncementEnabled)
                _ = SendGameAssessmentsAfterTimeoutAsync(signature);
        }

        private void AddGameAssessmentPlayer(string puuid, string name, bool isBot, string team)
        {
            puuid ??= "";
            _gameAssessmentRoster.Add((puuid, name ?? "未知玩家", team));
            if (!isBot && !string.IsNullOrWhiteSpace(puuid))
                _expectedGameAssessmentPuuids.Add(puuid);
        }

        private void ResetGameAssessments()
        {
            unchecked { _gameAssessmentGeneration++; }
            _gameAssessments.Clear();
            _expectedGameAssessmentPuuids.Clear();
            _gameAssessmentRoster.Clear();
            _gameAssessmentSignature = "";
            _gameAssessmentSent = false;
            _gameAssessmentSending = false;
        }

        private async Task SendGameAssessmentsAfterTimeoutAsync(string signature)
        {
            await Task.Delay(TimeSpan.FromSeconds(45));
            if (IsDisposed || signature != _gameAssessmentSignature || _gameAssessmentSent ||
                Program.GameMain.gameFlowPhase != GameFlowPhase.InProgress) return;
            // 单局详情补取可能仍在进行；不能把尚未返回的玩家提前喊成“无数据”。
            for (int attempt = 0; attempt < 3 &&
                 !_expectedGameAssessmentPuuids.All(puuid => _gameAssessments.ContainsKey(puuid)); attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                if (IsDisposed || signature != _gameAssessmentSignature || _gameAssessmentSent ||
                    Program.GameMain.gameFlowPhase != GameFlowPhase.InProgress) return;
            }
            SendGameKdaAnnouncement();
        }

        private void OnPlayerRecentPerformanceReady(object? sender, PlayerRecentPerformanceEventArgs result)
        {
            if (IsDisposed) return;
            if (Program.GameMain.gameFlowPhase == GameFlowPhase.InProgress)
            {
                if (!_expectedGameAssessmentPuuids.Contains(result.Puuid)) return;
                _gameAssessments[result.Puuid] = result;
                if (_expectedGameAssessmentPuuids.All(puuid => _gameAssessments.ContainsKey(puuid)))
                    SendGameKdaAnnouncement();
                return;
            }
            if (_champSelectAssessmentSent || Program.GameMain.gameFlowPhase != GameFlowPhase.ChampSelect ||
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

        private void SendGameKdaAnnouncement()
        {
            if (_gameAssessmentSent || _gameAssessmentSending || _gameAssessmentRoster.Count == 0 ||
                Program.GameMain.gameFlowPhase != GameFlowPhase.InProgress) return;
            var settings = AppCompositionRoot.ApplicationSettingsStore.Load();
            if (!settings.GameKdaAnnouncementEnabled) return;

            IReadOnlyList<string> messages = BuildCurrentGameKdaMessages(settings.GameKdaOnePlayerPerLine);
            if (messages.Count == 0) return;

            _gameAssessmentSending = true;
            _ = SendGameKdaAnnouncementAsync(_gameAssessmentSignature,
                _gameAssessmentGeneration, messages, settings);
        }

        /// <summary>快捷键主动发送当前双方评估；不受自动公告开关或已发送标记限制。</summary>
        public async Task SendGameKdaManuallyAsync()
        {
            if (IsDisposed || Program.GameMain.gameFlowPhase != GameFlowPhase.InProgress) return;
            if (_gameAssessmentSending)
            {
                Program.GameMain.infoMsg.AddMsg("对局 KDA 正在发送，请稍后再试。");
                return;
            }
            if (_gameAssessmentRoster.Count == 0)
            {
                _ = AddView();
                Program.GameMain.infoMsg.AddMsg("正在获取对局阵容，请加载完成后再次按 KDA 快捷键。");
                return;
            }
            _gameAssessmentSending = true;
            int generation = _gameAssessmentGeneration;
            try
            {
                int? gameTime = await LocalLiveClientDataReader.GetGameTimeSecondsAsync();
                if (IsDisposed || generation != _gameAssessmentGeneration ||
                    Program.GameMain.gameFlowPhase != GameFlowPhase.InProgress) return;
                if (gameTime is null or < 5)
                {
                    Program.GameMain.infoMsg.AddMsg("游戏尚未就绪，请进入对局后再按 KDA 快捷键。");
                    return;
                }
                var settings = AppCompositionRoot.ApplicationSettingsStore.Load();
                var messages = BuildCurrentGameKdaMessages(settings.GameKdaOnePlayerPerLine);
                var result = await Program.GameMain.SendGameKdaAnnouncementAsync(messages, settings, requireForeground: true);
                if (IsDisposed || generation != _gameAssessmentGeneration) return;
                if (result.SentCount == messages.Count && messages.Count > 0) _gameAssessmentSent = true;
                Program.GameMain.infoMsg.AddMsg($"手动对局 KDA：{result.Message}（{result.SentCount}/{messages.Count} 条）");
                RuntimeDiagnostics.Report("对局 KDA 快捷键", "手动发送", result.Message);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) Program.GameMain.infoMsg.AddMsg($"手动对局 KDA 未发送：{ex.Message}");
            }
            finally
            {
                if (!IsDisposed && generation == _gameAssessmentGeneration) _gameAssessmentSending = false;
            }
        }

        private IReadOnlyList<string> BuildCurrentGameKdaMessages(bool onePlayerPerLine)
        {
            var players = _gameAssessmentRoster.Select(member =>
            {
                PlayerRecentPerformanceEventArgs? result = null;
                if (!string.IsNullOrWhiteSpace(member.Puuid))
                    _gameAssessments.TryGetValue(member.Puuid, out result);
                return new GameKdaPlayerSummary(member.Team,
                    result?.DisplayName ?? member.Name, result?.Assessment);
            }).ToArray();
            return GameKdaAnnouncementBuilder.Build(players, onePlayerPerLine);
        }

        private async Task SendGameKdaAnnouncementAsync(string signature, int generation,
            IReadOnlyList<string> messages, LOL_GameAssistant.Domain.Settings.AssistantSettings settings)
        {
            int sentCount = 0;
            try
            {
                while (!IsDisposed && generation == _gameAssessmentGeneration &&
                       signature == _gameAssessmentSignature &&
                       Program.GameMain.gameFlowPhase == GameFlowPhase.InProgress &&
                       AppCompositionRoot.ApplicationSettingsStore.Load().GameKdaAnnouncementEnabled)
                {
                    // InProgress 在加载画面就会出现；实时客户端接口可用后才允许注入聊天按键。
                    int? gameTime = await LocalLiveClientDataReader.GetGameTimeSecondsAsync();
                    if (gameTime is null or < 5)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }

                    // 等待游戏就绪或前台期间，卡片可能已经补齐战绩；首条发送前重新生成文案。
                    // 部分发送后保持原批次，避免拆分条数变化导致重复或遗漏。
                    if (sentCount == 0) messages = BuildCurrentGameKdaMessages(settings.GameKdaOnePlayerPerLine);
                    GameShoutSendResult result = await Program.GameMain.SendGameKdaAnnouncementAsync(
                        messages.Skip(sentCount).ToArray(), settings, requireForeground: true);
                    if (IsDisposed || generation != _gameAssessmentGeneration ||
                        signature != _gameAssessmentSignature) return;
                    sentCount += result.SentCount;
                    if (sentCount >= messages.Count)
                    {
                        _gameAssessmentSent = true;
                        Program.GameMain.infoMsg.AddMsg("对局双方近期 KDA 评估已注入游戏聊天；请确认聊天窗口。");
                        RuntimeDiagnostics.Report("对局 KDA 评估", "按键已注入", result.Message);
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                if (sentCount > 0 && !IsDisposed && generation == _gameAssessmentGeneration)
                    Program.GameMain.infoMsg.AddMsg($"对局 KDA 评估已注入 {sentCount}/{messages.Count} 条，对局结束或设置关闭后停止发送。");
            }
            catch (Exception ex)
            {
                if (IsDisposed || generation != _gameAssessmentGeneration) return;
                Program.GameMain.infoMsg.AddMsg($"对局双方近期 KDA 评估未发送：{ex.Message}");
                RuntimeDiagnostics.Report("对局 KDA 评估", "发送失败", ex.Message);
            }
            finally
            {
                if (!IsDisposed && generation == _gameAssessmentGeneration)
                    _gameAssessmentSending = false;
            }
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
            RecentModePerformanceAssessment? assessment = result.Assessment;
            string name = string.IsNullOrWhiteSpace(result.DisplayName) ? "未知玩家" : result.DisplayName.Trim();
            if (assessment == null) return $"{name}：战绩暂未获取";
            if (assessment.SampleSize == 0) return $"{name}：近30天同模式无样本";
            return $"{name}：{RecentPerformanceLabelFormatter.GetText(assessment)} {assessment.Score}分 · KDA {assessment.Kda:F2} · 胜率 {assessment.WinRate:F0}%";
        }

        private async Task SendChampSelectKdaAnnouncementAsync(string signature, string message)
        {
            var result = await AppCompositionRoot.ChampionSelectChatService.SendAsync(message);
            if (IsDisposed || signature != _champSelectAssessmentSignature) return;

            string summary = result.Succeeded
                ? "选人近期 KDA 评估已发送到聊天窗口。"
                : $"选人近期 KDA 评估未发送：{result.Message}";
            Program.GameMain.infoMsg.AddMsg(summary);
        }

        /// <summary>
        /// 将本局房间标识的分组结果应用到表头与玩家卡片。
        /// </summary>
        private void ApplyPremadeResult(CurrentPartyDetectionResult detection)
        {
            PremadeDetectionResult result = detection.Groups;
            string summary1 = result.GetTeamSummary(0);
            string summary2 = result.GetTeamSummary(1);
            lblTeamTitle1.Text = string.IsNullOrEmpty(summary1)
                ? _teamTitleBase1
                : $"{_teamTitleBase1} · 开黑 {summary1}";
            lblTeamTitle2.Text = string.IsNullOrEmpty(summary2)
                ? _teamTitleBase2
                : $"{_teamTitleBase2} · 开黑 {summary2}";

            SetTeamQueueTag(_teamQueueTag1, detection.GetTeamStatus(0), detection.GetTeamDetail(0));
            SetTeamQueueTag(_teamQueueTag2, detection.GetTeamStatus(1), detection.GetTeamDetail(1));

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
            if (IsDisposed || _layingOutPlayerCards) return;
            _layingOutPlayerCards = true;
            try
            {
                ResizePlayerCards(panelTeam1);
                ResizePlayerCards(panelTeam2);
            }
            finally { _layingOutPlayerCards = false; }
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
            int twoColumnScrollbar = !panel.VerticalScroll.Visible && NeedsVerticalScrollbar(panel, twoColumnRows)
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int twoColumnWidth = (contentWidth - twoColumnScrollbar - 2 * PlayerCardHorizontalMargin) / 2;
            if (cardCount > 1 && twoColumnWidth >= PlayerCardMinimumWidth)
            {
                return Math.Min(PlayerCardPreferredWidth, twoColumnWidth);
            }

            // 单列时让卡片填充所在队列，避免旧逻辑在窗口变宽后仍停留在固定 450px。
            int singleColumnScrollbar = !panel.VerticalScroll.Visible && NeedsVerticalScrollbar(panel, cardCount)
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int singleColumnWidth = contentWidth - singleColumnScrollbar - PlayerCardHorizontalMargin;
            return Math.Max(1, Math.Min(PlayerCardSingleColumnMaxWidth, singleColumnWidth));
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
