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
            timer.Interval = 5000; // 1秒
            timer.Tick += async (s, e) => await AddView();
            timer.Start();
        }

        private async Task AddView()
        {
            LobbyGameInfo? gameInfo = await Game_Api.GameNowServer();
            if (gameInfo == null) return;
            //添加所有玩家信息
            foreach (var member in gameInfo.Members)
            {
                LivePlayerForm live = new LivePlayerForm(member.Puuid);
                this.Controls.Add(live);
            }
        }
    }
}