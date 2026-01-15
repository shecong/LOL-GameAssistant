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

        /// <summary>
        /// 游戏状态枚举
        /// </summary>
        public static GameFlowPhase gameFlowPhase;

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            //初始化模块
            await LoadAllForm();
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = Convert.ToInt32(settingForm.inputNumber1.Text);
            timer.Tick += Timer_Tick;
            //timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            //1.获取当前游戏状态
            _ = Task.Run(async () =>
           {
               var status = await Game_Api.GameFlowPhaseServer();
               if (status != null)
               {
                   if (Enum.TryParse(status, true, out GameFlowPhase parsedPhase))
                   {
                       gameFlowPhase = parsedPhase;
                       this.gameFlowPhaseName.Text = $"{gameFlowPhase.GetChineseName()}";
                   }
               }
           });
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
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void dj_refresh_Click(object sender, EventArgs e)
        {
            await liveGameForm.AddView();
        }

        public async void ConnectWebSocket()
        {
            // 创建客户端
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            var client = new WebSocketClient($"wss://127.0.0.1:{port}", Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{token}")));

            // 订阅事件
            client.OnMessage += msg => WebSocketMessage(msg);
            client.OnError += err => WebSocketError(err);
            client.OnConnectChanged += connected =>
                Console.WriteLine(connected ? "已连接" : "已断开");

            try
            {
                // 连接
                await client.ConnectAsync();

                // 发送消息
                await client.SendAsync("[5, \"OnJsonApiEvent\"]");

                // 保持连接
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
            finally
            {
                //await client.CloseAsync();
                //client.Dispose();
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
        private void HandleJsonApiEvent(JsonNode dataNode)
        {
            try
            {
                if (dataNode == null)
                {
                    Console.WriteLine("OnJsonApiEvent 数据为空");
                    return;
                }

                // 提取事件数据
                var uri = dataNode["uri"]?.GetValue<string>();
                var eventType = dataNode["eventType"]?.GetValue<string>();
                var data = dataNode["data"];

                // 根据URI进行特定处理
                if (!string.IsNullOrEmpty(uri))
                {
                    switch (uri)
                    {
                        case "/lol-gameflow/v1/gameflow-phase":
                            gameflowphaseStatus(Convert.ToString(data));
                            infoMsg.AddMsg(data.ToString());
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
            //修改主页状态
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
                    //在大厅,如果有开启自动对局,则自动开启
                    SettingForm.OpenGame(settingForm);
                    //刷新对局数据
                    await liveGameForm.AddView();
                    break;

                case "matchmaking":
                    //匹配中,如果有开启自动接受,则自动接受
                    SettingForm.GameTrue(settingForm);
                    break;

                case "ReadyCheck":

                    break;

                case "champselect":
                    //选择英雄阶段，执行禁用英雄和自动选择英雄|且刷新一次对局数据（ps：对手战绩此时查看不到）

                    //刷新对局数据
                    _ = liveGameForm.AddView();
                    break;

                case "GameStart":
                    break;

                case "inprogress":
                    //对局中，自动刷新对局数据
                    _ = liveGameForm.AddView();
                    break;

                case "waitingforstats":
                case "terminatedinerror":
                    //结束对局
                    break;

                default:
                    break;
            }
        }
    }
}