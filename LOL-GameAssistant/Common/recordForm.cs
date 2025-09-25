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
        public void setInfo(GameHeadModel.GameInfo head)
        {
            if (head == null) return;
            //根据表头获取明细信息
            GameDetailModel.GameInfo? gameInfo = new GameDetailModel.GameInfo();
            gameInfo = Game_Api.GetGameDetail(Convert.ToString(head.GameId));
            if (gameInfo == null) return;
            //头像
            this.game_pic.Image = Image.FromStream(Game_Api.GetGameUserImg(gameInfo.gameVersion, gameInfo.participantIdentities[0].player.profileIcon.ToString()));
            //技能

            //游戏数据
            GameDetailModel.ParticipantsItem? gamer = gameInfo.participants.Where(p => p.participantId == 1).FirstOrDefault<GameDetailModel.ParticipantsItem>();
            if (gamer == null) return;

            //游戏详情
            this.game_win.Text = gamer.stats.win == true ? "胜利" : "失败";
            this.game_type.Text = gameInfo.gameMode;
            this.game_time.Text = gameInfo.gameCreationDate;
            this.game_dj.Text = Convert.ToString(gamer.stats.champLevel);
            this.game_msg.Text = $"K:{gamer.stats.kills}D:{gamer.stats.deaths}A:{gamer.stats.assists}";
            this.pic_D.Image = Image.FromStream(Game_Api.GetGameZHSJNImg(gameInfo.gameVersion, gamer.spell1Id.ToString()));
            this.pic_F.Image = Image.FromStream(Game_Api.GetGameZHSJNImg(gameInfo.gameVersion, gamer.spell2Id.ToString()));
            //游戏装备
            this.pic_1.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item0.ToString()));
            this.pic_2.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item1.ToString()));
            this.pic_3.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item2.ToString()));
            this.pic_4.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item3.ToString()));
            this.pic_5.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item4.ToString()));
            this.pic_6.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item5.ToString()));
            this.pic_7.Image = Image.FromStream(Game_Api.GetGameZBImg(gameInfo.gameVersion, gamer.stats.item6.ToString()));
        }
    }
}