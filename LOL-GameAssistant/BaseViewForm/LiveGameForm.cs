using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using static LOL_GameAssistant.Entity.PlayerModel;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局页：渐变信息栏（呼吸状态点）+ 蓝方/红方渐变队头 + 玩家卡片（展开动效）。
    /// </summary>
    public partial class LiveGameForm : UserControl
    {
        private System.Windows.Forms.Timer? _autoRefreshTimer;
        private bool _refreshing;
        private string _lastSignature = "";
        private string? _myPuuid;
        private string _premadeSignature = "";
        private string _teamTitleBase1 = "蓝方";
        private string _teamTitleBase2 = "红方";
        private const int PlayerCardHeight = 470;
        private const int PlayerCardMaxWidth = 450;

        public LiveGameForm()
        {
            InitializeComponent();
            this.Load += LiveGameForm_Load;
            this.Disposed += (_, _) => _autoRefreshTimer?.Dispose();
        }

        private void LiveGameForm_Load(object? sender, EventArgs e)
        {
            lblGameInfo.Text = "暂无对局信息，进入对局后自动展示";
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
            _premadeSignature = "";
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
                if (GameMain.gameFlowPhase == GameFlowPhase.ChampSelect ||
                    GameMain.gameFlowPhase == GameFlowPhase.Lobby)
                {
                    LobbyGameInfo? gameInfo = await Game_Api.GameNowServer();
                    if (gameInfo?.GameConfig == null)
                    {
                        lblGameInfo.Text = "未获取到大厅信息";
                        return;
                    }

                    SetGameInfo(gameInfo.GameConfig.GameMode, gameInfo.GameConfig.QueueId);
                    // 大厅/选人阶段优先取本地成员 puuid，判断我方队伍
                    string? myPuuid = string.IsNullOrEmpty(gameInfo.LocalMember?.Puuid)
                        ? await GetMyPuuidAsync()
                        : gameInfo.LocalMember.Puuid;
                    RenderTeams(
                        gameInfo.GameConfig.CustomTeam100 ?? new List<Member>(),
                        gameInfo.GameConfig.CustomTeam200 ?? new List<Member>(),
                        force,
                        myPuuid);
                }
                else if (GameMain.gameFlowPhase == GameFlowPhase.InProgress)
                {
                    GameSessionResponse? session = await Game_Api.GameLineInfoServer();
                    if (session?.GameData == null)
                    {
                        lblGameInfo.Text = "未获取到对局信息";
                        return;
                    }

                    SetGameInfo("", 0);
                    // 对局中通过当前召唤师接口获取 puuid
                    string? myPuuid = await GetMyPuuidAsync();
                    RenderTeams(
                        session.GameData.TeamOne ?? new List<TeamMember>(),
                        session.GameData.TeamTwo ?? new List<TeamMember>(),
                        force,
                        myPuuid);
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        /// <summary>
        /// 获取当前登录召唤师的 puuid（带缓存，用于判断队友/对手）。
        /// </summary>
        private async Task<string?> GetMyPuuidAsync()
        {
            if (!string.IsNullOrEmpty(_myPuuid)) return _myPuuid;
            try
            {
                string json = await Assets_api.GetUser();
                if (!string.IsNullOrEmpty(json))
                {
                    var info = JsonConvert.DeserializeObject<Plyaer>(json);
                    _myPuuid = info?.puuid;
                }
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

        private void RenderTeams(List<Member> team1, List<Member> team2, bool force, string? myPuuid)
        {
            RenderTeamsCore(
                team1.Select(m => (m.Puuid, m.SummonerName, m.IsBot ? m.BotChampionId : 0, m.FirstPositionPreference, m.IsBot)).ToList(),
                team2.Select(m => (m.Puuid, m.SummonerName, m.IsBot ? m.BotChampionId : 0, m.FirstPositionPreference, m.IsBot)).ToList(),
                force,
                myPuuid);
        }

        private void RenderTeams(List<TeamMember> team1, List<TeamMember> team2, bool force, string? myPuuid)
        {
            RenderTeamsCore(
                team1.Select(m => (m.Puuid, m.SummonerName, m.ChampionId, m.SelectedPosition, false)).ToList(),
                team2.Select(m => (m.Puuid, m.SummonerName, m.ChampionId, m.SelectedPosition, false)).ToList(),
                force,
                myPuuid);
        }

        private void RenderTeamsCore(
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            bool force,
            string? myPuuid)
        {
            string signature = string.Join(
                ",",
                team1.Concat(team2)
                    .Select(t => t.Puuid)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .OrderBy(p => p, StringComparer.Ordinal));

            // 阵容未变化时跳过重建，避免自动刷新反复销毁/重建控件
            if (!force && signature == _lastSignature && panelTeam1.Controls.Count > 0)
                return;
            _lastSignature = signature;

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

            int width = Math.Min(
                PlayerCardMaxWidth,
                Math.Max(300, Math.Min(panelTeam1.ClientSize.Width, panelTeam2.ClientSize.Width) - 24));

            panelTeam1.SuspendLayout();
            panelTeam2.SuspendLayout();
            try
            {
                panelTeam1.Controls.Clear();
                panelTeam2.Controls.Clear();

                AddPlayerCards(panelTeam1, team1, width, myPuuid);
                AddPlayerCards(panelTeam2, team2, width, myPuuid);
            }
            finally
            {
                panelTeam1.ResumeLayout();
                panelTeam2.ResumeLayout();
            }

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
            _ = ApplyPremadeDetectionAsync(team1, team2, signature);
        }

        /// <summary>
        /// 异步执行开黑检测：拉取每人近期战绩，统计同队次数后更新表头与卡片标记。
        /// 阵容未变化时跳过，避免自动刷新反复计算。
        /// </summary>
        private async Task ApplyPremadeDetectionAsync(
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
            string signature)
        {
            if (_premadeSignature == signature) return;
            _premadeSignature = signature;

            try
            {
                var result = await PremadeDetector.DetectAsync(
                    team1.Select(m => (m.Puuid, m.Name)).ToList(),
                    team2.Select(m => (m.Puuid, m.Name)).ToList());

                // 等待期间阵容已变化则丢弃本次结果
                if (IsDisposed || signature != _lastSignature) return;
                ApplyPremadeResult(result);
            }
            catch
            {
                // 开黑检测失败不影响对局展示
            }
        }

        /// <summary>
        /// 将开黑检测结果应用到表头与玩家卡片。
        /// </summary>
        private void ApplyPremadeResult(PremadeDetector.PremadeResult result)
        {
            string summary1 = result.GetTeamSummary(0);
            string summary2 = result.GetTeamSummary(1);
            lblTeamTitle1.Text = string.IsNullOrEmpty(summary1)
                ? _teamTitleBase1
                : $"{_teamTitleBase1} · 开黑 {summary1}";
            lblTeamTitle2.Text = string.IsNullOrEmpty(summary2)
                ? _teamTitleBase2
                : $"{_teamTitleBase2} · 开黑 {summary2}";

            ApplyPremadeToPanel(panelTeam1, result);
            ApplyPremadeToPanel(panelTeam2, result);
        }

        /// <summary>
        /// 给单个队伍面板中的卡片设置/清除开黑标记。
        /// </summary>
        private static void ApplyPremadeToPanel(
            Control panel,
            PremadeDetector.PremadeResult result)
        {
            foreach (Control card in panel.Controls)
            {
                if (card is not LivePlayerForm player || player.Puuid == null) continue;
                var group = result.GroupByPuuid.GetValueOrDefault(player.Puuid);
                player.SetPremadeGroup(group?.Index, group?.Names);
            }
        }

        private static void AddPlayerCards(
            FlowLayoutPanel panel,
            List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> members,
            int width,
            string? myPuuid)
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
                bool isAlly = myPuuid != null && member.Puuid == myPuuid;
                bool teamKnown = myPuuid != null;
                var card = new LivePlayerForm(
                    member.Puuid,
                    member.Name,
                    member.ChampionId,
                    member.Position,
                    member.IsBot,
                    isAlly,
                    teamKnown)
                {
                    Width = width,
                    Height = PlayerCardHeight,
                    Margin = new Padding(0, 0, 10, 10)
                };
                panel.Controls.Add(card);
            }
        }
    }
}
