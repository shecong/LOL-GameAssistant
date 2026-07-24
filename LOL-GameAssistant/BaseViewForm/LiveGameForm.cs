using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class LiveGameForm : UserControl
    {
        /// <summary>
        /// 防重入信号量 —— 每次只允许一个 AddView 执行
        /// </summary>
        private readonly SemaphoreSlim _addViewLock = new SemaphoreSlim(1, 1);

        public LiveGameForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 清除 GridPanel 并释放子控件
        /// </summary>
        private void ClearPanel(AntdUI.GridPanel panel)
        {
            panel.BeginInvoke(new Action(() =>
            {
                // 先收集再 Dispose 和 Clear，避免在枚举过程中修改集合
                var controls = panel.Controls.Cast<Control>().ToList();
                foreach (Control c in controls)
                {
                    try { c.Dispose(); } catch { }
                }
                panel.Controls.Clear();
            }));
        }

        public async Task AddView()
        {
            // 防重入
            if (!await _addViewLock.WaitAsync(0)) return;

            try
            {
                // 捕获当前阶段快照以避免 TOCTOU
                var phase = GameMain.gameFlowPhase;

                if (phase == GameFlowPhase.ChampSelect || phase == GameFlowPhase.Lobby)
                {
                    LobbyGameInfo? gameInfo = await Game_Api.GameNowServer();
                    if (gameInfo?.GameConfig == null) return;

                    ClearPanel(this.gridPanel1);
                    ClearPanel(this.gridPanel2);

                    // 添加 Team100 玩家
                    var team100 = gameInfo.GameConfig.CustomTeam100;
                    if (team100 != null)
                    {
                        for (int i = 0; i < team100.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(team100[i].Puuid)) continue;
                            LivePlayerForm live = new LivePlayerForm(team100[i].Puuid);
                            this.gridPanel1.BeginInvoke(new Action(() =>
                            {
                                gridPanel1.Controls.Add(live);
                            }));
                        }
                    }

                    // 添加 Team200 玩家
                    var team200 = gameInfo.GameConfig.CustomTeam200;
                    if (team200 != null)
                    {
                        for (int i = 0; i < team200.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(team200[i].Puuid)) continue;
                            LivePlayerForm live = new LivePlayerForm(team200[i].Puuid);
                            this.gridPanel2.BeginInvoke(new Action(() =>
                            {
                                gridPanel2.Controls.Add(live);
                            }));
                        }
                    }
                }
                else if (phase == GameFlowPhase.InProgress)
                {
                    GameSessionResponse? gameInfo = await Game_Api.GameLineInfoServer();
                    if (gameInfo?.GameData == null) return;

                    ClearPanel(this.gridPanel1);
                    ClearPanel(this.gridPanel2);

                    // 添加 TeamOne 玩家
                    var teamOne = gameInfo.GameData.TeamOne;
                    if (teamOne != null)
                    {
                        for (int i = 0; i < teamOne.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(teamOne[i].Puuid)) continue;
                            LivePlayerForm live = new LivePlayerForm(teamOne[i].Puuid);
                            this.gridPanel1.BeginInvoke(new Action(() =>
                            {
                                gridPanel1.Controls.Add(live);
                            }));
                        }
                    }

                    // 添加 TeamTwo 玩家
                    var teamTwo = gameInfo.GameData.TeamTwo;
                    if (teamTwo != null)
                    {
                        for (int i = 0; i < teamTwo.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(teamTwo[i].Puuid)) continue;
                            LivePlayerForm live = new LivePlayerForm(teamTwo[i].Puuid);
                            this.gridPanel2.BeginInvoke(new Action(() =>
                            {
                                gridPanel2.Controls.Add(live);
                            }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"刷新对局信息失败: {ex.Message}");
            }
            finally
            {
                _addViewLock.Release();
            }
        }
    }
}
