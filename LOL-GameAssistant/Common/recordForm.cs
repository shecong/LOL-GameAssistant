using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static log4net.Appender.RollingFileAppender;
using static LOL_GameAssistant.Models.GameDetailModel;

namespace LOL_GameAssistant
{
    public partial class recordForm : UserControl
    {
        public recordForm()
        {
            InitializeComponent();
        }

        private void recordForm_Load(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 加载信息
        /// </summary>
        public void setInfo(GameHeadModel.GameInfo head,String? puuid)
        {
            if (head == null) return;
            //根据表头获取明细信息
            GameDetailModel.GameInfo? gameInfo = new GameDetailModel.GameInfo();
            gameInfo = Game_Api.GetGameDetail(Convert.ToString(head.GameId));
            if (gameInfo == null) return;
            

            //游戏数据
            GameDetailModel.ParticipantsItem? gamer = gameInfo.participants.Where(p => p.participantId == gameInfo.participantIdentities.Where(p => p.player.puuid == puuid).FirstOrDefault().participantId).FirstOrDefault<GameDetailModel.ParticipantsItem>();
            if (gamer == null) return;

            //游戏详情
            //头像
            this.game_pic.Image = Image.FromStream(Game_Api.GetGameYXImg(gamer.championId));
            this.game_win.Text = gamer.stats.win == "true" ? "胜利" : "失败";
            this.game_type.Text = gameInfo.gameMode;
            this.game_time.Text = gameInfo.gameCreationDate.Substring(0,10);
            this.game_dj.Text = Convert.ToString(gamer.stats.champLevel);
            this.game_name.Text= gameInfo.participantIdentities.Where(p => p.player.puuid == puuid).FirstOrDefault().player.gameName;
            this.game_msg.Text = $"{gamer.stats.kills}/{gamer.stats.deaths}/{gamer.stats.assists}";
            this.pic_D.Image = Image.FromStream(Game_Api.GetGameZHSJNImg(gamer.Spell1Id.ToString()));
            this.pic_F.Image = Image.FromStream(Game_Api.GetGameZHSJNImg(gamer.Spell2Id.ToString()));
            //游戏装备
            this.pic_1.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item0.ToString()));
            this.pic_2.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item1.ToString()));
            this.pic_3.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item2.ToString()));
            this.pic_4.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item3.ToString()));
            this.pic_5.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item4.ToString()));
            this.pic_6.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item5.ToString()));
            this.pic_7.Image = Image.FromStream(Game_Api.GetGameZBImg(gamer.stats.item6.ToString()));
        }
    }
}