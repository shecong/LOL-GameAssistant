using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using GameFlowPhase = LOL_GameAssistant.Domain.LeagueClient.GameFlowPhase;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public static InfoMsgForm infoMsg = new InfoMsgForm();
        public static HomeForm home = new HomeForm(infoMsg!);
        public static FriendsForm friendsForm = new FriendsForm();
        public static LiveGameForm liveGameForm = new LiveGameForm();
        public static BattleQueryForm battleQueryForm = new BattleQueryForm();
        public static CoachForm coachForm = new CoachForm();
        public static DiagnosticsForm diagnosticsForm = new DiagnosticsForm();
        // SettingForm may load while controls are attached; its side effects need these pages.
        public static SettingForm settingForm = new SettingForm();

        private readonly ILeagueClientEventStream _eventStream;
        private readonly ILobbyService _lobbyService;
        private readonly IChampionSelectService _championSelectService;
        private readonly IClientFeatureService _clientFeatureService;
        private readonly IApplicationSettingsStore _settingsStore;
        private readonly IGameClientLauncher _gameClientLauncher;
        private readonly IRecommendationCoordinator _recommendationCoordinator;
        private CancellationTokenSource? _lcuRetryCts;
        private NotifyIcon? _trayIcon;
        private CancellationTokenSource? _autoActionCts;
        private CancellationTokenSource? _autoAcceptCts;
        private CancellationTokenSource? _opggPromptCts;
        private CancellationTokenSource? _phaseDataLoadCts;
        private GameFlowPhase? _lastNotifiedEndPhase;
        private bool _readyCheckDeclinedByUser;
        private bool _postGameAutomationsTriggered;
        private bool _restoringFromTray;
        private readonly WindowHoldController _windowHoldController;
        private readonly QuickMessageSenderController _quickMessageController;
        private bool _autoClientLaunchAttempted;
        private bool? _lastAntdDarkMode;

        /// <summary>
        /// 游戏状态枚举
        /// </summary>
        public static GameFlowPhase gameFlowPhase;

        /// <summary>
        /// 当前是否停留在“对局”标签页。
        /// </summary>
        private const int FriendsTabIndex = 1;

        private const int LiveGameTabIndex = 2;
        private const int BattleQueryTabIndex = 3;
        private const int CoachTabIndex = 7;
        private readonly AntdUI.TabPage _coachTab;
        private readonly AntdUI.TabPage _diagnosticsTab;

        public bool IsLiveGameTabActive => tabs1.SelectedIndex == LiveGameTabIndex;

        public Task<GameShoutSendResult> SendQuickShoutToGameAsync(string phrase, bool sendToAll,
            bool useClipboard, bool perCharacter, int minimumIntervalSeconds) =>
            _quickMessageController.SendSelectedToGameAsync(phrase, sendToAll,
                useClipboard, perCharacter, minimumIntervalSeconds);

        public Task<GameShoutSendResult> TestGameChatOpenAsync() =>
            _quickMessageController.TestChatOpenAsync();

        public Task<GameShoutSendResult> SendGameKdaAnnouncementAsync(
            IReadOnlyList<string> messages, AssistantSettings settings) =>
            _quickMessageController.SendBatchToGameAsync(messages,
                settings.QuickShoutSendToAll, settings.QuickShoutUseClipboard,
                settings.QuickMessageSendIntervalSeconds);

        public void ConfigureQuickShoutHotkeys(AssistantSettings config) =>
            _windowHoldController.ConfigureQuickShoutHotkeys(config,
                custom => _ = settingForm.SendRandomQuickShoutToGameAsync(custom));

        /// <summary>
        /// 切换到“战绩查询”标签页（供其他界面点击玩家头像跳转使用）。
        /// </summary>
        public void ShowBattleQueryPage()
        {
            if (tabs1.SelectedIndex != BattleQueryTabIndex)
            {
                tabs1.SelectedIndex = BattleQueryTabIndex;
            }
        }

        public GameMain() : this(
            AppCompositionRoot.LeagueClientEventStream,
            AppCompositionRoot.LobbyService,
            AppCompositionRoot.ChampionSelectService,
            AppCompositionRoot.ClientFeatureService,
            AppCompositionRoot.ApplicationSettingsStore,
            AppCompositionRoot.GameClientLauncher,
            AppCompositionRoot.RecommendationCoordinator)
        {
        }

        /// <summary>主窗体仅接收连接与大厅应用端口，认证细节保留在基础设施层。</summary>
        internal GameMain(
            ILeagueClientEventStream eventStream,
            ILobbyService lobbyService,
            IChampionSelectService championSelectService,
            IClientFeatureService clientFeatureService,
            IApplicationSettingsStore settingsStore,
            IGameClientLauncher gameClientLauncher,
            IRecommendationCoordinator recommendationCoordinator)
        {
            _eventStream = eventStream;
            _lobbyService = lobbyService;
            _championSelectService = championSelectService;
            _clientFeatureService = clientFeatureService;
            _settingsStore = settingsStore;
            _gameClientLauncher = gameClientLauncher;
            _recommendationCoordinator = recommendationCoordinator;
            InitializeComponent();
            _coachTab = new AntdUI.TabPage { Text = "智能建议", Dock = DockStyle.Fill };
            _diagnosticsTab = new AntdUI.TabPage { Text = "运行诊断", Dock = DockStyle.Fill };
            tabs1.Controls.Add(_coachTab);
            tabs1.Pages.Add(_coachTab);
            tabs1.Controls.Add(_diagnosticsTab);
            tabs1.Pages.Add(_diagnosticsTab);
            _windowHoldController = new WindowHoldController(this);
            _quickMessageController = new QuickMessageSenderController(this);
            UiTheme.Changed += UiThemeChanged;
            _eventStream.EventReceived += LeagueClientEventReceived;
            _eventStream.ErrorOccurred += WebSocketError;
            _eventStream.ConnectionChanged += WebSocketChange;
            _eventStream.Reconnecting += WebSocketReconnecting;
        }

        /// <summary>应用设置页中的透明度和“按住置顶”快捷键。</summary>
        public void ApplyWindowSettings(AssistantSettings config)
        {
            _windowHoldController.Apply(config);
            ConfigureQuickShoutHotkeys(config);
            UiTheme.SetMode(config.ThemeMode);
            ApplyTheme();
        }

        private void UiThemeChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(ApplyTheme);
            else ApplyTheme();
        }

        /// <summary>Reapply the semantic palette to all pages already attached to the main window.</summary>
        public void ApplyTheme()
        {
            ThemePalette palette = UiTheme.Palette;
            // 与参考实现一致：AntdUI 的全局主题和窗口语义色同时更新，避免混用控件出现两套配色。
            if (_lastAntdDarkMode != palette.IsDark)
            {
                AntdUI.Style.Clear();
                if (palette.IsDark)
                {
                    AntdUI.Style.Set(AntdUI.Colour.BgBase, palette.Surface);
                    AntdUI.Style.Set(AntdUI.Colour.BgLayout, palette.Surface);
                    AntdUI.Style.Set(AntdUI.Colour.BgContainer, palette.SurfaceRaised);
                    AntdUI.Style.Set(AntdUI.Colour.BgElevated, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.TextBase, palette.TextPrimary);
                    AntdUI.Style.Set(AntdUI.Colour.Text, palette.TextPrimary);
                    AntdUI.Style.Set(AntdUI.Colour.TextSecondary, palette.TextSecondary);
                    AntdUI.Style.Set(AntdUI.Colour.TextTertiary, palette.TextSecondary);
                    AntdUI.Style.Set(AntdUI.Colour.TextQuaternary, palette.TextSecondary);
                    AntdUI.Style.Set(AntdUI.Colour.Fill, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.FillSecondary, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.FillTertiary, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.FillQuaternary, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.HoverBg, palette.SurfaceMuted);
                    AntdUI.Style.Set(AntdUI.Colour.BorderColor, palette.Border);
                }
                _lastAntdDarkMode = palette.IsDark;
            }
            AntdUI.Config.IsDark = palette.IsDark;
            btn_theme_toggle.IconSvg = palette.IsDark ? "MoonFilled" : "SunOutlined";
            btn_theme_toggle.AccessibleName = palette.IsDark ? "切换到浅色主题" : "切换到深色主题";
            BackColor = palette.Surface;
            ForeColor = palette.TextPrimary;
            UiTheme.Apply(this);
            Refresh();
        }

        /// <summary>同步建议开关与调度周期；协调器不依赖设置页是否可见。</summary>
        public void ApplyRecommendationSettings(AssistantSettings config) =>
            _recommendationCoordinator.UpdateSettings(config);

        public async void GameMain_Load(object sender, EventArgs e)
        {
            // 托盘与窗口事件先挂好：它们不依赖 LCU/网络，
            // 若放在 await 之后，客户端探测卡住时这段时间窗口既没有托盘图标也没有关闭拦截。
            InitializeTray();
            FormClosing += GameMain_FormClosing;
            Resize += GameMain_Resize;
            tabs1.SelectedIndexChanged += Tabs1_SelectedIndexChanged;

            // 设置页并不一定会被用户打开；在主窗体启动时立即恢复已保存的快捷键，
            // 使快捷消息不依赖设置页的 Load 事件才开始注册。
            AssistantSettings startupSettings = _settingsStore.Load();
            ApplyWindowSettings(startupSettings);
            _recommendationCoordinator.Start(startupSettings);
            TryAutoLaunchLeagueClient(startupSettings);
            //初始化模块
            LoadAllForm();
            liveGameForm.ConfigureAutoRefresh(startupSettings.AutoRefresh,
                Math.Max(10, startupSettings.AutoRefreshIntervalSeconds));
            ApplyTheme();
            _ = InitializeLiveGameAsync();
        }

        /// <summary>
        /// 自动启动属于主窗口生命周期，而不是设置页生命周期。
        /// 这样即使用户从未切换到“设置”标签，保存过的开关也会在助手启动时执行一次。
        /// </summary>
        private void TryAutoLaunchLeagueClient(AssistantSettings settings)
        {
            if (_autoClientLaunchAttempted) return;
            _autoClientLaunchAttempted = true;

            if (!settings.AutoLaunchGameClient) return;

            _ = AutoLaunchLeagueClientAsync(settings.GameClientPath);
        }

        private async Task AutoLaunchLeagueClientAsync(string configuredPath)
        {
            try
            {
                GameClientLaunchResult result = await _gameClientLauncher.StartAndVerifyAsync(configuredPath);
                AddInfoMessage($"自动启动 LOL：{result.Message}");
            }
            catch (OperationCanceledException)
            {
                // Application shutdown.
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Report("LOL 客户端", "启动失败", ex.Message);
                AddInfoMessage($"自动启动 LOL 失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 切换到“对局”标签页时自动拉取最新对局信息。
        /// </summary>
        private void Tabs1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (tabs1.SelectedIndex == FriendsTabIndex)
            {
                _ = friendsForm.RefreshAsync();
            }
            else if (tabs1.SelectedIndex == LiveGameTabIndex)
            {
                _ = liveGameForm.AddView(force: true);
            }
            else if (tabs1.SelectedIndex == CoachTabIndex)
            {
                _ = coachForm.RefreshRecommendationAsync(manual: true);
            }
        }

        /// <summary>
        /// 用 LCU 返回的阶段字符串更新全局阶段与表头显示。
        /// 不能只依赖 WebSocket 事件：LCU 只在阶段"变化"时推送，订阅当下不会补发当前阶段，
        /// 因此启动或重连时可能一直停在旧值，导致"刷新"按钮点了没反应。
        /// </summary>
        public void ApplyGameFlowPhase(string? phaseText)
        {
            if (string.IsNullOrEmpty(phaseText)) return;
            if (!Enum.TryParse(phaseText, true, out GameFlowPhase parsed)) return;

            gameFlowPhase = parsed;
            string label = parsed.GetChineseName();
            // 该全局阶段值可能由后台线程写入，表头更新统一切回 UI 线程
            if (RunOnUiThread(() => { gameFlowPhaseName.Text = label; })) return;
            gameFlowPhaseName.Text = label;
        }

        /// <summary>
        /// 程序启动时检测客户端是否已在对局流程中，若是则立即加载对局信息。
        /// </summary>
        private async Task InitializeLiveGameAsync()
        {
            try
            {
                string? phase = await _lobbyService.GetGameFlowPhaseAsync();
                if (string.IsNullOrEmpty(phase)) return;

                // 先写回全局阶段，否则 AddView 读到的是默认值，会什么都不做
                ApplyGameFlowPhase(phase);
                _recommendationCoordinator.NotifyGamePhaseChanged(phase);

                if (Enum.TryParse(phase, true, out GameFlowPhase parsed) &&
                    (parsed == GameFlowPhase.Lobby ||
                     parsed == GameFlowPhase.ChampSelect ||
                     parsed == GameFlowPhase.InProgress))
                {
                    if (parsed is GameFlowPhase.ChampSelect or GameFlowPhase.InProgress)
                        StartPhaseDataLoad(parsed);
                    else
                        await liveGameForm.AddView(force: true);
                }
                if (parsed == GameFlowPhase.ChampSelect)
                    StartOpggChampSelectMonitor();
            }
            catch
            {
                // 启动阶段检测失败不影响主流程
            }
        }

        /// <summary>
        /// 初始化加载所有窗体
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private void LoadAllForm()
        {
            //使用websokect连接
            ConnectWebSocket();
            //加载首页
            tab0_grid1.Controls.Clear();
            tab0_grid1.Controls.Add(home);

            //加载好友
            friendsGrid.Controls.Clear();
            friendsGrid.Controls.Add(friendsForm);

            //加载对局
            tab1_grid1.Controls.Clear();
            liveGameForm.Dock = DockStyle.Fill;
            tab1_grid1.Controls.Add(liveGameForm);
            //加载战绩查询
            tabPage3.Controls.Clear();
            battleQueryForm.Dock = DockStyle.Fill;
            tabPage3.Controls.Add(battleQueryForm);
            //加载智能建议
            _coachTab.Controls.Clear();
            coachForm.Dock = DockStyle.Fill;
            _coachTab.Controls.Add(coachForm);
            _diagnosticsTab.Controls.Clear();
            diagnosticsForm.Dock = DockStyle.Fill;
            _diagnosticsTab.Controls.Add(diagnosticsForm);
            //关于
            tab4_grid1.Controls.Add(new AboutForm() { Dock = DockStyle.Fill });
            //加载设置
            tabPage5.Controls.Clear();
            settingForm.Dock = DockStyle.Fill;
            tabPage5.Controls.Add(settingForm);
            //加载日志窗口
            tab5_grid1.Controls.Clear();
            tab5_grid1.Controls.Add(infoMsg);
        }

        /// <summary>
        /// 刷新对局
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void dj_refresh_Click(object sender, EventArgs e)
        {
            await liveGameForm.AddView(force: true);
        }

        /// <summary>标题栏快捷切换明确写入浅色/深色，不改变用户在设置中选择的“跟随系统”之外的其它配置。</summary>
        private void btn_theme_toggle_Click(object sender, EventArgs e)
        {
            AssistantSettings settings = _settingsStore.Load();
            settings.ThemeMode = UiTheme.Palette.IsDark ? "Light" : "Dark";
            settings.Normalize();
            _settingsStore.Save(settings);
            UiTheme.SetMode(settings.ThemeMode);
            ApplyTheme();
            AntdUI.Message.success(this, settings.ThemeMode == "Dark" ? "已切换为深色主题" : "已切换为浅色主题");
        }

        /// <summary>
        /// 启动 WebSocket 连接（首次调用）。
        /// LCU 探测内部是 WMI 查询（Win32_Process），同步跑在 UI 线程上会让窗口白屏假死，
        /// 因此探测与连接整体放到后台线程；需要更新界面的地方都会自己切回 UI 线程。
        /// </summary>
        public void ConnectWebSocket() => _ = Task.Run(ConnectWebSocketCoreAsync);

        private async Task ConnectWebSocketCoreAsync()
        {
            // 认证发现、协议连接和订阅都由基础设施层完成；主窗体只处理事件结果。
            if (!await _eventStream.ConnectAsync().ConfigureAwait(false))
            {
                AddInfoMessage("未检测到 LOL 客户端，每 10 秒重试获取 LCU 端口...");
                _ = RetryLcuDetectionAsync();
            }
        }

        /// <summary>
        /// 周期性检测 LOL 客户端是否启动，直到获取到有效 LCU 端口
        /// </summary>
        private async Task RetryLcuDetectionAsync()
        {
            if (_lcuRetryCts != null) _lcuRetryCts.Cancel();
            _lcuRetryCts = new CancellationTokenSource();
            var token = _lcuRetryCts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                bool connected;
                try
                {
                    connected = await _eventStream.ConnectAsync(forceRefresh: true, cancellationToken: token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (connected)
                {
                    AddInfoMessage("检测到 LOL 客户端已启动，正在连接 WebSocket...");
                    break;
                }
            }
        }

        /// <summary>
        /// LCU WebSocket 订阅不会补发“当前阶段”。客户端晚于助手启动、或连接重建时，
        /// 主动读取一次阶段并复用常规阶段处理，避免已经选人/进游戏却没有自动加载数据。
        /// </summary>
        private async Task SynchronizeCurrentGameFlowAsync()
        {
            try
            {
                string? phase = await _lobbyService.GetGameFlowPhaseAsync();
                if (string.IsNullOrWhiteSpace(phase)) return;
                if (RunOnUiThread(() => _ = SynchronizeCurrentGameFlowAsync())) return;
                await gameflowphaseStatus(phase);
            }
            catch
            {
                // 连接建立的极短暂窗口内 LCU 可能还未准备好；后续事件或手动刷新会再次读取。
            }
        }

        /// <summary>
        /// 选人和刚进入游戏时，相关 LCU 端点会比 gameflow 事件晚就绪。
        /// 立即加载后再做两次短延迟重试，确保无需用户手点刷新，同时用取消令牌防止旧局回填。
        /// </summary>
        private void StartPhaseDataLoad(GameFlowPhase expectedPhase)
        {
            StopPhaseDataLoad();
            _phaseDataLoadCts = new CancellationTokenSource();
            _ = LoadPhaseDataAsync(expectedPhase, _phaseDataLoadCts.Token);
        }

        private void StopPhaseDataLoad()
        {
            _phaseDataLoadCts?.Cancel();
            _phaseDataLoadCts?.Dispose();
            _phaseDataLoadCts = null;
        }

        private async Task LoadPhaseDataAsync(GameFlowPhase expectedPhase, CancellationToken cancellationToken)
        {
            try
            {
                foreach (int delayMilliseconds in new[] { 0, 1500, 4500 })
                {
                    if (delayMilliseconds > 0)
                        await Task.Delay(delayMilliseconds, cancellationToken);
                    if (IsDisposed || cancellationToken.IsCancellationRequested ||
                        gameFlowPhase != expectedPhase) return;

                    await liveGameForm.AddView(force: true);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 正常阶段切换；不允许上一局的延迟加载覆盖新阵容。
            }
        }

        /// <summary>
        /// 把后台线程回调切回 UI 线程执行。
        /// LCU 事件流由后台线程回调，
        /// 在池线程上操作控件会在 native 层破坏窗口句柄：Release 未挂调试器时
        /// CheckForIllegalCrossThreadCalls 为 false，不会抛"跨线程操作无效"，
        /// 只会莫名其妙地报"创建窗口句柄时出错"。
        /// </summary>
        /// <returns>true 表示已转投到 UI 线程（或窗口正在退出），调用方应立即返回。</returns>
        private bool RunOnUiThread(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return true;
            if (!InvokeRequired) return false;

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // 窗口正在销毁，丢弃本次回调
            }
            return true;
        }

        /// <summary>
        /// WebSocket 连接状态变化处理
        /// </summary>
        private void WebSocketChange(bool connected)
        {
            if (RunOnUiThread(() => WebSocketChange(connected))) return;

            if (connected)
            {
                RuntimeDiagnostics.Report("LCU WebSocket", "已连接", "已订阅游戏流程事件");
                infoMsg.AddMsg("WebSocket已连接");
                // 客户端是后启动的，刷新首页玩家数据
                _ = home.RefreshAsync();
                // 连接成功后重新订阅事件（重连后 LCU 侧需要重新订阅）
                _ = _eventStream.SubscribeToJsonApiEventsAsync();
                _ = SynchronizeCurrentGameFlowAsync();
            }
            else
            {
                RuntimeDiagnostics.Report("LCU WebSocket", "已断开", "正在重新检测 lockfile 与客户端端口");
                infoMsg.AddMsg("WebSocket已断开，正在检测 LCU 端口变化...");
                _ = Task.Run(() => RetryLcuDetectionAsync());
            }
        }

        private void WebSocketError(string err)
        {
            RuntimeDiagnostics.Report("LCU WebSocket", "错误", err);
            AddInfoMessage(err);
        }

        private void WebSocketReconnecting(string message)
        {
            RuntimeDiagnostics.Report("LCU WebSocket", "重连中", message);
            AddInfoMessage(message);
        }

        /// <summary>所有后台连接反馈都通过此方法切回界面线程。</summary>
        private void AddInfoMessage(string message)
        {
            if (RunOnUiThread(() => AddInfoMessage(message))) return;
            infoMsg.AddMsg(message);
        }

        /// <summary>处理基础设施已解析的 LCU 事件；表现层不再解析 WebSocket 原始 JSON。</summary>
        private void LeagueClientEventReceived(LeagueClientEvent gameEvent)
        {
            if (RunOnUiThread(() => LeagueClientEventReceived(gameEvent))) return;

            if (string.Equals(gameEvent.Uri, "/lol-gameflow/v1/gameflow-phase", StringComparison.Ordinal))
            {
                RuntimeDiagnostics.Report("游戏流程", gameEvent.Data, "来自 LCU 游戏流程事件");
                _ = gameflowphaseStatus(gameEvent.Data);
                infoMsg.AddMsg(gameEvent.Data);
            }
            else if (string.Equals(gameEvent.Uri, "/lol-matchmaking/v1/ready-check", StringComparison.Ordinal))
            {
                ObserveReadyCheckResponse(gameEvent.Data);
            }
        }

        /// <summary>
        /// 仅在明确收到 Declined 时抑制本轮自动接受，保证手动拒绝不会被延迟任务覆盖。
        /// </summary>
        private void ObserveReadyCheckResponse(string data)
        {
            if (!data.Contains("\"playerResponse\":\"Declined\"", StringComparison.OrdinalIgnoreCase) &&
                !data.Contains("\"playerResponse\": \"Declined\"", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _readyCheckDeclinedByUser = true;
            CancelAutoAccept();
            AddInfoMessage("检测到手动拒绝，本轮不再自动接受");
        }

        /// <summary>
        /// 游戏流程状态处理
        /// </summary>
        /// <param name="statustype"></param>
        private async Task gameflowphaseStatus(String? statustype)
        {
            if (string.IsNullOrEmpty(statustype)) return;
            // 下面会直接操作对局页控件（AddView / ResetRosterCache），必须回到 UI 线程
            if (RunOnUiThread(() => { _ = gameflowphaseStatus(statustype); })) return;

            string phase = statustype.ToLowerInvariant();

            //修改主页状态
            if (Enum.TryParse(statustype, true, out GameFlowPhase parsedPhase))
            {
                gameFlowPhase = parsedPhase;
                // 离开结束状态后重置“已通知”标记，保证下一局还能再次提醒
                if (parsedPhase != GameFlowPhase.WaitingForStats && parsedPhase != GameFlowPhase.EndOfGame)
                {
                    _lastNotifiedEndPhase = null;
                    _postGameAutomationsTriggered = false;
                }
                if (parsedPhase != GameFlowPhase.ReadyCheck)
                {
                    CancelAutoAccept();
                    _readyCheckDeclinedByUser = false;
                }
                // 已在 UI 线程上，直接更新表头即可
                this.gameFlowPhaseName.Text = $"{gameFlowPhase.GetChineseName()}";
            }
            _recommendationCoordinator.NotifyGamePhaseChanged(statustype);
            if (!string.Equals(phase, "champselect", StringComparison.OrdinalIgnoreCase))
                StopOpggChampSelectMonitor();
            switch (phase)
            {
                case "none":
                    StopPhaseDataLoad();
                    liveGameForm.ResetRosterCache();
                    break;

                case "lobby":
                    StopPhaseDataLoad();
                    //在大厅,如果有开启自动对局,则自动开启
                    SettingForm.OpenGame(settingForm);
                    _ = liveGameForm.AddView(force: true);
                    break;

                case "matchmaking":
                    StopPhaseDataLoad();
                    break;

                case "readycheck":
                    StopPhaseDataLoad();
                    ScheduleAutoAccept();
                    liveGameForm.ResetRosterCache();
                    break;

                case "champselect":
                    // 立即加载，并在选人会话/玩家列表就绪后自动重试两次。
                    StartPhaseDataLoad(GameFlowPhase.ChampSelect);
                    _ = AutoBanPickLoopAsync();
                    StartOpggChampSelectMonitor();
                    break;

                case "gamestart":
                    StopPhaseDataLoad();
                    break;

                case "inprogress":
                    // 游戏进程、实时客户端接口和全员阵容并非同时可用，使用同一套延迟加载策略。
                    StartPhaseDataLoad(GameFlowPhase.InProgress);
                    break;

                case "waitingforstats":
                case "terminatedinerror":
                case "endofgame":
                    StopPhaseDataLoad();
                    //结束对局：通知 + 刷新战绩
                    // 该局已经结束，释放对局页的开黑检测结果；下一局必须重新检测。
                    liveGameForm.ResetRosterCache();
                    await NotifyGameEndedAsync();
                    TriggerPostGameAutomations();
                    _ = liveGameForm.AddView();
                    break;

                default:
                    break;
            }
        }

        /// <summary>根据持久化延迟范围安排一次自动接受；同一轮 Ready Check 不重复排队。</summary>
        private void ScheduleAutoAccept()
        {
            if (_readyCheckDeclinedByUser || _autoAcceptCts != null) return;

            AssistantSettings config = _settingsStore.Load();
            if (!config.AutoAccept) return;

            _autoAcceptCts = new CancellationTokenSource();
            _ = AcceptReadyCheckAfterDelayAsync(config, _autoAcceptCts);
        }

        private async Task AcceptReadyCheckAfterDelayAsync(AssistantSettings config, CancellationTokenSource source)
        {
            try
            {
                int min = Math.Max(0, config.AutoAcceptDelayMinMilliseconds);
                int max = Math.Max(min, config.AutoAcceptDelayMaxMilliseconds);
                int delay = min == max ? min : Random.Shared.Next(min, max + 1);
                if (delay > 0)
                    await Task.Delay(delay, source.Token).ConfigureAwait(true);

                if (source.Token.IsCancellationRequested || _readyCheckDeclinedByUser ||
                    gameFlowPhase != GameFlowPhase.ReadyCheck)
                {
                    return;
                }

                await _lobbyService.AcceptReadyCheckAsync(source.Token).ConfigureAwait(true);
                AddInfoMessage(delay > 0 ? $"已延迟 {delay}ms 自动接受对局" : "已自动接受对局");
            }
            catch (OperationCanceledException)
            {
                // 阶段结束、用户拒绝或应用关闭时，取消本轮延迟是预期行为。
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Report("自动接受", "执行失败", ex.Message);
                AddInfoMessage($"自动接受失败：{ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_autoAcceptCts, source))
                {
                    _autoAcceptCts.Dispose();
                    _autoAcceptCts = null;
                }
            }
        }

        private void CancelAutoAccept()
        {
            CancellationTokenSource? source = _autoAcceptCts;
            _autoAcceptCts = null;
            source?.Cancel();
            source?.Dispose();
        }

        /// <summary>赛后自动化只在同一局的第一个结束阶段执行一次。</summary>
        private void TriggerPostGameAutomations()
        {
            if (_postGameAutomationsTriggered) return;
            _postGameAutomationsTriggered = true;
            _ = RunPostGameAutomationsAsync(_settingsStore.Load());
        }

        private async Task RunPostGameAutomationsAsync(AssistantSettings config)
        {
            try
            {
                if (config.AutoHonor)
                {
                    ClientFeatureResult honor = await _clientFeatureService.HonorRandomEligibleAllyAsync();
                    AddInfoMessage(honor.Message);
                }

                if (config.AutoReturnToLobby)
                {
                    ClientFeatureResult result = await _clientFeatureService.ReturnToLobbyAsync(config.AutoReturnStartMatchmaking);
                    AddInfoMessage(result.Message);
                }
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.Report("赛后自动化", "执行失败", ex.Message);
                AddInfoMessage($"赛后自动化失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 选人阶段后台处理自动禁用和自动选用英雄。
        /// 自动选用单独快速轮询，避免受到自动禁用间隔影响。
        /// </summary>
        private async Task AutoBanPickLoopAsync()
        {
            _autoActionCts?.Cancel();
            _autoActionCts = new CancellationTokenSource();
            var token = _autoActionCts.Token;

            try
            {
                // 读取持久化配置（避免设置页未打开时读取到内存默认值）
                var config = _settingsStore.Load();
                bool autoBanEnabled = config.AutoBan;
                bool autoPickEnabled = config.AutoPick;
                var cachedBanIds = ResolveChampionIds(config.BanChampions);
                var cachedPickIds = ResolveChampionIds(config.PickChampions);

                // 自动抢英雄立即开始，独立于自动禁用循环和其间隔设置。
                Task pickTask = autoPickEnabled && cachedPickIds.Count > 0
                    ? AutoPickFastLoopAsync(cachedPickIds, config, token)
                    : Task.CompletedTask;

                // 自动禁用仍按设置的“自动禁用间隔”执行。
                while (autoBanEnabled && cachedBanIds.Count > 0 &&
                       gameFlowPhase == GameFlowPhase.ChampSelect && !token.IsCancellationRequested)
                {
                    if (await _championSelectService.AutoBanAsync(cachedBanIds))
                    {
                        infoMsg.AddMsg("自动禁用英雄成功");
                    }

                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(1, config.CheckIntervalSeconds)),
                        token).ConfigureAwait(false);
                }

                // 等待快速抢英雄任务正常收尾，避免留下未观察的后台任务。
                await pickTask.ConfigureAwait(false);
                infoMsg.AddMsg("选人阶段结束，停止自动禁/选");
            }
            catch (OperationCanceledException)
            {
                // 窗体关闭时正常取消
            }
        }

        /// <summary>
        /// 快速尝试自动选英雄：首次立即请求，选人动作尚未生成时每 100ms 重试一次。
        /// </summary>
        private async Task AutoPickFastLoopAsync(
            List<int> pickChampionIds,
            AssistantSettings config,
            CancellationToken token)
        {
            const int RetryDelayMilliseconds = 100;

            if (config.SkipAutoPickOnFill && await ShouldSkipAutoPickForFillAsync(token))
            {
                AddInfoMessage("检测到补位，已跳过自动选人");
                return;
            }

            while (gameFlowPhase == GameFlowPhase.ChampSelect && !token.IsCancellationRequested)
            {
                try
                {
                    if (await _championSelectService.AutoPickAsync(
                        pickChampionIds,
                        lockIn: !config.AutoPickPreselectOnly,
                        cancellationToken: token))
                    {
                        infoMsg.AddMsg(config.AutoPickPreselectOnly ? "已自动预选英雄" : "自动选用英雄成功");
                        return;
                    }
                }
                catch
                {
                    // 选人会话尚未准备好时静默重试，不影响自动抢英雄流程。
                }

                await Task.Delay(RetryDelayMilliseconds, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 仅在客户端同时给出偏好分路和实际分路时判定补位；信息缺失时继续自动选人，
        /// 避免在没有分路概念的模式误跳过。
        /// </summary>
        private async Task<bool> ShouldSkipAutoPickForFillAsync(CancellationToken cancellationToken)
        {
            ChampionSelectionSnapshot? selection = await _championSelectService.GetSessionAsync(cancellationToken);
            LobbySnapshot? lobby = await _lobbyService.GetLobbyAsync(cancellationToken);
            if (selection == null || lobby == null) return false;

            var me = selection.MyTeam.FirstOrDefault(member => member.CellId == selection.LocalPlayerCellId);
            if (me?.IsAutofilled == true) return true;
            string assigned = NormalizePosition(me?.AssignedPosition);
            string primary = NormalizePosition(lobby.LocalPrimaryPosition);
            string secondary = NormalizePosition(lobby.LocalSecondaryPosition);
            if (string.IsNullOrEmpty(assigned) || string.IsNullOrEmpty(primary)) return false;

            return !string.Equals(assigned, primary, StringComparison.Ordinal) &&
                   !string.IsNullOrEmpty(secondary) &&
                   !string.Equals(assigned, secondary, StringComparison.Ordinal);
        }

        private static string NormalizePosition(string? position) => position?.Trim().ToUpperInvariant() switch
        {
            "TOP" => "TOP",
            "JUNGLE" or "JNG" => "JUNGLE",
            "MIDDLE" or "MID" => "MIDDLE",
            "BOTTOM" or "BOT" or "ADC" => "BOTTOM",
            "UTILITY" or "SUPPORT" or "SUP" => "UTILITY",
            _ => ""
        };

        /// <summary>
        /// 把设置中的英雄名列表转换为英雄 ID 列表。
        /// </summary>
        private static List<int> ResolveChampionIds(List<string> names)
        {
            return names
                .Select(AppCompositionRoot.ChampionCatalog.FindIdByDisplayName)
                .OfType<int>()
                .ToList();
        }

        /// <summary>
        /// 选人期间以较低频率检查当前已选英雄。实际弹窗和“同英雄只提示一次”由 CoachForm 管理，
        /// 使该功能不依赖用户是否正停留在智能建议页。
        /// </summary>
        private void StartOpggChampSelectMonitor()
        {
            StopOpggChampSelectMonitor();
            coachForm.ResetOpggChampSelectPrompt();
            _opggPromptCts = new CancellationTokenSource();
            _ = MonitorOpggChampSelectAsync(_opggPromptCts.Token);
            RuntimeDiagnostics.Report("OP.GG 选人推荐", "监测中", "已进入选人阶段，开始检测已选英雄");
        }

        private void StopOpggChampSelectMonitor()
        {
            _opggPromptCts?.Cancel();
            _opggPromptCts?.Dispose();
            _opggPromptCts = null;
            coachForm.ResetOpggChampSelectPrompt();
        }

        private async Task MonitorOpggChampSelectAsync(CancellationToken cancellationToken)
        {
            while (gameFlowPhase == GameFlowPhase.ChampSelect && !cancellationToken.IsCancellationRequested)
            {
                // 异常只在单次轮询内消化：写在循环外的话，一次偶发失败会让整个选人阶段
                // 再也不提示 OP.GG，而界面上没有任何反馈。
                try
                {
                    if (_settingsStore.Load().OpggBuildAssistantEnabled)
                        await coachForm.PromptOpggBuildIfNeededAsync(cancellationToken);

                    await Task.Delay(TimeSpan.FromMilliseconds(550), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 选人结束、离开客户端或关闭主窗口时正常停止。
                    break;
                }
                catch (Exception ex)
                {
                    RuntimeDiagnostics.Report("OP.GG 选人推荐", "监测失败", ex.Message);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 对局结束提醒（托盘气泡 + 日志）。
        /// </summary>
        private Task NotifyGameEndedAsync()
        {
            try
            {
                var config = _settingsStore.Load();
                if (!config.NotifyOnGameEnd) return Task.CompletedTask;
                if (_lastNotifiedEndPhase == gameFlowPhase) return Task.CompletedTask;
                _lastNotifiedEndPhase = gameFlowPhase;

                infoMsg.AddMsg("对局已结束，可查看战绩详情");
                if (_trayIcon != null)
                {
                    // 气泡提示要求托盘图标可见；提示显示期间先保持图标，
                    // 6 秒后再交回"窗口隐藏才显示图标"的规则，避免把最后的入口关掉。
                    _trayIcon.Visible = true;
                    _trayIcon.ShowBalloonTip(5000, "LOL GameAssistant", "对局已结束，可查看战绩详情。", ToolTipIcon.Info);
                    _ = Task.Delay(6000).ContinueWith(_ => RunOnUiThread(UpdateTrayVisibility));
                }
            }
            catch
            {
                // 通知失败不影响主流程
            }
            return Task.CompletedTask;
        }

        private void InitializeTray()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "LOL GameAssistant 运行中",
                Icon = SystemIcons.Application,
                Visible = false
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
            menu.Items.Add("-");
            menu.Items.Add("退出", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;
            // 单击也要能唤回窗口：只挂双击时，习惯单击托盘图标的人会以为程序打不开了。
            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowWindow();
            };
            _trayIcon.DoubleClick += (_, _) => ShowWindow();
        }

        /// <summary>
        /// 托盘图标可见性 = 主窗口是否隐藏。
        /// 不要把它和"关闭时最小化到托盘"配置绑在一起：一旦窗口被藏起来而图标又没显示，
        /// 这个进程就再没有任何入口能被唤回（只能去任务管理器结束）。
        /// </summary>
        private void UpdateTrayVisibility()
        {
            if (_trayIcon != null) _trayIcon.Visible = !Visible;
        }

        /// <summary>
        /// 从托盘恢复窗口。
        /// 两处都要设置：Show() 之前先恢复 Normal，否则窗口会以"最小化"状态显示出来；
        /// Show() 之后再兜一次，因为窗口处于隐藏状态时这个赋值可能被忽略，窗口会停在最小化。
        /// 恢复过程中用 _restoringFromTray 屏蔽 GameMain_Resize 的隐藏逻辑。
        /// </summary>
        private void ShowWindow()
        {
            if (IsDisposed) return;

            _restoringFromTray = true;
            try
            {
                if (WindowState != FormWindowState.Normal)
                    WindowState = FormWindowState.Normal;

                Show();

                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                Activate();
                BringToFront();
            }
            finally
            {
                _restoringFromTray = false;
            }

            // 窗口确实显示出来了才收起托盘图标；恢复失败时保留图标，不丢掉最后的入口。
            UpdateTrayVisibility();
        }

        /// <summary>
        /// 真正退出应用
        /// </summary>
        private void ExitApp()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            System.Windows.Forms.Application.Exit();
        }

        /// <summary>
        /// 关闭窗口时最小化到托盘或退出
        /// </summary>
        private void GameMain_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _settingsStore.Load().MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                UpdateTrayVisibility();
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(2000, "LOL GameAssistant", "已最小化到系统托盘，单击或双击图标恢复窗口", ToolTipIcon.Info);
                }
            }
            else
            {
                _autoActionCts?.Cancel();
                CancelAutoAccept();
                StopOpggChampSelectMonitor();
                StopPhaseDataLoad();
                _lcuRetryCts?.Cancel();
                _trayIcon?.Dispose();
                _eventStream.Dispose();
                _windowHoldController.Dispose();
                _quickMessageController.Dispose();
                UiTheme.Changed -= UiThemeChanged;
                _recommendationCoordinator.Dispose();
            }
        }

        /// <summary>
        /// 最小化时隐藏到托盘
        /// </summary>
        private void GameMain_Resize(object? sender, EventArgs e)
        {
            // 从托盘恢复时会先改 WindowState 再 Show()，这一瞬间不能把窗口又藏回去
            if (_restoringFromTray) return;

            if (WindowState == FormWindowState.Minimized && _settingsStore.Load().MinimizeToTray)
            {
                Hide();
                UpdateTrayVisibility();
                if (_trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(2000, "LOL GameAssistant", "已最小化到系统托盘", ToolTipIcon.Info);
                }
            }
        }
    }
}
