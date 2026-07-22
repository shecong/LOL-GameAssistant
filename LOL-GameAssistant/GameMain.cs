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
        public static SettingForm settingForm = new SettingForm();
        public static LiveGameForm liveGameForm = new LiveGameForm();
        public Plyaer? userinfo = new Plyaer();

        private WebSocketClient? _wsClient;

        /// <summary>
        /// 游戏状态枚举（volatile 保证多核可见性）
        /// </summary>
        public static volatile GameFlowPhase gameFlowPhase;

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            //初始化模块
            await LoadAllForm();
        }

        /// <summary>
        /// 初始化加载所有窗体
        /// </summary>
        private async Task LoadAllForm()
        {
            //使用websocket连接
            try
            {
                await ConnectWebSocketAsync();
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"WebSocket连接失败: {ex.Message}");
            }

            //加载首页
            tab0_grid1.Controls.Clear();
            tab0_grid1.Controls.Add(home);

            //加载对局
            tab1_grid1.Controls.Clear();
            tab1_grid1.Controls.Add(liveGameForm);
            //加载战绩查询
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
        private async void dj_refresh_Click(object sender, EventArgs e)
        {
            try
            {
                await liveGameForm.AddView();
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"对局刷新异常: {ex.Message}");
            }
        }

        public async Task ConnectWebSocketAsync()
        {
            // 释放旧连接（如有）
            if (_wsClient != null)
            {
                try { await _wsClient.DisposeAsync(); } catch { }
                _wsClient = null;
            }

            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();

            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                infoMsg.AddMsg("未找到LOL客户端认证信息，请确保客户端已启动并登录。");
                return;
            }

            // 创建客户端
            _wsClient = new WebSocketClient($"wss://127.0.0.1:{port}", Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}")));

            // 订阅事件
            _wsClient.OnMessage += msg => WebSocketMessage(msg);
            _wsClient.OnError += err => WebSocketError(_wsClient, err.Message);
            _wsClient.OnConnectChanged += connected =>
                WebSocketChange(_wsClient, connected);

            try
            {
                await _wsClient.ConnectAsync();
                await _wsClient.SendAsync("[5, \"OnJsonApiEvent\"]");
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"WebSocket连接异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放 WebSocket 资源
        /// </summary>
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_wsClient != null)
            {
                try { await _wsClient.DisposeAsync(); } catch { }
            }
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 连接状态变化
        /// </summary>
        private void WebSocketChange(WebSocketClient client, bool connected)
        {
            if (connected)
            {
                infoMsg.AddMsg("WebSocket已连接");
            }
        }

        private async Task WebSocketError(WebSocketClient client, string err)
        {
            infoMsg.AddMsg(err);
        }

        private void WebSocketMessage(string msg)
        {
            try
            {
                var jsonArray = JsonNode.Parse(msg)?.AsArray();
                if (jsonArray == null || jsonArray.Count < 3) return;

                var messageId = jsonArray[0]?.GetValue<int>() ?? 0;
                var eventName = jsonArray[1]?.GetValue<string>();
                var dataNode = jsonArray[2];

                switch (eventName)
                {
                    case "OnJsonApiEvent":
                        HandleJsonApiEvent(dataNode);
                        break;

                    default:
                        Console.WriteLine($"未知事件: {eventName}");
                        Console.WriteLine($"完整消息: {msg}");
                        break;
                }
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"WebSocket消息解析异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理 OnJsonApiEvent 事件
        /// </summary>
        private void HandleJsonApiEvent(JsonNode dataNode)
        {
            try
            {
                if (dataNode == null)
                {
                    Console.WriteLine("OnJsonApiEvent 数据为空");
                    return;
                }

                var uri = dataNode["uri"]?.GetValue<string>();
                var eventType = dataNode["eventType"]?.GetValue<string>();
                var data = dataNode["data"];

                if (!string.IsNullOrEmpty(uri))
                {
                    switch (uri)
                    {
                        case "/lol-gameflow/v1/gameflow-phase":
                            // 修复：使用 GetValue<string>() 获取原始字符串值，而非 Convert.ToString()（会带引号）
                            var phaseStr = data?.GetValue<string>();
                            if (!string.IsNullOrEmpty(phaseStr))
                            {
                                gameflowphaseStatus(phaseStr);
                            }
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
        /// 游戏流程状态处理（始终在 UI 线程执行）
        /// </summary>
        /// <param name="statustype"></param>
        private void gameflowphaseStatus(string statustype)
        {
            // 确保在 UI 线程执行
            if (this.InvokeRequired)
            {
                this.BeginInvoke(() => gameflowphaseStatus(statustype));
                return;
            }

            if (string.IsNullOrEmpty(statustype)) return;

            // 更新主页状态
            if (Enum.TryParse(statustype, true, out GameFlowPhase parsedPhase))
            {
                gameFlowPhase = parsedPhase;
                this.gameFlowPhaseName.Text = $"{gameFlowPhase.GetChineseName()}";
            }

            switch (statustype.ToLower())
            {
                case "none":
                    break;

                case "lobby":
                    // 在大厅，如果有开启自动对局，则自动开启
                    SettingForm.OpenGame(settingForm);
                    break;

                case "matchmaking":
                    break;

                case "readycheck":
                    // 匹配中，如果有开启自动接受，则自动接受
                    SettingForm.GameTrue(settingForm);
                    break;

                case "champselect":
                    // 选择英雄阶段：自动禁用 & 自动选择
                    settingForm.BeginInvoke(async () =>
                    {
                        try
                        {
                            // 刷新对局数据
                            await liveGameForm.AddView();
                            // 执行自动 ban/pick
                            await settingForm.ExecuteAutoBanPickOnceAsync();
                        }
                        catch (Exception ex) { infoMsg.AddMsg($"ChampSelect处理失败: {ex.Message}"); }
                    });
                    break;

                case "gamestart":
                    break;

                case "inprogress":
                    // 对局中，自动刷新对局数据
                    this.BeginInvoke(async () =>
                    {
                        try { await liveGameForm.AddView(); }
                        catch (Exception ex) { infoMsg.AddMsg($"对局刷新失败: {ex.Message}"); }
                    });
                    break;

                case "waitingforstats":
                case "terminatedinerror":
                    // 结束对局
                    break;

                default:
                    break;
            }
        }
    }
}
