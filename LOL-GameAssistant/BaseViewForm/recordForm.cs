using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
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
        /// 安全的 Image.FromStream 替代
        /// </summary>
        private static Image LoadImageSafe(Stream stream)
        {
            if (stream == null || stream == Stream.Null || stream.Length == 0)
                return null!;
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }

        /// <summary>
        /// 安全释放旧 Image
        /// </summary>
        private static void DisposeOldImage(Image? img)
        {
            if (img != null)
            {
                try { img.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 安全设置 PictureBox 图像
        /// </summary>
        private static async Task SetPictureImage(PictureBox pic, Task<Stream> streamTask)
        {
            try
            {
                var stream = await streamTask;
                if (stream == null || stream == Stream.Null || stream.Length == 0) return;
                DisposeOldImage(pic.Image);
                pic.Image = LoadImageSafe(stream);
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"加载图标失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载信息
        /// </summary>
        public async Task setInfo(GameHeadModel.GameInfo head, String? puuid)
        {
            if (head == null) return;

            try
            {
                // 获取比赛详情
                GameDetailModel.GameInfo? gameInfo = await Game_Api.GetGameDetail(Convert.ToString(head.GameId));
                if (gameInfo == null) return;

                // 找到玩家身份
                var identity = gameInfo.participantIdentities
                    ?.FirstOrDefault(p => p?.player?.puuid == puuid);
                if (identity == null) return;

                // 找到玩家游戏数据
                GameDetailModel.ParticipantsItem? gamer = gameInfo.participants
                    ?.FirstOrDefault(p => p?.participantId == identity.participantId);
                if (gamer?.stats == null) return;

                var stats = gamer.stats;

                // 游戏详情（大小写不敏感胜负判断）
                bool isWin = string.Equals(stats.win, "true", StringComparison.OrdinalIgnoreCase);
                this.BackColor = isWin
                    ? System.Drawing.Color.FromArgb(250, 250, 250)
                    : System.Drawing.Color.FromArgb(242, 242, 242);

                this.game_win.Text = isWin ? "胜利" : "失败";
                this.game_type.Text = gameInfo.queueId;
                this.game_time.Text = (gameInfo.gameCreationDate?.Length >= 10)
                    ? gameInfo.gameCreationDate.Substring(0, 10) : "";
                this.game_dj.Text = Convert.ToString(stats.champLevel);

                // 玩家名称
                this.game_name.Text = gameInfo.participantIdentities
                    ?.FirstOrDefault(p => p?.player?.puuid == puuid)
                    ?.player?.gameName ?? "";

                this.game_msg.Text = $"{stats.kills}/{stats.deaths}/{stats.assists}";

                // 英雄图标
                await SetPictureImage(this.game_pic, Game_Api.GetGameYXImg(gamer.championId));
                // 召唤师技能图标
                await SetPictureImage(this.pic_D, Game_Api.GetGameZHSJNImg(gamer.Spell1Id?.ToString() ?? ""));
                await SetPictureImage(this.pic_F, Game_Api.GetGameZHSJNImg(gamer.Spell2Id?.ToString() ?? ""));
                // 装备图标
                await SetPictureImage(this.pic_1, Game_Api.GetGameZBImg(stats.item0.ToString()));
                await SetPictureImage(this.pic_2, Game_Api.GetGameZBImg(stats.item1.ToString()));
                await SetPictureImage(this.pic_3, Game_Api.GetGameZBImg(stats.item2.ToString()));
                await SetPictureImage(this.pic_4, Game_Api.GetGameZBImg(stats.item3.ToString()));
                await SetPictureImage(this.pic_5, Game_Api.GetGameZBImg(stats.item4.ToString()));
                await SetPictureImage(this.pic_6, Game_Api.GetGameZBImg(stats.item5.ToString()));
                await SetPictureImage(this.pic_7, Game_Api.GetGameZBImg(stats.item6.ToString()));
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"加载比赛记录详情失败: {ex.Message}");
            }
        }

        private void AttachDoubleClickToAllControls(Control parent)
        {
            parent.DoubleClick += FormOrControl_DoubleClick;

            foreach (Control child in parent.Controls)
            {
                AttachDoubleClickToAllControls(child);
            }
        }

        private void FormOrControl_DoubleClick(object sender, EventArgs e)
        {
            if (ParentForm != null)
                AntdUI.Message.warn(ParentForm, "敬请期待！");
        }
    }
}
