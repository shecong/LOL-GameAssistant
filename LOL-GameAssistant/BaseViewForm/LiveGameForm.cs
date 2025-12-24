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
        private System.Windows.Forms.Timer timer = new();

        public LiveGameForm()
        {
            InitializeComponent();
            this.Load += LiveGameForm_Load;
        }

        /// <summary>
        /// 加载游戏对局信息
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LiveGameForm_Load(object? sender, EventArgs e)
        {
            timer.Interval = 10000; // 1秒
            timer.Tick += async (s, e) => await AddView();
            //timer.Start();
        }

        public async Task AddView()
        {
            if (GameMain.gameFlowPhase == GameFlowPhase.ChampSelect || GameMain.gameFlowPhase == GameFlowPhase.Lobby)
            {
                LobbyGameInfo? gameInfo = await Game_Api.GameNowServer();
                if (gameInfo == null) return;
                this.gridPanel1.Controls.Clear();
                this.gridPanel2.Controls.Clear();
                //添加所有玩家信息
                for (int i = 0; i < gameInfo.GameConfig.CustomTeam100.Count; i++)
                {
                    if (gameInfo.GameConfig.CustomTeam100[i].Puuid.Trim() == "") continue;
                    LivePlayerForm live = new LivePlayerForm(gameInfo.GameConfig.CustomTeam100[i].Puuid);
                    this.gridPanel1.Controls.Add(live);
                }
                for (int i = 0; i < gameInfo.GameConfig.CustomTeam200.Count; i++)
                {
                    if (gameInfo.GameConfig.CustomTeam200[i].Puuid.Trim() == "") continue;
                    LivePlayerForm live = new LivePlayerForm(gameInfo.GameConfig.CustomTeam200[i].Puuid);
                    this.gridPanel2.Controls.Add(live);
                }
            }
            else if (GameMain.gameFlowPhase == GameFlowPhase.InProgress)
            {
                GameSessionResponse? gameInfo = await Game_Api.GameLineInfoServer();
                if (gameInfo == null) return;
                this.gridPanel1.Controls.Clear();
                this.gridPanel2.Controls.Clear();
                //添加所有玩家信息
                for (int i = 0; i < gameInfo.GameData.TeamOne.Count; i++)
                {
                    if (gameInfo.GameData.TeamOne[i].Puuid.Trim() == "") continue;
                    LivePlayerForm live = new LivePlayerForm(gameInfo.GameData.TeamOne[i].Puuid);
                    this.gridPanel1.Controls.Add(live);
                }
                for (int i = 0; i < gameInfo.GameData.TeamTwo.Count; i++)
                {
                    if (gameInfo.GameData.TeamTwo[i].Puuid.Trim() == "") continue;
                    LivePlayerForm live = new LivePlayerForm(gameInfo.GameData.TeamTwo[i].Puuid);
                    this.gridPanel2.Controls.Add(live);
                }
            }
        }
    }
}