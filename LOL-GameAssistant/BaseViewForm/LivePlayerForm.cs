using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Text;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class LivePlayerForm : UserControl
    {
        private string? Playerpuuid;

        public LivePlayerForm(string _Playerpuuid)
        {
            InitializeComponent();
            Playerpuuid = _Playerpuuid;
            this.Load += LivePlayerForm_Load;
        }

        private async void LivePlayerForm_Load(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(Playerpuuid))
            {
                return;
            }
            //根据传入进来的玩家id进行加载信息
            //获取玩家近十场的游戏记录
            String begIndex = "0";
            String endIndex = "10";
            GameHeadModel.MatchHistoryResponse? matchlists = await Game_Api.GetUserGame(Playerpuuid, begIndex, endIndex);

            if (matchlists != null)
            {
                var sortedList = matchlists.Games.Games
               .OrderByDescending(p => p.GameCreation).Take(9)
               .OrderBy(p => p.GameCreation)
               .ToList();
                //初始加载玩家游戏
                for (int i = 0; i < sortedList.Count; i++)
                {
                    //获取每一场的游戏记录
                    //根据表头获取明细信息
                    GameDetailModel.GameInfo? gameInfo = new GameDetailModel.GameInfo();
                    gameInfo = await Game_Api.GetGameDetail(Convert.ToString(sortedList[i].GameId));
                    if (gameInfo == null) return;
                    //游戏数据
                    GameDetailModel.ParticipantsItem? gamer = gameInfo.participants.Where(p => p.participantId == gameInfo.participantIdentities.Where(p => p.player.puuid == Playerpuuid).FirstOrDefault().participantId).FirstOrDefault<GameDetailModel.ParticipantsItem>();
                    if (gamer == null) return;
                    //加载明细
                    LivePlayersForm livePlayersForm = new LivePlayersForm("明细", gameInfo.queueId, ChampionMap.GetChampion(gamer.championId).RealName, gameInfo.gameCreationDate.Substring(0, 10), $"{gamer.stats.kills}/{gamer.stats.deaths}/{gamer.stats.assists}", gamer.stats.win == "true" ? "win" : "loss");
                    this.gridPanel1.Controls.Add(livePlayersForm);
                    //最后一个加载头部
                    if (i == sortedList.Count - 1)
                    {
                        var palyername = gameInfo.participantIdentities?.Where(p => p.player.puuid == Playerpuuid).FirstOrDefault()?.player.gameName;
                        var puuid = gameInfo.participantIdentities?.Where(p => p.player.puuid == Playerpuuid).FirstOrDefault()?.player.puuid;

                        LivePlayersForm palyerLive = new LivePlayersForm("头部", palyername, puuid, "", "", "");
                        this.gridPanel1.Controls.Add(palyerLive);
                    }
                }
            }
        }
    }
}