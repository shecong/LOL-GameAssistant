using System.Drawing.Drawing2D;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 单场战绩行：本人战绩 + 模式 + 日期 + KDA + 胜负；查询页可展开显示队友信息。
    /// </summary>
    public partial class RecentMatchRow : UserControl
    {
        public const int RowHeight = 40;
        public const int TeamRowHeight = 126;

        private GameDetailModel.GameInfo? _detail;
        private string? _puuid;
        private readonly Panel _teamInfoPanel;
        private readonly Label _teamTitle;
        private readonly Label _teamQueueTag;
        private readonly FlowLayoutPanel _teammatesPanel;
        private ToolTip? _teamQueueTip;
        private bool _showTeammateInfo;
        private bool _teamQueueDetectionStarted;
        private static readonly SemaphoreSlim TeamQueueDetectionGate = new(2, 2);
        private Color _baseBack = Color.FromArgb(250, 250, 250);
        private Color _hoverBack = Color.FromArgb(235, 242, 252);
        private Color _accent = Color.FromArgb(150, 150, 150);

        private readonly System.Windows.Forms.Timer _hoverTimer;
        private Color _hoverFrom;
        private Color _hoverTo;
        private bool _hoverActive;
        private double _hoverT;

        /// <summary>
        /// 是否在本场记录下方显示本人所在队伍的队友信息。
        /// </summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowTeammateInfo
        {
            get => _showTeammateInfo;
            set
            {
                _showTeammateInfo = value;
                _teamInfoPanel.Visible = value;
                _teamQueueTag.Visible = value;
                Height = value ? TeamRowHeight : RowHeight;
                LayoutRow();
            }
        }

        public RecentMatchRow()
        {
            InitializeComponent();

            _teamInfoPanel = new Panel
            {
                BackColor = Color.Transparent,
                Visible = false
            };
            _teamTitle = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 90, 90),
                Text = "队友信息",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _teamQueueTag = new Label
            {
                AutoSize = false,
                BackColor = Color.FromArgb(238, 238, 238),
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 100, 100),
                Text = "检测中",
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            _teammatesPanel = new FlowLayoutPanel
            {
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _teamInfoPanel.Controls.Add(_teammatesPanel);
            _teamInfoPanel.Controls.Add(_teamTitle);
            _teamInfoPanel.Controls.Add(_teamQueueTag);
            Controls.Add(_teamInfoPanel);

            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _hoverTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _hoverTimer.Tick += (_, _) => HoverTick();
            Disposed += (_, _) => _hoverTimer.Dispose();

            var tip = new ToolTip();
            tip.SetToolTip(picChampion, "双击查看对局详情");

            this.DoubleClick += (_, _) => OpenDetail();
            this.MouseEnter += (_, _) => StartHover(true);
            this.MouseLeave += (_, _) => StartHover(false);
            foreach (Control child in Controls)
            {
                child.DoubleClick += (_, _) => OpenDetail();
                child.MouseEnter += (_, _) => StartHover(true);
                child.MouseLeave += (_, _) => StartHover(false);
            }
        }

        /// <summary>
        /// 根据当前宽度自适应分布各列：左侧固定信息区，时长靠右，
        /// 中间的留白随宽度自然伸展，全屏时也不会显得拥挤。
        /// </summary>
        private void LayoutRow()
        {
            int w = Math.Max(1, ClientSize.Width);
            int x = 10;
            int iconY = _showTeammateInfo ? 5 : Math.Max(3, (Height - 34) / 2);
            int labelY = _showTeammateInfo ? 10 : Math.Max(6, (Height - 20) / 2);

            picChampion.Location = new Point(x, iconY);
            x += 34 + 8;

            lblResult.Location = new Point(x, labelY);
            lblResult.Width = 44;
            x += 48;

            int right = Math.Max(x, w - 12);
            int durationWidth = w >= 520 ? 82 : 62;
            int kdaWidth = w >= 520 ? 90 : 82;
            int durationLeft = right - durationWidth;
            int kdaLeft = durationLeft - 6 - kdaWidth;
            bool showDate = w >= 440;
            int dateWidth = showDate ? 78 : 0;
            int dateLeft = kdaLeft - 6 - dateWidth;
            int detailsRight = showDate ? dateLeft - 6 : kdaLeft - 6;
            int detailsWidth = Math.Max(0, detailsRight - x);

            // 宽卡片展示模式和日期；窄卡片只保留胜负、玩家/英雄、KDA 和时长，避免列互相遮挡。
            bool showMode = detailsWidth >= 200;
            int championWidth = showMode
                ? Math.Min(150, Math.Max(92, detailsWidth / 2 - 3))
                : detailsWidth;
            lblChampion.Location = new Point(x, labelY);
            lblChampion.Width = championWidth;

            lblMode.Visible = showMode;
            if (showMode)
            {
                lblMode.Location = new Point(x + championWidth + 6, labelY);
                lblMode.Width = Math.Max(0, detailsRight - lblMode.Left);
            }

            lblDate.Visible = showDate;
            if (showDate)
            {
                lblDate.Location = new Point(dateLeft, labelY);
                lblDate.Width = dateWidth;
            }

            lblKda.Location = new Point(kdaLeft, labelY);
            lblKda.Width = kdaWidth;
            lblDuration.Location = new Point(durationLeft, labelY);
            lblDuration.Width = durationWidth;

            if (_showTeammateInfo)
            {
                _teamInfoPanel.Location = new Point(10, 47);
                _teamInfoPanel.Size = new Size(Math.Max(1, w - 20), Math.Max(70, Height - 51));
                _teamTitle.Location = new Point(0, 0);
                _teamTitle.Size = new Size(66, 25);
                int queueTagWidth = Math.Clamp(
                    TextRenderer.MeasureText(_teamQueueTag.Text, _teamQueueTag.Font).Width + 14,
                    66,
                    112);
                _teamQueueTag.Location = new Point(0, 29);
                _teamQueueTag.Size = new Size(queueTagWidth, 22);
                int teammateLeft = queueTagWidth + 4;
                _teammatesPanel.Location = new Point(teammateLeft, 0);
                _teammatesPanel.Size = new Size(Math.Max(1, _teamInfoPanel.ClientSize.Width - teammateLeft), 56);
                ResizeTeammateCards();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRow();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRow();
        }

        public async Task SetDataAsync(GameDetailModel.GameInfo detail, GameDetailModel.ParticipantsItem gamer, string? puuid)
        {
            try
            {
                _detail = detail;
                _puuid = puuid;

                bool win = gamer.IsWin();
                _baseBack = win ? Color.FromArgb(236, 247, 238) : Color.FromArgb(253, 238, 238);
                _hoverBack = win ? Color.FromArgb(224, 243, 228) : Color.FromArgb(251, 228, 228);
                _accent = win ? Color.FromArgb(76, 175, 80) : Color.FromArgb(229, 57, 53);
                BackColor = _baseBack;

                lblResult.Text = win ? "胜利" : "失败";
                lblResult.ForeColor = win ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
                string championName = ChampionMap.GetChampion(gamer.championId)?.RealName ?? $"英雄{gamer.championId}";
                var playerIdentity = detail.GetPlayerIdentity(puuid);
                string playerName = !string.IsNullOrWhiteSpace(playerIdentity?.gameName)
                    ? playerIdentity.gameName
                    : !string.IsNullOrWhiteSpace(playerIdentity?.summonerName)
                        ? playerIdentity.summonerName
                        : "本人";
                lblChampion.Text = $"{playerName} · {championName}";
                string modeText = detail.GetModeText();
                lblMode.Text = modeText;
                lblDate.Text = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "未知";
                lblKda.Text = gamer.GetKdaText();
                lblDuration.Text = detail.GetDurationText();

                string champName = ChampionMap.GetChampion(gamer.championId)?.RealName ?? $"英雄{gamer.championId}";
                var tip = new ToolTip();
                tip.SetToolTip(picChampion, $"{playerName} · {champName} · {modeText} · {detail.GetDurationText()}\n{gamer.GetKdaText()} · {(win ? "胜利" : "失败")}");

                if (_showTeammateInfo)
                {
                    BuildTeammateInfo(detail, gamer, puuid);
                    SetTeamQueueStatus("检测中", "正在根据近期同队记录识别本场队伍类型");
                }

                var icon = await Game_Api.GetGameChampionIconAsync(gamer.championId);
                if (icon != null && !IsDisposed)
                {
                    picChampion.Image = icon;
                }
                Invalidate();
            }
            catch
            {
                // 单行加载失败不影响其它行
            }
        }

        /// <summary>
        /// 异步识别本场本人队伍是单排还是多排。检测使用近期同队记录，结果为推断值。
        /// </summary>
        public async Task DetectTeamQueueStatusAsync()
        {
            if (!_showTeammateInfo || _teamQueueDetectionStarted || _detail == null || string.IsNullOrEmpty(_puuid)) return;
            _teamQueueDetectionStarted = true;

            var detail = _detail;
            string puuid = _puuid;
            try
            {
                var current = detail.GetParticipant(puuid);
                if (current == null)
                {
                    SetTeamQueueStatus("未知", "未找到本场玩家的队伍信息");
                    return;
                }

                var identities = detail.participantIdentities ?? new List<GameDetailModel.ParticipantIdentitiesItem>();
                var team = (detail.participants ?? new List<GameDetailModel.ParticipantsItem>())
                    .Where(participant => participant.teamId == current.teamId)
                    .Select(participant =>
                    {
                        var player = identities.FirstOrDefault(identity => identity.participantId == participant.participantId)?.player;
                        return (Puuid: player?.puuid ?? "", Name: player?.gameName ?? player?.summonerName ?? "");
                    })
                    .Where(player => !string.IsNullOrWhiteSpace(player.Puuid))
                    .ToList();

                if (team.Count < 2)
                {
                    SetTeamQueueStatus("未知", "本场可用队友信息不足，无法判断是否多排");
                    return;
                }

                await TeamQueueDetectionGate.WaitAsync();
                try
                {
                    var result = await PremadeDetector.DetectAsync(team, new List<(string Puuid, string Name)>());
                    if (IsDisposed || !ReferenceEquals(detail, _detail)) return;

                    SetTeamQueueStatus(result.GetTeamQueueStatus(0), result.GetTeamQueueDetail(0));
                }
                finally
                {
                    TeamQueueDetectionGate.Release();
                }
            }
            catch
            {
                if (!IsDisposed && ReferenceEquals(detail, _detail))
                {
                    SetTeamQueueStatus("未知", "组队检测暂不可用");
                }
            }
        }

        private void SetTeamQueueStatus(string status, string detail)
        {
            _teamQueueTag.Text = status;
            (_teamQueueTag.BackColor, _teamQueueTag.ForeColor) = status switch
            {
                "单排" => (Color.FromArgb(238, 238, 238), Color.FromArgb(100, 100, 100)),
                "检测中" => (Color.FromArgb(227, 242, 253), Color.FromArgb(25, 118, 210)),
                "未知" => (Color.FromArgb(255, 243, 224), Color.FromArgb(230, 126, 34)),
                _ => (Color.FromArgb(255, 236, 179), Color.FromArgb(191, 104, 0))
            };
            _teamQueueTip ??= new ToolTip();
            _teamQueueTip.SetToolTip(_teamQueueTag, $"{status}：{detail}");
        }

        /// <summary>
        /// 在记录行下方显示本人所在队伍的 4 名队友及其英雄、KDA 和胜负信息。
        /// </summary>
        private void BuildTeammateInfo(
            GameDetailModel.GameInfo detail,
            GameDetailModel.ParticipantsItem gamer,
            string? puuid)
        {
            _teammatesPanel.Controls.Clear();

            var identities = detail.participantIdentities ?? new List<GameDetailModel.ParticipantIdentitiesItem>();
            var teammates = (detail.participants ?? new List<GameDetailModel.ParticipantsItem>())
                .Where(p => p.teamId == gamer.teamId)
                .Where(p => p.participantId != gamer.participantId)
                .OrderBy(p => p.participantId)
                .ToList();

            _teamTitle.Text = teammates.Count > 0 ? $"队友（{teammates.Count}）" : "队友信息";
            if (teammates.Count == 0)
            {
                _teammatesPanel.Controls.Add(new Label
                {
                    AutoSize = false,
                    BackColor = Color.Transparent,
                    ForeColor = SystemColors.GrayText,
                    Size = new Size(180, 56),
                    Text = "未获取到队友信息",
                    TextAlign = ContentAlignment.MiddleLeft
                });
                return;
            }

            int cardWidth = CalculateTeammateCardWidth(teammates.Count);
            foreach (var teammate in teammates)
            {
                var identity = identities.FirstOrDefault(i => i.participantId == teammate.participantId)?.player;
                _teammatesPanel.Controls.Add(CreateTeammateCard(teammate, identity, cardWidth));
            }
            ResizeTeammateCards();
        }

        private int CalculateTeammateCardWidth(int count)
        {
            if (count <= 0) return 48;
            const int cardSpacing = 6;
            int available = Math.Max(0, _teammatesPanel.ClientSize.Width - count * cardSpacing);
            return Math.Max(48, available / count);
        }

        private void ResizeTeammateCards()
        {
            var cards = _teammatesPanel.Controls.OfType<Panel>().ToList();
            if (cards.Count == 0) return;

            int cardWidth = CalculateTeammateCardWidth(cards.Count);
            foreach (var card in cards)
            {
                LayoutTeammateCard(card, cardWidth);
            }
        }

        private Control CreateTeammateCard(
            GameDetailModel.ParticipantsItem participant,
            GameDetailModel.Player? identity,
            int cardWidth)
        {
            var card = new Panel
            {
                BackColor = Color.FromArgb(42, 255, 255, 255),
                Margin = new Padding(0, 0, 6, 0),
                Size = new Size(cardWidth, 56)
            };

            var avatar = new RoundPictureBox
            {
                BorderColor = Color.FromArgb(125, 125, 125),
                BorderWidth = 1,
                Location = new Point(5, 12),
                Size = new Size(32, 32)
            };
            _ = LoadAvatarAsync(avatar, participant.championId);
            card.Controls.Add(avatar);

            string name = identity?.gameName ?? identity?.summonerName ?? $"玩家{participant.participantId}";
            string champion = ChampionMap.GetChampion(participant.championId)?.RealName ?? $"英雄{participant.championId}";
            bool win = participant.IsWin();
            var nameLabel = new Label
            {
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                Location = new Point(43, 5),
                Size = new Size(Math.Max(0, cardWidth - 48), 20),
                Text = name
            };
            var detailLabel = new Label
            {
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = win ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40),
                Location = new Point(43, 27),
                Size = new Size(Math.Max(0, cardWidth - 48), 20),
                Text = $"{champion} · {participant.GetKdaText()} · {(win ? "胜" : "负")}"
            };
            card.Controls.Add(nameLabel);
            card.Controls.Add(detailLabel);

            var tip = new ToolTip();
            tip.SetToolTip(card, $"{name}\n{champion} · KDA {participant.GetKdaText()} · {(win ? "胜利" : "失败")}\n双击查看本局详情");
            tip.SetToolTip(nameLabel, tip.GetToolTip(card));
            tip.SetToolTip(detailLabel, tip.GetToolTip(card));
            WireOpenDetail(card);
            WireOpenDetail(avatar);
            WireOpenDetail(nameLabel);
            WireOpenDetail(detailLabel);
            LayoutTeammateCard(card, cardWidth);
            return card;
        }

        /// <summary>
        /// 队友信息不足以展示文字时保留英雄头像与完整悬停提示，避免小面板中的标签越界。
        /// </summary>
        private static void LayoutTeammateCard(Panel card, int cardWidth)
        {
            const int compactThreshold = 118;
            card.Size = new Size(cardWidth, 56);

            var avatar = card.Controls.OfType<RoundPictureBox>().FirstOrDefault();
            var labels = card.Controls.OfType<Label>().ToList();
            bool compact = cardWidth < compactThreshold;
            if (avatar != null)
            {
                int avatarSize = compact ? 30 : 32;
                avatar.Size = new Size(avatarSize, avatarSize);
                avatar.Location = compact
                    ? new Point(Math.Max(0, (cardWidth - avatarSize) / 2), 13)
                    : new Point(5, 12);
            }

            foreach (var label in labels)
            {
                label.Visible = !compact;
            }
            if (compact || labels.Count < 2) return;

            int textLeft = 43;
            int textWidth = Math.Max(0, cardWidth - textLeft - 5);
            labels[0].Location = new Point(textLeft, 5);
            labels[0].Size = new Size(textWidth, 20);
            labels[1].Location = new Point(textLeft, 27);
            labels[1].Size = new Size(textWidth, 20);
        }

        private void WireOpenDetail(Control control)
        {
            control.DoubleClick += (_, _) => OpenDetail();
        }

        private static async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            try
            {
                var icon = await Game_Api.GetGameChampionIconAsync(championId);
                if (icon != null && !box.IsDisposed)
                {
                    box.Image = icon;
                }
            }
            catch
            {
                // 队友头像加载失败不影响文字信息。
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 滑入/布局过程中行高可能被压到极小，Width-3 / Height-3 会变成 0 或负数，
            // GDI+ 不接受空矩形，会抛 ArgumentException。
            if (Width <= 3 || Height <= 3) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = GradientPanel.RoundedRect(rect, 8);
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);

            // 左侧胜负色条
            using var accentBrush = new SolidBrush(_accent);
            g.FillRectangle(accentBrush, 3, 9, 4, Height - 18);

            using var borderPen = new Pen(Color.FromArgb(28, 0, 0, 0), 1);
            g.DrawPath(borderPen, path);

            base.OnPaint(e);
        }

        private void StartHover(bool hovering)
        {
            _hoverFrom = BackColor;
            _hoverTo = hovering ? _hoverBack : _baseBack;
            _hoverT = 0;
            _hoverActive = true;
            _hoverTimer.Start();
        }

        private void HoverTick()
        {
            if (!_hoverActive) return;
            _hoverT = Math.Min(1, _hoverT + 0.14);
            BackColor = UiAnimation.LerpColor(_hoverFrom, _hoverTo, UiAnimation.EaseOutCubic(_hoverT));
            if (_hoverT >= 1)
            {
                _hoverActive = false;
                _hoverTimer.Stop();
            }
        }

        private void OpenDetail()
        {
            if (_detail == null || string.IsNullOrEmpty(_puuid)) return;
            MatchDetailForm.OpenAndHandle(_detail, _puuid, this);
        }
    }
}
