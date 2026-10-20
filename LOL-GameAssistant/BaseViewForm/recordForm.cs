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
        private readonly UiRequestScope _lifetime = new();
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
            WindowLayout.ConfigureDpi(this);
            game_win.Font = new Font(UiMetrics.FontFamily, 12F, FontStyle.Bold);
            foreach (AntdUI.Label label in Controls.OfType<AntdUI.Label>()) label.AutoSize = false;
            _performanceTag.AutoSize = false;
            _performanceTag.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            _performanceTag.TextAlign = ContentAlignment.MiddleCenter;
            _performanceTag.Location = new Point(506, 30);
            _performanceTag.Size = new Size(80, 22);
            Controls.Add(_performanceTag);
            _performanceTag.BringToFront();
            AttachDoubleClickToAllControls(this);
            Resize += (_, _) => LayoutRecord();
            LayoutRecord();
            Disposed += (_, _) =>
            {
                _lifetime.Dispose();
                _performanceTip.Dispose();
                foreach (Image image in _ownedImages.Values) image.Dispose();
                _ownedImages.Clear();
            };
        }

        /// <summary>
        /// 加载单场详情；首页传入整批摘要，供评级复用而不重新请求列表。
        /// </summary>
        public async Task setInfo(MatchHistoryGame? head, String? puuid, MatchDetail? detail = null,
            CancellationToken cancellationToken = default, MatchHistoryResponse? history = null)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            if (head == null || string.IsNullOrEmpty(puuid)) return;
            _playerPuuid = puuid;
            ShowSummary(head, puuid);
            if (history != null) await ApplyRecentModePerformanceTagAsync(head, puuid, history, cancellationToken);
            try { _gameDetail = detail ?? await PlayerHistoryRepository.For(_matchHistoryService).GetDetailAsync(head.GameId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { RuntimeDiagnostics.Report("首页详情", "暂不可用", error.Message); _gameDetail = null; }
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed) return;
            if (_gameDetail == null)
            {
                game_name.Text = "摘要 · 详情暂不可用";
                return;
            }

            var gamer = _gameDetail.GetParticipant(puuid);
            if (gamer == null) return;

            bool win = gamer.IsWin();
            _isWin = gamer.stats?.HasWinResult == true ? win : null;
            ApplyTheme(UiTheme.Palette);
            try
            {
                //头像
                ReplaceImage(game_pic, await LoadImageAsync(() => _gameAssetService.GetChampionIconAsync(gamer.championId, cancellationToken)));
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                this.game_win.Text = _isWin == null ? "胜负未知" : win ? "胜利" : "失败";
                this.game_win.ForeColor = win ? System.Drawing.Color.FromArgb(76, 175, 80) : System.Drawing.Color.FromArgb(244, 67, 54);
                this.game_type.Text = _gameDetail.GetModeText();
                this.game_time.Text = (_gameDetail.gameCreationDate?.Length >= 10 ? _gameDetail.gameCreationDate.Substring(0, 10) : _gameDetail.gameCreationDate) ?? "未知";
                this.game_dj.Text = Convert.ToString(gamer.stats?.champLevel);
                this.game_name.Text = _gameDetail.GetPlayerIdentity(puuid)?.gameName ?? "未知";
                this.game_msg.Text = gamer.stats?.HasKdaResult == true ? gamer.GetKdaText() : "KDA —";
                this.game_duration.Text = _gameDetail.GetDurationText();
                var stats = gamer.stats;
                int cs = (stats?.totalMinionsKilled ?? 0) + (stats?.neutralMinionsKilled ?? 0);
                this.game_cs.Text = $"补刀 {cs}";
                this.game_damage.Text = $"伤害 {stats?.totalDamageDealtToChampions ?? 0}";
                this.game_gold.Text = $"金币 {stats?.goldEarned ?? 0}";
                await ApplyRecentModePerformanceTagAsync(head, puuid, history, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                ReplaceImage(pic_D, await LoadImageAsync(() => _gameAssetService.GetSummonerSpellIconAsync(gamer.Spell1Id, cancellationToken)));
                ReplaceImage(pic_F, await LoadImageAsync(() => _gameAssetService.GetSummonerSpellIconAsync(gamer.Spell2Id, cancellationToken)));
                //游戏装备
                if (gamer.stats != null)
                {
                    int[] items = { gamer.stats.item0, gamer.stats.item1, gamer.stats.item2, gamer.stats.item3, gamer.stats.item4, gamer.stats.item5, gamer.stats.item6 };
                    PictureBox[] boxes = { pic_1, pic_2, pic_3, pic_4, pic_5, pic_6, pic_7 };
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        int itemId = items[i];
                        ReplaceImage(boxes[i], await LoadImageAsync(() => _gameAssetService.GetItemIconAsync(itemId, cancellationToken)));
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsDisposed) BuildTeamAvatars(_gameDetail, puuid);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\u52a0\u8f7d\u56fe\u7247\u5931\u8d25: {ex.Message}");
            }
        }

        private void ShowSummary(MatchHistoryGame head, string puuid)
        {
            var summary = head.GetParticipant(puuid);
            _isWin = summary?.stats?.HasWinResult == true ? summary.IsWin() : null;
            game_win.Text = _isWin == null ? "胜负未知" : _isWin == true ? "胜利" : "失败";
            game_type.Text = head.GetModeText();
            game_name.Text = "对局摘要 · 详情加载中";
            game_msg.Text = summary?.stats?.HasKdaResult == true ? summary.GetKdaText() : "KDA —";
            game_duration.Text = head.GameDuration > 0 ? $"{head.GameDuration / 60}:{head.GameDuration % 60:D2}" : "时长 —";
            game_time.Text = head.GameCreation > 0 && head.GameCreation <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
                ? DateTimeOffset.FromUnixTimeMilliseconds(head.GameCreation).ToLocalTime().ToString("yyyy-MM-dd") : "时间 —";
            game_cs.Text = summary?.stats == null ? "补刀 —" : $"补刀 {summary.stats.totalMinionsKilled}";
            game_damage.Text = summary?.stats == null ? "伤害 —" : $"伤害 {summary.stats.totalDamageDealtToChampions:N0}";
            game_gold.Text = summary?.stats == null ? "金币 —" : $"金币 {summary.stats.goldEarned:N0}";
            lblAlly.Visible = lblEnemy.Visible = flowAlly.Visible = flowEnemy.Visible = false;
            ApplyTheme(UiTheme.Palette);
        }

        /// <summary>按已获取摘要中最近50场同模式有效对局评分，不为卡片重复读取摘要。</summary>
        private async Task ApplyRecentModePerformanceTagAsync(MatchHistoryGame currentGame, string puuid,
            MatchHistoryResponse? history, CancellationToken token)
        {
            var service = new RecentModePerformanceService(_matchHistoryService);
            RecentModePerformanceAssessment assessment = await service.EvaluateAsync(
                puuid, currentGame.QueueId, currentGame.GameMode, currentGame.GetModeText(),
                firstPage: history, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (IsDisposed) return;
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
            BackColor = palette.IsDark ? (_isWin == false ? Color.FromArgb(55, 30, 48) : Color.FromArgb(17, 49, 62))
                : _isWin == false ? Color.FromArgb(255, 234, 237) : Color.FromArgb(230, 247, 245);
            Invalidate();
            foreach (AntdUI.Label label in Controls.OfType<AntdUI.Label>())
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = palette.TextSecondary;
            }
            game_name.ForeColor = game_msg.ForeColor = palette.TextPrimary;
            game_win.ForeColor = _isWin == false ? Color.FromArgb(239, 102, 129) : Color.FromArgb(81, 194, 142);
            lblAlly.ForeColor = palette.Accent;
            lblEnemy.ForeColor = Color.FromArgb(239, 102, 129);
            flowAlly.BackColor = flowEnemy.BackColor = Color.Transparent;
            foreach (var entry in flowAlly.Controls.Cast<Control>().Concat(flowEnemy.Controls.Cast<Control>()))
            {
                entry.BackColor = Color.Transparent;
                foreach (AntdUI.Label name in entry.Controls.OfType<AntdUI.Label>())
                {
                    name.BackColor = Color.Transparent;
                    name.ForeColor = palette.TextPrimary;
                }
            }
            foreach (PictureBox picture in Controls.OfType<PictureBox>()) picture.BackColor = palette.SurfaceMuted;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 8 || Height < 8) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color accent = _isWin == false ? Color.FromArgb(239, 102, 129) : Color.FromArgb(81, 194, 142);
            using var border = new Pen(Color.FromArgb(160, accent), 1.5F);
            using var outline = GradientPanel.RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), 10);
            e.Graphics.DrawPath(border, outline);
            using var strip = new SolidBrush(accent);
            e.Graphics.FillRectangle(strip, 2, 12, 4, Height - 24);
        }

        private void LayoutRecord()
        {
            if (IsDisposed) return;
            int Scale(int value) => value * DeviceDpi / 96;
            int width = ClientSize.Width;
            bool sideTeams = width >= Scale(900);
            Height = Scale(sideTeams ? 158 : width < Scale(680) ? 252 : 224);
            game_pic.SetBounds(Scale(14), Scale(16), Scale(56), Scale(56));
            game_win.SetBounds(Scale(82), Scale(12), Scale(66), Scale(28));
            game_type.SetBounds(Scale(155), Scale(12), Math.Max(Scale(100), width - Scale(320)), Scale(28));
            game_name.SetBounds(Scale(82), Scale(42), Scale(160), Scale(26));
            game_msg.SetBounds(Scale(252), Scale(42), Scale(140), Scale(26));
            game_time.SetBounds(Math.Max(Scale(340), width - Scale(164)), Scale(12), Scale(150), Scale(26));
            game_duration.SetBounds(Math.Max(Scale(400), width - Scale(92)), Scale(42), Scale(78), Scale(26));
            _performanceTag.SetBounds(Math.Max(Scale(390), width - Scale(190)), Scale(44), Scale(90), Scale(22));
            game_cs.SetBounds(Scale(14), Scale(78), Scale(120), Scale(24));
            game_damage.SetBounds(Scale(142), Scale(78), Scale(160), Scale(24));
            game_gold.SetBounds(Scale(310), Scale(78), Scale(140), Scale(24));
            PictureBox[] items = [pic_1, pic_2, pic_3, pic_4, pic_5, pic_6, pic_7, pic_D, pic_F];
            for (int index = 0; index < items.Length; index++)
                items[index].SetBounds(Scale(14 + index * 30), Scale(110), Scale(26), Scale(26));
            int teamTop = Scale(144);
            int teamLeft = Scale(14);
            int teamWidth = Math.Max(1, width - Scale(68));
            if (sideTeams)
            {
                // 使用战绩和装备右侧空白；日期与评分仍留在右上角。
                game_type.Width = Scale(240);
                game_gold.Width = Scale(110);
                teamLeft = Scale(430);
                teamTop = Scale(70);
                teamWidth = Math.Max(1, width - teamLeft - Scale(54));
            }
            if (width < Scale(680))
            {
                game_type.SetBounds(Scale(155), Scale(12), Math.Max(Scale(90), width - Scale(170)), Scale(28));
                game_time.SetBounds(Math.Max(Scale(245), width - Scale(164)), Scale(42), Scale(150), Scale(26));
                game_msg.SetBounds(Scale(82), Scale(70), Scale(140), Scale(26));
                game_duration.SetBounds(Scale(232), Scale(70), Scale(78), Scale(26));
                _performanceTag.SetBounds(width - Scale(104), Scale(72), Scale(90), Scale(22));
                game_cs.Top = game_damage.Top = game_gold.Top = Scale(102);
                foreach (PictureBox picture in items) picture.Top = Scale(134);
                teamTop = Scale(172);
            }
            lblAlly.SetBounds(teamLeft, teamTop, Scale(36), Scale(34));
            flowAlly.SetBounds(teamLeft + Scale(40), teamTop, teamWidth, Scale(34));
            lblEnemy.SetBounds(teamLeft, teamTop + Scale(38), Scale(36), Scale(34));
            flowEnemy.SetBounds(teamLeft + Scale(40), teamTop + Scale(38), teamWidth, Scale(34));
            LayoutTeamPlayers(flowAlly);
            LayoutTeamPlayers(flowEnemy);
        }

        private void LayoutTeamPlayers(FlowLayoutPanel team)
        {
            int Scale(int value) => value * DeviceDpi / 96;
            team.WrapContents = false;
            team.FlowDirection = FlowDirection.LeftToRight;
            int count = team.Controls.Count;
            if (count == 0) return;
            int gap = Scale(6);
            int cellWidth = Math.Max(Scale(42), (team.ClientSize.Width - gap * (count - 1)) / count);
            for (int index = 0; index < count; index++)
            {
                Control entry = team.Controls[index];
                entry.Size = new Size(cellWidth, Scale(34));
                entry.Margin = new Padding(0, 0, index == count - 1 ? 0 : gap, 0);
                entry.Controls["championAvatar"]?.SetBounds(0, Scale(2), Scale(30), Scale(30));
                entry.Controls["summonerName"]?.SetBounds(Scale(36), 0, Math.Max(1, cellWidth - Scale(36)), Scale(34));
            }
        }

        /// <summary>
        /// 构建上下两排阵容，每位玩家显示英雄头像和召唤师名；自己保留金色描边。
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
                string name = !string.IsNullOrWhiteSpace(identity?.gameName) ? identity.gameName
                    : !string.IsNullOrWhiteSpace(identity?.summonerName) ? identity.summonerName : $"玩家{p.participantId}";
                var entry = new Panel { Name = "teamPlayer", BackColor = Color.Transparent, AccessibleName = name };
                var nameLabel = new AntdUI.Label { Name = "summonerName", Text = name, AutoSize = false,
                    AutoEllipsis = true, TextMultiLine = false, BackColor = Color.Transparent, ForeColor = UiTheme.Palette.TextPrimary,
                    Font = new Font(UiMetrics.FontFamily, 8.5F), TextAlign = ContentAlignment.MiddleLeft };

                var avatar = new RoundPictureBox
                {
                    Name = "championAvatar",
                    Size = new Size(30, 30),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderWidth = isMe ? 3 : 1,
                    BorderColor = isMe ? Color.FromArgb(255, 193, 7) : Color.FromArgb(160, 255, 255, 255),
                    Margin = new Padding(0, 0, 4, 0)
                };
                avatar.Disposed += (_, _) => avatar.Image?.Dispose();
                var tip = new ToolTip();
                entry.Disposed += (_, _) => tip.Dispose();
                string? playerPuuid = identity?.puuid;
                tip.SetToolTip(avatar, string.IsNullOrEmpty(playerPuuid)
                    ? name
                    : $"{name}（点击查询该玩家战绩）");
                tip.SetToolTip(nameLabel, tip.GetToolTip(avatar));

                // 点击其他玩家头像 → 跳转到战绩查询该玩家
                if (!string.IsNullOrEmpty(playerPuuid))
                {
                    avatar.Cursor = Cursors.Hand;
                    nameLabel.Cursor = Cursors.Hand;
                    avatar.Click += (_, _) => _ = BattleQueryForm.QueryPlayerAsync(playerPuuid);
                    nameLabel.Click += (_, _) => _ = BattleQueryForm.QueryPlayerAsync(playerPuuid);
                }
                entry.Controls.Add(avatar);
                entry.Controls.Add(nameLabel);

                int championId = p.championId;
                _ = LoadAvatarAsync(avatar, championId);

                if (p.teamId == myTeamId)
                    flowAlly.Controls.Add(entry);
                else
                    flowEnemy.Controls.Add(entry);
            }
            lblAlly.Visible = flowAlly.Visible = flowAlly.Controls.Count > 0;
            lblEnemy.Visible = flowEnemy.Visible = flowEnemy.Controls.Count > 0;
            LayoutRecord();
        }

        /// <summary>
        /// 异步加载英雄头像到圆形控件（全局缓存，不重复下载）。
        /// </summary>
        private async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            Image? icon = await LoadImageAsync(() => _gameAssetService.GetChampionIconAsync(championId, _lifetime.Token));
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
            if (IsDisposed || box.IsDisposed) { image?.Dispose(); return; }
            if (_ownedImages.Remove(box, out Image? old)) old.Dispose();
            box.Image = image;
            if (image != null) _ownedImages[box] = image;
        }

        /// <summary>替换控件图像并释放旧图像资源。</summary>
        private void ReplaceImage(AntdUI.Avatar box, Image? image)
        {
            if (IsDisposed || box.IsDisposed) { image?.Dispose(); return; }
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
