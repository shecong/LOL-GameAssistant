using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Entity;
using System.Data;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class recordForm : UserControl
    {
        public recordForm()
        {
            InitializeComponent();
            AttachDoubleClickToAllControls(this);
        }

        private void recordForm_Load(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 加载信息
        /// </summary>
        public async Task setInfo(GameHeadModel.GameInfo head, String? puuid)
        {
            if (head == null) return;
            //根据表头获取明细信息
            GameDetailModel.GameInfo? gameInfo = new GameDetailModel.GameInfo();
            gameInfo = await Game_Api.GetGameDetail(Convert.ToString(head.GameId));
            if (gameInfo == null) return;

            //游戏数据
            GameDetailModel.ParticipantsItem? gamer = gameInfo.participants.Where(p => p.participantId == gameInfo.participantIdentities.Where(p => p.player.puuid == puuid).FirstOrDefault().participantId).FirstOrDefault<GameDetailModel.ParticipantsItem>();
            if (gamer == null) return;

            //游戏详情
            this.BackColor = gamer.stats.win == "true" ? System.Drawing.Color.FromArgb(225, 245, 233) : System.Drawing.Color.FromArgb(254, 235, 234);
            //头像
            this.game_pic.Image = Image.FromStream(await Game_Api.GetGameYXImg(gamer.championId));
            this.game_win.Text = gamer.stats.win == "true" ? "胜利" : "失败";
            this.game_type.Text = gameInfo.queueId;
            this.game_time.Text = gameInfo.gameCreationDate.Substring(0, 10);
            this.game_dj.Text = Convert.ToString(gamer.stats.champLevel);
            this.game_name.Text = gameInfo.participantIdentities.Where(p => p.player.puuid == puuid).FirstOrDefault().player.gameName;
            this.game_msg.Text = $"{gamer.stats.kills}/{gamer.stats.deaths}/{gamer.stats.assists}";
            this.pic_D.Image = Image.FromStream(await Game_Api.GetGameZHSJNImg(gamer.Spell1Id.ToString()));
            this.pic_F.Image = Image.FromStream(await Game_Api.GetGameZHSJNImg(gamer.Spell2Id.ToString()));
            //游戏装备
            this.pic_1.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item0.ToString()));
            this.pic_2.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item1.ToString()));
            this.pic_3.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item2.ToString()));
            this.pic_4.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item3.ToString()));
            this.pic_5.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item4.ToString()));
            this.pic_6.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item5.ToString()));
            this.pic_7.Image = Image.FromStream(await Game_Api.GetGameZBImg(gamer.stats.item6.ToString()));
        }

        private void AttachDoubleClickToAllControls(Control parent)
        {
            // 为父控件本身添加双击事件
            parent.DoubleClick += FormOrControl_DoubleClick;

            // 递归为所有子控件添加双击事件
            foreach (Control child in parent.Controls)
            {
                AttachDoubleClickToAllControls(child);
            }
        }

        private void FormOrControl_DoubleClick(object sender, EventArgs e)
        {
            //双击查看详情
            AntdUI.Message.warn(ParentForm!, "敬请期待！");
        }
    }
}