using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class SettingForm : UserControl
    {
        public DateTime? lastOpenGameTime = DateTime.Now;

        /// <summary>
        /// 自动禁用英雄列表（用户选中的英雄 ID）
        /// </summary>
        private List<int> BanChampionIds = new List<int>();

        /// <summary>
        /// 自动选择英雄列表（用户选中的英雄 ID）
        /// </summary>
        private List<int> PickChampionIds = new List<int>();

        /// <summary>
        /// 定时轮询 Timer
        /// </summary>
        private System.Windows.Forms.Timer? _pollTimer;

        /// <summary>
        /// 上次执行 ban/pick 的 session action 快照，避免重复执行
        /// </summary>
        private string? _lastActionsHash;

        /// <summary>
        /// Ban/pick 执行冷却计时（避免同一轮多次触发）
        /// </summary>
        private DateTime _lastAutoActionTime = DateTime.MinValue;

        /// <summary>
        /// 上次自动匹配检测时间
        /// </summary>
        private DateTime _lastAutoMatchCheck = DateTime.MinValue;

        /// <summary>
        /// 上次自动接受检测时间
        /// </summary>
        private DateTime _lastAutoAcceptCheck = DateTime.MinValue;

        /// <summary>
        /// 防重入标志，0=空闲，1=正在轮询
        /// </summary>
        private int _isPolling = 0;

        public SettingForm()
        {
            InitializeComponent();
        }

        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadBase();
            // 监听间隔时间变化
            inputNumber1.ValueChanged += InputNumber1_ValueChanged;
        }

        protected override void Dispose(bool disposing)
        {
            // 停止轮询 Timer（必须在 Dispose 前停止，防止访问已释放的控件）
            StopPolling();
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 定时执行方法

        /// <summary>
        /// 自动匹配对局（线程安全，始终在 UI 线程执行）
        /// </summary>
        public static void OpenGame(SettingForm form)
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(() => OpenGame(form));
                return;
            }

            var now = DateTime.Now;
            if ((form.lastOpenGameTime == null || (now - form.lastOpenGameTime.Value).TotalSeconds >= 10) && form.swi_open.Checked)
            {
                _ = Game_Api.OpenGameServerAsync();
                form.lastOpenGameTime = now;
            }
        }

        /// <summary>
        /// 自动接受对局（线程安全，始终在 UI 线程执行）
        /// </summary>
        public static void GameTrue(SettingForm form)
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(() => GameTrue(form));
                return;
            }

            if (form.swi_gametrue.Checked)
            {
                _ = Game_Api.GameTrueServerAsync();
            }
        }

        /// <summary>
        /// 开始轮询（WebSocket 连接后或游戏启动后调用）
        /// </summary>
        public void StartPolling()
        {
            if (_pollTimer != null) return;

            int intervalSeconds = Math.Max(1, (int)(inputNumber1?.Value ?? 2));
            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = intervalSeconds * 1000;
            _pollTimer.Tick += async (s, e) => await PollTickAsync();
            _pollTimer.Start();

            GameMain.infoMsg.AddMsg($"设置轮询已启动，检测间隔: {intervalSeconds}秒");
        }

        /// <summary>
        /// 停止轮询
        /// </summary>
        public void StopPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
                _pollTimer = null;
            }
        }

        /// <summary>
        /// 用户修改检查间隔时更新 Timer
        /// </summary>
        private void InputNumber1_ValueChanged(object sender, decimal value)
        {
            if (_pollTimer != null)
            {
                int intervalSeconds = Math.Max(1, (int)value);
                _pollTimer.Interval = intervalSeconds * 1000;
                GameMain.infoMsg.AddMsg($"轮询间隔已更新为: {intervalSeconds}秒");
            }
        }

        /// <summary>
        /// 轮询检测：游戏未启动时持续检测 LCU 连接，
        /// 连接上之后根据游戏阶段执行自动操作
        /// </summary>
        private async Task PollTickAsync()
        {
            // 防重入：上次轮询还在 await 中时跳过
            if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0) return;

            try
            {
                // 检查 LCU 连接是否可用
                if (string.IsNullOrEmpty(HttpClentHelper.Port) || string.IsNullOrEmpty(HttpClentHelper.Token))
                {
                    // 尝试获取 LCU 认证
                    (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
                    if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                    {
                        HttpClentHelper.SetCredentials(port, token);
                        GameMain.infoMsg.AddMsg($"轮询检测到LOL客户端，端口: {port}");

                        // 尝试重连 WebSocket（如果还没连上）
                        if (GameMain.gameFlowPhase == GameFlowPhase.None || GameMain.gameFlowPhase == GameFlowPhase.Closed)
                        {
                            _ = Program.GameMain.ConnectWebSocketAsync();
                        }

                        // 通过 rest API 获取当前阶段
                        var phase = await Game_Api.GameFlowPhaseServer();
                        if (!string.IsNullOrEmpty(phase))
                        {
                            GameMain.gameFlowPhase = Enum.TryParse(phase, true, out GameFlowPhase p) ? p : GameFlowPhase.None;
                            if (GameMain.gameFlowPhase == GameFlowPhase.Lobby)
                            {
                                GameMain.infoMsg.AddMsg("轮询检测到游戏在大厅状态");
                            }
                        }
                    }
                    return;
                }

                // 获取当前游戏阶段
                var currentPhase = await Game_Api.GameFlowPhaseServer();
                if (string.IsNullOrEmpty(currentPhase)) return;

                if (Enum.TryParse(currentPhase, true, out GameFlowPhase parsedPhase))
                {
                    GameMain.gameFlowPhase = parsedPhase;
                }

                var now = DateTime.Now;

                switch (currentPhase.ToLower())
                {
                    case "lobby":
                        // 自动匹配：每 10 秒检测一次
                        if (swi_open.Checked && (now - _lastAutoMatchCheck).TotalSeconds >= 10)
                        {
                            _lastAutoMatchCheck = now;
                            _ = Game_Api.OpenGameServerAsync();
                            GameMain.infoMsg.AddMsg("轮询: 自动开始匹配");
                        }
                        break;

                    case "matchmaking":
                        break;

                    case "readycheck":
                        // 自动接受：每 5 秒检测一次
                        if (swi_gametrue.Checked && (now - _lastAutoAcceptCheck).TotalSeconds >= 5)
                        {
                            _lastAutoAcceptCheck = now;
                            _ = Game_Api.GameTrueServerAsync();
                            GameMain.infoMsg.AddMsg("轮询: 自动接受对局");
                        }
                        break;

                    case "champselect":
                        // 自动禁用 & 自动选择
                        if ((swi_jyyx.Checked || swi_xyx.Checked) &&
                            (now - _lastAutoActionTime).TotalSeconds >= 3)
                        {
                            await ExecuteAutoBanPickAsync();
                            _lastAutoActionTime = now;
                        }
                        break;

                    case "inprogress":
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                // 轮询异常不中断定时器
                GameMain.infoMsg.AddMsg($"轮询异常: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        /// <summary>
        /// 执行自动 ban/pick（轮询调用）
        /// </summary>
        private async Task ExecuteAutoBanPickAsync()
        {
            try
            {
                var session = await Select_Api.GetSessionAsync();
                if (session == null) return;

                // 刷新 ban/pick 目标（先刷新，确保数据最新）
                RefreshBanPickTargets();

                // 检查 session 是否有变化（避免重复执行）
                var actionsJson = session["actions"]?.ToJsonString() ?? "";
                var actionsHash = actionsJson.GetHashCode().ToString();
                if (actionsHash == _lastActionsHash) return;

                // 在确认要执行后才设置 hash
                if ((BanChampionIds.Count > 0 || PickChampionIds.Count > 0) &&
                    (swi_jyyx.Checked || swi_xyx.Checked))
                {
                    var banIds = swi_jyyx.Checked ? new List<int>(BanChampionIds) : new List<int>();
                    var pickIds = swi_xyx.Checked ? new List<int>(PickChampionIds) : new List<int>();
                    await Select_Api.ExecuteAutoActionsAsync(session, banIds, pickIds);
                    _lastActionsHash = actionsHash;
                }
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"自动ban/pick执行异常: {ex.Message}");
            }
        }

        /// <summary>
        /// WebSocket 事件触发的一次性自动 ban/pick（GameMain 调用）
        /// </summary>
        public async Task ExecuteAutoBanPickOnceAsync()
        {
            var now = DateTime.Now;
            if ((now - _lastAutoActionTime).TotalSeconds < 2) return;
            _lastAutoActionTime = now;

            await ExecuteAutoBanPickAsync();
        }

        /// <summary>
        /// 从 UI 选择控件刷新 ban/pick 目标
        /// </summary>
        private void RefreshBanPickTargets()
        {
            try
            {
                BanChampionIds.Clear();
                PickChampionIds.Clear();

                // 从多选控件获取选中的英雄名称
                if (swi_jyyx.Checked)
                {
                    var selectedNames = GetSelectedChampionNames(setting_select_jyx);
                    foreach (var name in selectedNames)
                    {
                        var champion = ChampionMap.GetChampionMap()
                            .FirstOrDefault(c => c.Value.RealName == name).Value;
                        if (champion != null && champion.Value > 0)
                        {
                            BanChampionIds.Add(champion.Value);
                        }
                    }
                }

                if (swi_xyx.Checked)
                {
                    var selectedNames = GetSelectedChampionNames(setting_select_xyx);
                    foreach (var name in selectedNames)
                    {
                        var champion = ChampionMap.GetChampionMap()
                            .FirstOrDefault(c => c.Value.RealName == name).Value;
                        if (champion != null && champion.Value > 0)
                        {
                            PickChampionIds.Add(champion.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"刷新ban/pick目标异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 SelectMultiple 安全获取选中的英雄名称列表
        /// </summary>
        private static List<string> GetSelectedChampionNames(AntdUI.SelectMultiple control)
        {
            var result = new List<string>();

            if (control.SelectedValue == null) return result;

            // SelectedValue 可能是多种类型，逐一尝试
            if (control.SelectedValue is IList<string> stringList)
            {
                result.AddRange(stringList.Where(n => !string.IsNullOrEmpty(n)));
            }
            else if (control.SelectedValue is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var str = item?.ToString();
                    if (!string.IsNullOrEmpty(str))
                        result.Add(str);
                }
            }
            else
            {
                var str = control.SelectedValue.ToString();
                if (!string.IsNullOrEmpty(str))
                    result.Add(str);
            }

            return result;
        }

        #endregion 定时执行方法

        /// <summary>
        /// 初始化基础数据
        /// </summary>
        private async Task LoadBase()
        {
            // 获取所有英雄
            var allChampions = ChampionMap.GetChampionMap();
            var names = allChampions.Values
                .Where(c => c.Value > 0)
                .OrderBy(c => c.RealName)
                .Select(c => c.RealName)
                .ToList();

            this.setting_select_jyx.Items.AddRange(names);
            this.setting_select_xyx.Items.AddRange(names);

            // 启动轮询
            StartPolling();
        }
    }
}
