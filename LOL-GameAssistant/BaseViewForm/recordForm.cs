using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using System.Data;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>首页单场战绩卡片，支持查看本局详情。</summary>
    public partial class RecordForm : UserControl, IThemeAware
    {
        private MatchDetail? _gameDetail;
        private string? _playerPuuid;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IGameAssetService _gameAssetService;
        private readonly AntdUI.Label _performanceTag = new();
        private readonly ToolTip _performanceTip = new();
        private readonly Dictionary<Control, Image> _ownedImages = new();
        private bool? _isWin;
        private RecentModePerformanceAssessment? _performanceAssessment;

        /// <summary>初始化 RecordForm 的实例状态。</summary>
        public RecordForm() : this(AppCompositionRoot.MatchHistoryService, AppCompositionRoot.GameAssetService)
        {
        }

        /// <summary>战绩卡片仅依赖应用服务，图像解码保留在 WinForms 表现层。</summary>
        internal RecordForm(IMatchHistoryService matchHistoryService, IGameAssetService gameAssetService)
        {
            _matchHistoryService = matchHistoryService;
            _gameAssetService = gameAssetService;
            InitializeComponent();
            _performanceTag.AutoSize = false;
            _performanceTag.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _performanceTag.TextAlign = ContentAlignment.MiddleCenter;
            _performanceTag.Location = new Point(506, 30);
            _performanceTag.Size = new Size(80, 22);
            Controls.Add(_performanceTag);
            _performanceTag.BringToFront();
            AttachDoubleClickToAllControls(this);
            Disposed += (_, _) =>
            {
                _performanceTip.Dispose();
                foreach (Image image in _ownedImages.Values) image.Dispose();
                _ownedImages.Clear();
            };
        }

        /// <summary>加载首页战绩卡片的英雄及对局信息。</summary>
        private void RecordForm_Load(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 加载单场战绩信息（支持传入已缓存的对局详情，避免重复请求）。
        /// </summary>
        public async Task setInfo(MatchHistoryGame? head, String? puuid, MatchDetail? detail = null)
        {
            if (head == null || string.IsNullOrEmpty(puuid)) return;
            _playerPuuid = puuid;

            _gameDetail = detail ?? await _matchHistoryService.GetDetailAsync(head.GameId);
            if (_gameDetail == null) return;

            var gamer = _gameDetail.GetParticipant(puuid);
            if (gamer == null) return;

            bool win = gamer.IsWin();
            _isWin = win;
            ApplyTheme(UiTheme.Palette);
            try
            {
                //头像
                ReplaceImage(game_pic, await LoadImageAsync(() => _gameAssetService.GetChampionIconAsync(gamer.championId)));
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
                await ApplyRecentModePerformanceTagAsync(_gameDetail, puuid);
                ReplaceImage(pic_D, await LoadImageAsync(() => _gameAssetService.GetSummonerSpellIconAsync(gamer.Spell1Id)));
                ReplaceImage(pic_F, await LoadImageAsync(() => _gameAssetService.GetSummonerSpellIconAsync(gamer.Spell2Id)));
                //游戏装备
                if (gamer.stats != null)
                {
                    int[] items = { gamer.stats.item0, gamer.stats.item1, gamer.stats.item2, gamer.stats.item3, gamer.stats.item4, gamer.stats.item5, gamer.stats.item6 };
                    PictureBox[] boxes = { pic_1, pic_2, pic_3, pic_4, pic_5, pic_6, pic_7 };
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        int itemId = items[i];
                        ReplaceImage(boxes[i], await LoadImageAsync(() => _gameAssetService.GetItemIconAsync(itemId)));
                    }
                }
                BuildTeamAvatars(_gameDetail, puuid);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\u52a0\u8f7d\u56fe\u7247\u5931\u8d25: {ex.Message}");
            }
        }

        /// <summary>最近 30 天同模式全部有效对局共用统一评分逻辑。</summary>
        private async Task ApplyRecentModePerformanceTagAsync(MatchDetail currentGame, string puuid)
        {
            string queueText = string.IsNullOrWhiteSpace(currentGame.queueId)
                ? currentGame._queueId : currentGame.queueId;
            int.TryParse(queueText, out int queueId);
            var service = new RecentModePerformanceService(_matchHistoryService);
            RecentModePerformanceAssessment assessment = await service.EvaluateAsync(
                puuid, queueId, currentGame.gameMode, currentGame.GetModeText());
            ApplyPostGamePerformanceTag(assessment);
        }

        /// <summary>将已结束对局的表现结果显示为状态标签。</summary>
        private void ApplyPostGamePerformanceTag(RecentModePerformanceAssessment assessment)
        {
            _performanceAssessment = assessment;
            if (!assessment.HasEnoughSample)
            {
                _performanceTag.Text = "样本不足";
                ApplyTheme(UiTheme.Palette);
                _performanceTip.SetToolTip(_performanceTag, assessment.Detail);
                return;
            }

            string tag = RecentPerformanceLabelFormatter.GetText(assessment);
            _performanceTag.Text = tag;
            ApplyTheme(UiTheme.Palette);
            _performanceTip.SetToolTip(_performanceTag, $"同模式近期表现 · {assessment.Score} 分\n{assessment.Detail}");
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.IsDark ? (_isWin == false ? palette.SurfaceMuted : palette.SurfaceRaised)
                : _isWin == false ? Color.FromArgb(242, 242, 242) : Color.FromArgb(250, 250, 250);
            if (_performanceAssessment is not { HasEnoughSample: true } assessment)
            {
                _performanceTag.BackColor = palette.SurfaceMuted;
                _performanceTag.ForeColor = palette.TextSecondary;
                return;
            }

            (_performanceTag.BackColor, _performanceTag.ForeColor) = (assessment.Label, palette.IsDark) switch
            {
                (RecentPerformanceLabel.Upper, true) => (Color.FromArgb(38, 75, 58), Color.FromArgb(145, 220, 164)),
                (RecentPerformanceLabel.Human, true) => (Color.FromArgb(65, 48, 79), Color.FromArgb(205, 164, 231)),
                (RecentPerformanceLabel.Lower, true) => (Color.FromArgb(82, 48, 52), Color.FromArgb(245, 164, 172)),
                (RecentPerformanceLabel.Upper, false) => (Color.FromArgb(232, 245, 233), Color.FromArgb(27, 94, 32)),
                (RecentPerformanceLabel.Human, false) => (Color.FromArgb(243, 229, 245), Color.FromArgb(123, 31, 162)),
                (RecentPerformanceLabel.Lower, false) => (Color.FromArgb(255, 235, 238), Color.FromArgb(183, 28, 28)),
                _ => (palette.SurfaceMuted, palette.TextSecondary)
            };
        }

        /// <summary>
        /// 构建本局 10 名玩家头像：区分我方（含自己，金色描边）与敌方。
        /// </summary>
        private void BuildTeamAvatars(MatchDetail detail, string? puuid)
        {
            ControlLifetime.ClearAndDispose(flowAlly);
            ControlLifetime.ClearAndDispose(flowEnemy);

            var participants = detail.participants;
            var identities = detail.participantIdentities;
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
                avatar.Disposed += (_, _) => avatar.Image?.Dispose();
                var tip = new ToolTip();
                avatar.Disposed += (_, _) => tip.Dispose();
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
        private async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            Image? icon = await LoadImageAsync(() => _gameAssetService.GetChampionIconAsync(championId));
            if (icon != null && !box.IsDisposed)
            {
                box.Image = icon;
            }
            else
            {
                icon?.Dispose();
            }
        }

        /// <summary>
        /// 异步加载图片：复制到 MemoryStream 后转为独立 Bitmap，避免流被释放导致 GDI+ 报错。
        /// </summary>
        private static async Task<Image?> LoadImageAsync(Func<Task<GameAsset?>> loader)
        {
            try
            {
                GameAsset? asset = await loader();
                if (asset == null || asset.IsEmpty) return null;
                using var ms = new MemoryStream(asset.Content);
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
        private void ReplaceImage(PictureBox box, Image? image)
        {
            if (_ownedImages.Remove(box, out Image? old)) old.Dispose();
            box.Image = image;
            if (image != null) _ownedImages[box] = image;
        }

        /// <summary>替换控件图像并释放旧图像资源。</summary>
        private void ReplaceImage(AntdUI.Avatar box, Image? image)
        {
            if (_ownedImages.Remove(box, out Image? old)) old.Dispose();
            box.Image = image;
            if (image != null) _ownedImages[box] = image;
        }

        /// <summary>为战绩卡片及子控件统一绑定双击操作。</summary>
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

        /// <summary>响应战绩卡片双击并打开对应对局详情。</summary>
        private void FormOrControl_DoubleClick(object? sender, EventArgs e)
        {
            //双击打开对局详情
            if (_gameDetail == null || string.IsNullOrEmpty(_playerPuuid)) return;
            MatchDetailForm.OpenAndHandle(_gameDetail, _playerPuuid, this);
        }
    }
}
