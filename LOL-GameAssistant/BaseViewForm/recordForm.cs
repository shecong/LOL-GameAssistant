using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
using System.Data;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class recordForm : UserControl
    {
        private GameDetailModel.GameInfo? _gameDetail;
        private string? _playerPuuid;
        public recordForm()
        {
            InitializeComponent();
            AttachDoubleClickToAllControls(this);
        }

        private void recordForm_Load(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 加载单场战绩信息（支持传入已缓存的对局详情，避免重复请求）。
        /// </summary>
        public async Task setInfo(GameHeadModel.GameInfo? head, String? puuid, GameDetailModel.GameInfo? detail = null)
        {
            if (head == null || string.IsNullOrEmpty(puuid)) return;
            _playerPuuid = puuid;

            _gameDetail = detail ?? await Game_Api.GetGameDetail(head.GameId);
            if (_gameDetail == null) return;

            var gamer = _gameDetail.GetParticipant(puuid);
            if (gamer == null) return;

            bool win = gamer.IsWin();
            this.BackColor = win ? System.Drawing.Color.FromArgb(250, 250, 250) : System.Drawing.Color.FromArgb(242, 242, 242);
            try
            {
                //头像
                ReplaceImage(game_pic, await LoadImageAsync(() => Game_Api.GetGameYXImg(gamer.championId)));
                this.game_win.Text = win ? "胜利" : "失败";
                this.game_win.ForeColor = win ? System.Drawing.Color.FromArgb(76, 175, 80) : System.Drawing.Color.FromArgb(244, 67, 54);
                this.game_type.Text = _gameDetail.GetModeText();
                this.game_time.Text = (_gameDetail.gameCreationDate?.Length >= 10 ? _gameDetail.gameCreationDate.Substring(0, 10) : _gameDetail.gameCreationDate) ?? "未知";
                this.game_dj.Text = Convert.ToString(gamer.stats?.champLevel);
                this.game_name.Text = _gameDetail.GetPlayerIdentity(puuid)?.gameName ?? "未知";
                this.game_msg.Text = gamer.GetKdaText();
                this.game_duration.Text = _gameDetail.GetDurationText();
                var stats = gamer.stats;
                int cs = (stats?.totalMinionsKilled ?? 0) + (stats?.neutralMinionsKilled ?? 0);
                this.game_cs.Text = $"补刀 {cs}";
                this.game_damage.Text = $"伤害 {stats?.totalDamageDealtToChampions ?? 0}";
                this.game_gold.Text = $"金币 {stats?.goldEarned ?? 0}";
                ReplaceImage(pic_D, await LoadImageAsync(() => Game_Api.GetGameZHSJNImg(gamer.Spell1Id)));
                ReplaceImage(pic_F, await LoadImageAsync(() => Game_Api.GetGameZHSJNImg(gamer.Spell2Id)));
                //游戏装备
                if (gamer.stats != null)
                {
                    int[] items = { gamer.stats.item0, gamer.stats.item1, gamer.stats.item2, gamer.stats.item3, gamer.stats.item4, gamer.stats.item5, gamer.stats.item6 };
                    PictureBox[] boxes = { pic_1, pic_2, pic_3, pic_4, pic_5, pic_6, pic_7 };
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        int itemId = items[i];
                        ReplaceImage(boxes[i], await LoadImageAsync(() => Game_Api.GetGameZBImg(itemId.ToString())));
                    }
                }
                BuildTeamAvatars(_gameDetail, puuid);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\u52a0\u8f7d\u56fe\u7247\u5931\u8d25: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建本局 10 名玩家头像：区分我方（含自己，金色描边）与敌方。
        /// </summary>
        private void BuildTeamAvatars(GameDetailModel.GameInfo detail, string? puuid)
        {
            flowAlly.Controls.Clear();
            flowEnemy.Controls.Clear();

            var participants = detail.participants ?? new List<GameDetailModel.ParticipantsItem>();
            var identities = detail.participantIdentities ?? new List<GameDetailModel.ParticipantIdentitiesItem>();
            int myTeamId = detail.GetParticipant(puuid)?.teamId ?? 100;

            foreach (var p in participants.OrderBy(p => p.participantId))
            {
                var identity = identities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
                bool isMe = identity?.puuid == puuid;
                string name = isMe ? "我" : (identity?.gameName ?? $"玩家{p.participantId}");

                var avatar = new RoundPictureBox
                {
                    Size = new Size(30, 30),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderWidth = isMe ? 3 : 1,
                    BorderColor = isMe ? Color.FromArgb(255, 193, 7) : Color.FromArgb(160, 255, 255, 255),
                    Margin = new Padding(0, 0, 4, 0)
                };
                var tip = new ToolTip();
                string? playerPuuid = identity?.puuid;
                tip.SetToolTip(avatar, string.IsNullOrEmpty(playerPuuid)
                    ? name
                    : $"{name}（点击查询该玩家战绩）");

                // 点击其他玩家头像 → 跳转到战绩查询该玩家
                if (!string.IsNullOrEmpty(playerPuuid))
                {
                    avatar.Cursor = Cursors.Hand;
                    avatar.Click += (_, _) => _ = BattleQueryForm.QueryPlayerAsync(playerPuuid);
                }

                int championId = p.championId;
                _ = LoadAvatarAsync(avatar, championId);

                if (p.teamId == myTeamId)
                    flowAlly.Controls.Add(avatar);
                else
                    flowEnemy.Controls.Add(avatar);
            }
        }

        /// <summary>
        /// 异步加载英雄头像到圆形控件（全局缓存，不重复下载）。
        /// </summary>
        private static async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            var icon = await Game_Api.GetGameChampionIconAsync(championId);
            if (icon != null && !box.IsDisposed)
            {
                box.Image = icon;
            }
        }

        /// <summary>
        /// 异步加载图片：复制到 MemoryStream 后转为独立 Bitmap，避免流被释放导致 GDI+ 报错。
        /// </summary>
        private static async Task<Image?> LoadImageAsync(Func<Task<Stream>> loader)
        {
            try
            {
                using Stream? stream = await loader();
                if (stream == null || stream == Stream.Null) return null;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                using var temp = Image.FromStream(ms);
                return new Bitmap(temp);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 替换控件图片并释放旧图，避免内存泄漏。
        /// </summary>
        private static void ReplaceImage(PictureBox box, Image? image)
        {
            var old = box.Image;
            box.Image = image;
            old?.Dispose();
        }

        private static void ReplaceImage(AntdUI.Avatar box, Image? image)
        {
            var old = box.Image;
            box.Image = image;
            old?.Dispose();
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

        private void FormOrControl_DoubleClick(object? sender, EventArgs e)
        {
            //双击打开对局详情
            if (_gameDetail == null || string.IsNullOrEmpty(_playerPuuid)) return;
            MatchDetailForm.OpenAndHandle(_gameDetail, _playerPuuid, this);
        }
    }
}
