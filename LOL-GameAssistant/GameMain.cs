using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static LOL_GameAssistant.Entity.LolRankedDataParser;
using static LOL_GameAssistant.Entity.PlayerModel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public static InfoMsgForm infoMsg = new InfoMsgForm();
        public static HomeForm home = new HomeForm(infoMsg!);
        public static FriendsForm friendsForm = new FriendsForm();
        public static SettingForm settingForm = new SettingForm();
        public static LiveGameForm liveGameForm = new LiveGameForm();
        public static BattleQueryForm battleQueryForm = new BattleQueryForm();
        public Plyaer? userinfo = new Plyaer();

        private WebSocketClient? _wsClient;
        private CancellationTokenSource? _lcuRetryCts;
        private NotifyIcon? _trayIcon;
        private CancellationTokenSource? _autoActionCts;
        private GameFlowPhase? _lastNotifiedEndPhase;

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

        public bool IsLiveGameTabActive => tabs1.SelectedIndex == LiveGameTabIndex;

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

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            //初始化模块
            await LoadAllForm();
            InitializeTray();
            FormClosing += GameMain_FormClosing;
            Resize += GameMain_Resize;
            tabs1.SelectedIndexChanged += Tabs1_SelectedIndexChanged;
            _ = InitializeLiveGameAsync();
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
        }

        /// <summary>
        /// 程序启动时检测客户端是否已在对局流程中，若是则立即加载对局信息。
        /// </summary>
        private async Task InitializeLiveGameAsync()
        {
            try
            {
                string? phase = await Game_Api.GameFlowPhaseServer();
                if (string.IsNullOrEmpty(phase)) return;
                if (Enum.TryParse(phase, true, out GameFlowPhase parsed) &&
                    (parsed == GameFlowPhase.Lobby ||
                     parsed == GameFlowPhase.ChampSelect ||
                     parsed == GameFlowPhase.InProgress))
                {
                    await liveGameForm.AddView(force: true);
                }
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
        private async Task LoadAllForm()
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
            tab1_grid1.Controls.Add(liveGameForm);
            //加载战绩查询
            tabPage3.Controls.Clear();
            tabPage3.Controls.Add(battleQueryForm);
            //关于
            tab4_grid1.Controls.Add(new AboutForm() { Dock = DockStyle.Fill });
            //加载设置
            tabPage5.Controls.Clear();
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

        /// <summary>
        /// 启动 WebSocket 连接（首次调用）
        /// </summary>
        public async void ConnectWebSocket()
        {
            // 获取 LCU 认证信息
            (string? port, string? token) = GetlolLcu.GetAuth();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                infoMsg.AddMsg("未检测到 LOL 客户端，每 10 秒重试获取 LCU 端口...");
                _ = Task.Run(() => RetryLcuDetectionAsync());
                return;
            }

            await ConnectWebSocketWithAuth(port, token);
        }

        /// <summary>
        /// 携带认证信息连接 WebSocket
        /// </summary>
        private async Task ConnectWebSocketWithAuth(string port, string token)
        {
            // 同步更新 HTTP 客户端认证信息，确保晚于客户端启动时
            // 的自动匹配/自动接受/战绩请求也能正常调用 LCU。
            HttpClentHelper.Port = port;
            HttpClentHelper.Token = token;

            // 释放旧客户端
            if (_wsClient != null)
            {
                await _wsClient.CloseAsync();
                _wsClient.Dispose();
            }

            _wsClient = new WebSocketClient(
                $"wss://127.0.0.1:{port}",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}"))
            );

            // 订阅事件
            _wsClient.OnMessage += msg => WebSocketMessage(msg);
            _wsClient.OnError += err => WebSocketError(err.Message);
            _wsClient.OnConnectChanged += connected => WebSocketChange(connected);
            _wsClient.OnReconnecting += msg => infoMsg.AddMsg(msg);

            // 连接（客户端内部自动启用重连）
            await _wsClient.ConnectAsync();
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

                (string? port, string? newToken) = GetlolLcu.GetAuth();
                if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(newToken))
                {
                    infoMsg.AddMsg("检测到 LOL 客户端已启动，正在连接 WebSocket...");
                    await ConnectWebSocketWithAuth(port, newToken);
                    break;
                }
            }
        }

        /// <summary>
        /// WebSocket 连接状态变化处理
        /// </summary>
        private void WebSocketChange(bool connected)
        {
            if (connected)
            {
                infoMsg.AddMsg("WebSocket已连接");
                // 客户端是后启动的，刷新首页玩家数据
                _ = home.RefreshAsync();
                // 连接成功后重新订阅事件（重连后 LCU 侧需要重新订阅）
                _ = _wsClient?.SendAsync("[5, \"OnJsonApiEvent\"]");
            }
            else
            {
                infoMsg.AddMsg("WebSocket已断开，正在检测 LCU 端口变化...");
                _ = Task.Run(() => RetryLcuDetectionAsync());
            }
        }

        private void WebSocketError(string err)
        {
            infoMsg.AddMsg(err);
        }

        private void WebSocketMessage(string msg)
        {
            //infoMsg.AddMsg(msg);
            // 解析JSON数组
            try
            {
                var jsonArray = JsonNode.Parse(msg)?.AsArray();
                if (jsonArray == null || jsonArray.Count < 3) return;
                // 提取数组元素
                var messageId = jsonArray[0]?.GetValue<int>() ?? 0;  // 第一个元素：消息ID（如8）
                var eventName = jsonArray[1]?.GetValue<string>();    // 第二个元素：事件名称
                var dataNode = jsonArray[2];                         // 第三个元素：数据对象
                                                                     // 根据事件类型处理
                switch (eventName)
                {
                    case "OnJsonApiEvent":
                        HandleJsonApiEvent(dataNode);
                        break;

                    // 可以添加其他事件类型
                    default:
                        Console.WriteLine($"未知事件: {eventName}");
                        Console.WriteLine($"完整消息: {msg}");
                        break;
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        /// <summary>
        /// 处理 OnJsonApiEvent 事件
        /// </summary>
        private void HandleJsonApiEvent(JsonNode? dataNode)
        {
            try
            {
                if (dataNode == null)
                {
                    return;
                }

                // 提取事件数据
                var uri = dataNode["uri"]?.GetValue<string>();
                _ = dataNode["eventType"]?.GetValue<string>(); // eventType reserved for future use
                var data = dataNode["data"];

                // 根据URI进行特定处理
                if (!string.IsNullOrEmpty(uri))
                {
                    switch (uri)
                    {
                        case "/lol-gameflow/v1/gameflow-phase":
                            _ = gameflowphaseStatus(Convert.ToString(data));
                            if (data != null) infoMsg.AddMsg(data.ToString());
                            break;

                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 游戏流程状态处理
        /// </summary>
        /// <param name="statustype"></param>
        private async Task gameflowphaseStatus(String? statustype)
        {
            if (string.IsNullOrEmpty(statustype)) return;
            string phase = statustype.ToLowerInvariant();

            //修改主页状态
            if (Enum.TryParse(statustype, true, out GameFlowPhase parsedPhase))
            {
                gameFlowPhase = parsedPhase;
                // 离开结束状态后重置“已通知”标记，保证下一局还能再次提醒
                if (parsedPhase != GameFlowPhase.WaitingForStats && parsedPhase != GameFlowPhase.EndOfGame)
                {
                    _lastNotifiedEndPhase = null;
                }
                this.BeginInvoke(new Action(() =>
                { this.gameFlowPhaseName.Text = $"{gameFlowPhase.GetChineseName()}"; }));
            }
            switch (phase)
            {
                case "none":
                    liveGameForm.ResetRosterCache();
                    break;

                case "lobby":
                    //在大厅,如果有开启自动对局,则自动开启
                    SettingForm.OpenGame(settingForm);
                    _ = liveGameForm.AddView(force: true);
                    break;

                case "matchmaking":

                    break;

                case "readycheck":
                    //匹配中,如果有开启自动接受,则自动接受
                    SettingForm.GameTrue(settingForm);
                    liveGameForm.ResetRosterCache();
                    break;

                case "champselect":
                    //选择英雄阶段，执行禁用英雄和自动选择英雄|且刷新一次对局数据（ps：对手战绩此时查看不到）

                    //刷新对局数据
                    _ = liveGameForm.AddView(force: true);
                    _ = AutoBanPickLoopAsync();
                    break;

                case "GameStart":
                    break;

                case "inprogress":
                    //对局中，自动刷新对局数据
                    _ = liveGameForm.AddView(force: true);
                    break;

                case "waitingforstats":
                case "terminatedinerror":
                case "endofgame":
                    //结束对局：通知 + 刷新战绩
                    await NotifyGameEndedAsync();
                    _ = liveGameForm.AddView();
                    break;

                default:
                    break;
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
                var config = Entity.SettingCache.Load();
                bool autoBanEnabled = config.AutoBan;
                bool autoPickEnabled = config.AutoPick;
                var cachedBanIds = ResolveChampionIds(config.BanChampions);
                var cachedPickIds = ResolveChampionIds(config.PickChampions);

                // 自动抢英雄立即开始，独立于自动禁用循环和其间隔设置。
                Task pickTask = autoPickEnabled && cachedPickIds.Count > 0
                    ? AutoPickFastLoopAsync(cachedPickIds, token)
                    : Task.CompletedTask;

                // 自动禁用仍按设置的“自动禁用间隔”执行。
                while (autoBanEnabled && cachedBanIds.Count > 0 &&
                       gameFlowPhase == GameFlowPhase.ChampSelect && !token.IsCancellationRequested)
                {
                    if (await Select_Api.AutoBanAsync(cachedBanIds))
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
        private async Task AutoPickFastLoopAsync(List<int> pickChampionIds, CancellationToken token)
        {
            const int RetryDelayMilliseconds = 100;

            while (gameFlowPhase == GameFlowPhase.ChampSelect && !token.IsCancellationRequested)
            {
                try
                {
                    if (await Select_Api.AutoPickAsync(pickChampionIds))
                    {
                        infoMsg.AddMsg("自动选用英雄成功");
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
        /// 把设置中的英雄名列表转换为英雄 ID 列表。
        /// </summary>
        private static List<int> ResolveChampionIds(List<string> names)
        {
            var map = Helper.ChampionMap.GetChampionMap();
            var result = new List<int>();
            foreach (var name in names)
            {
                foreach (var kv in map)
                {
                    if (string.Equals(kv.Value.RealName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(kv.Key);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 对局结束提醒（托盘气泡 + 日志）。
        /// </summary>
        private Task NotifyGameEndedAsync()
        {
            try
            {
                var config = Entity.SettingCache.Load();
                if (!config.NotifyOnGameEnd) return Task.CompletedTask;
                if (_lastNotifiedEndPhase == gameFlowPhase) return Task.CompletedTask;
                _lastNotifiedEndPhase = gameFlowPhase;

                infoMsg.AddMsg("对局已结束，可查看战绩详情");
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = true;
                    _trayIcon.ShowBalloonTip(5000, "LOL GameAssistant", "对局已结束，可查看战绩详情。", ToolTipIcon.Info);
                    _trayIcon.Visible = config.MinimizeToTray;
                }
            }
            catch
            {
                // 通知失败不影响主流程
            }
            return Task.CompletedTask;
        }

        /// </summary>
        private void InitializeTray()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "LOL GameAssistant 运行中",
                Visible = false
            };

            try
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
            menu.Items.Add("-");
            menu.Items.Add("退出", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => ShowWindow();
        }

        /// <summary>
        /// 从托盘恢复窗口
        /// </summary>
        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            if (_trayIcon != null) _trayIcon.Visible = false;
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
            Application.Exit();
        }

        /// <summary>
        /// 关闭窗口时最小化到托盘或退出
        /// </summary>
        private void GameMain_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && Entity.SettingCache.Load().MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = true;
                    _trayIcon.ShowBalloonTip(2000, "LOL GameAssistant", "已最小化到系统托盘，双击图标恢复窗口", ToolTipIcon.Info);
                }
            }
            else
            {
                _autoActionCts?.Cancel();
                _lcuRetryCts?.Cancel();
                _trayIcon?.Dispose();
                _wsClient?.Dispose();
            }
        }

        /// <summary>
        /// 最小化时隐藏到托盘
        /// </summary>
        private void GameMain_Resize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && Entity.SettingCache.Load().MinimizeToTray)
            {
                Hide();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = true;
                    _trayIcon.ShowBalloonTip(2000, "LOL GameAssistant", "已最小化到系统托盘", ToolTipIcon.Info);
                }
            }
        }
    }
}
