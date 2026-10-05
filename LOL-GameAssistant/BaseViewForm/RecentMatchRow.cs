using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using System.Drawing.Drawing2D;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 单场战绩行：本人战绩 + 模式 + 日期 + KDA + 胜负；查询页可展开显示队友信息。
    /// </summary>
    public partial class RecentMatchRow : UserControl, IThemeAware
    {
        public const int RowHeight = 40;
        public const int TeamRowHeight = 126;

        private MatchDetail? _detail;
        private string? _puuid;
        private readonly IGameAssetService _gameAssetService;
        private readonly IPremadeDetectionService _premadeDetectionService;
        private readonly AntdUI.Panel _teamInfoPanel;
        private readonly AntdUI.Label _teamTitle;
        private readonly AntdUI.Label _teamQueueTag;
        private readonly FlowLayoutPanel _teammatesPanel;
        private ToolTip? _teamQueueTip;
        private readonly ToolTip _championTip = new();
        private Image? _ownedChampionImage;
        private bool _showTeammateInfo;
        private bool _championAndModeOnly;
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
        private bool _isHovered;
        private bool _isWin;

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

        /// <summary>初始化 RecentMatchRow 的实例状态。</summary>
        public RecentMatchRow() : this(AppCompositionRoot.GameAssetService, AppCompositionRoot.PremadeDetectionService)
        {
        }

        /// <summary>战绩行通过应用端口读取英雄资源，避免直接依赖静态 LCU API。</summary>
        internal RecentMatchRow(
            IGameAssetService gameAssetService,
            IPremadeDetectionService premadeDetectionService)
        {
            _gameAssetService = gameAssetService;
            _premadeDetectionService = premadeDetectionService;
            InitializeComponent();

            _teamInfoPanel = new AntdUI.Panel
            {
                BackColor = Color.Transparent,
                Visible = false
            };
            _teamTitle = new AntdUI.Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 90, 90),
                Text = "队友信息",
                TextAlign = ContentAlignment.MiddleLeft
            };
            _teamQueueTag = new AntdUI.Label
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

            _championTip.SetToolTip(picChampion, "双击查看对局详情");
            Disposed += (_, _) => { _championTip.Dispose(); _teamQueueTip?.Dispose(); _ownedChampionImage?.Dispose(); };

            this.DoubleClick += (_, _) => OpenDetail();
            this.MouseEnter += (_, _) => StartHover(true);
            this.MouseLeave += (_, _) => UpdateHoverState(Cursor.Position);
            foreach (Control child in Controls)
            {
                child.DoubleClick += (_, _) => OpenDetail();
                child.MouseEnter += (_, _) => StartHover(true);
                child.MouseLeave += (_, _) => UpdateHoverState(Cursor.Position);
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
            x += 34 + (_championAndModeOnly ? 6 : 8);

            lblResult.Location = new Point(x, labelY);
            lblResult.Width = _championAndModeOnly
                ? Math.Max(30, TextRenderer.MeasureText(lblResult.Text, lblResult.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 2) : 44;
            x += lblResult.Width + 4;

            int right = Math.Max(x, w - (_championAndModeOnly ? 8 : 12));
            // 按实际文字保留必要宽度，长 KDA/时长仍能完整显示。
            int durationWidth = _championAndModeOnly
                ? Math.Max(42, TextRenderer.MeasureText(lblDuration.Text, lblDuration.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 4) : w >= 520 ? 82 : 62;
            int kdaWidth = _championAndModeOnly
                ? Math.Max(64, TextRenderer.MeasureText(lblKda.Text, lblKda.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 4) : w >= 520 ? 90 : 82;
            int columnGap = _championAndModeOnly ? 4 : 6;
            int durationLeft = right - durationWidth;
            int kdaLeft = durationLeft - columnGap - kdaWidth;
            bool showDate = !_championAndModeOnly && w >= 440;
            int dateWidth = showDate ? 78 : 0;
            int dateLeft = kdaLeft - 6 - dateWidth;
            int detailsRight = showDate ? dateLeft - 6 : kdaLeft - columnGap;
            int detailsWidth = Math.Max(0, detailsRight - x);

            // 对局玩家卡片在同一列分两行展示英雄、模式，窄卡片也保留模式。
            bool showMode = !_championAndModeOnly && detailsWidth >= 200;
            int championWidth = showMode
                ? Math.Min(150, Math.Max(92, detailsWidth / 2 - 3))
                : detailsWidth;
            lblChampion.Location = new Point(x, _championAndModeOnly ? Math.Max(2, (Height - 34) / 2) : labelY);
            lblChampion.Width = championWidth;
            lblChampion.Height = _championAndModeOnly ? 34 : 20;

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

        /// <summary>窗口或控件尺寸变化时重新计算内容布局。</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRow();
        }

        /// <summary>布局发生变化后更新内部控件尺寸。</summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRow();
        }

        /// <summary>读取并绑定近期战绩行的展示数据。</summary>
        public async Task SetDataAsync(MatchDetail detail, MatchParticipant gamer, string? puuid,
            bool championAndModeOnly = false)
        {
            try
            {
                _detail = detail;
                _puuid = puuid;
                _championAndModeOnly = championAndModeOnly;

                bool win = gamer.IsWin();
                _isWin = win;
                ApplyTheme(UiTheme.Palette);
                bool dark = UiTheme.Palette.IsDark;
                lblResult.Text = win ? "胜利" : "失败";
                lblResult.ForeColor = win
                    ? (dark ? Color.FromArgb(129, 199, 132) : Color.FromArgb(46, 125, 50))
                    : (dark ? Color.FromArgb(239, 154, 154) : Color.FromArgb(198, 40, 40));
                string championName = GetChampionDisplayName(gamer.championId);
                var playerIdentity = detail.GetPlayerIdentity(puuid);
                string playerName = !string.IsNullOrWhiteSpace(playerIdentity?.gameName)
                    ? playerIdentity.gameName
                    : !string.IsNullOrWhiteSpace(playerIdentity?.summonerName)
                        ? playerIdentity.summonerName
                        : "本人";
                string modeText = detail.GetModeText();
                lblChampion.Text = championAndModeOnly ? $"{championName}\n{modeText}" : $"{playerName} · {championName}";
                lblMode.Text = modeText;
                lblDate.Text = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "未知";
                lblKda.Text = gamer.GetKdaText();
                lblDuration.Text = detail.GetDurationText();

                string champName = GetChampionDisplayName(gamer.championId);
                string caption = championAndModeOnly ? $"{champName} · {modeText}" : $"{playerName} · {champName} · {modeText}";
                _championTip.SetToolTip(picChampion, $"{caption} · {detail.GetDurationText()}\n{gamer.GetKdaText()} · {(win ? "胜利" : "失败")}\n双击查看对局详情");
                _championTip.SetToolTip(lblChampion, caption + "\n双击查看对局详情");
                LayoutRow();

                if (_showTeammateInfo)
                {
                    BuildTeammateInfo(detail, gamer, puuid);
                    SetTeamQueueStatus("检测中", "正在根据近期同队记录识别本场队伍类型");
                }

                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(gamer.championId));
                if (icon != null && !IsDisposed)
                {
                    picChampion.Image = icon;
                    _ownedChampionImage?.Dispose();
                    _ownedChampionImage = icon;
                }
                else
                {
                    icon?.Dispose();
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

                var identities = detail.participantIdentities;
                var team = detail.participants
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
                    var result = await _premadeDetectionService.DetectAsync(
                        team.Select(member => new TeamMemberIdentity(member.Puuid, member.Name)).ToArray(),
                        Array.Empty<TeamMemberIdentity>());
                    if (IsDisposed || !ReferenceEquals(detail, _detail)) return;

                    SetTeamQueueStatus(result.GetInferredTeamStatus(0), result.GetTeamQueueDetail(0));
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

        /// <summary>更新当前战绩行的组队状态提示。</summary>
        private void SetTeamQueueStatus(string status, string detail)
        {
            _teamQueueTag.Text = status;
            (_teamQueueTag.BackColor, _teamQueueTag.ForeColor) = status switch
            {
                "未发现" => (Color.FromArgb(238, 238, 238), Color.FromArgb(100, 100, 100)),
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
            MatchDetail detail,
            MatchParticipant gamer,
            string? puuid)
        {
            ControlLifetime.ClearAndDispose(_teammatesPanel);

            var identities = detail.participantIdentities;
            var teammates = detail.participants
                .Where(p => p.teamId == gamer.teamId)
                .Where(p => p.participantId != gamer.participantId)
                .OrderBy(p => p.participantId)
                .ToList();

            _teamTitle.Text = teammates.Count > 0 ? $"队友（{teammates.Count}）" : "队友信息";
            if (teammates.Count == 0)
            {
                _teammatesPanel.Controls.Add(new AntdUI.Label
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

        /// <summary>计算队友预览卡片的合适宽度。</summary>
        private int CalculateTeammateCardWidth(int count)
        {
            if (count <= 0) return 48;
            const int cardSpacing = 6;
            int available = Math.Max(0, _teammatesPanel.ClientSize.Width - count * cardSpacing);
            return Math.Max(48, available / count);
        }

        /// <summary>按战绩行可用空间调整队友卡片。</summary>
        private void ResizeTeammateCards()
        {
            var cards = _teammatesPanel.Controls.OfType<AntdUI.Panel>().ToList();
            if (cards.Count == 0) return;

            int cardWidth = CalculateTeammateCardWidth(cards.Count);
            foreach (var card in cards)
            {
                LayoutTeammateCard(card, cardWidth);
            }
        }

        /// <summary>创建一名队友的战绩预览卡片。</summary>
        private Control CreateTeammateCard(
            MatchParticipant participant,
            MatchPlayer? identity,
            int cardWidth)
        {
            var card = new AntdUI.Panel
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
            string champion = GetChampionDisplayName(participant.championId);
            bool win = participant.IsWin();
            var nameLabel = new AntdUI.Label
            {
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                Location = new Point(43, 5),
                Size = new Size(Math.Max(0, cardWidth - 48), 20),
                Text = name
            };
            var detailLabel = new AntdUI.Label
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
            card.Disposed += (_, _) => { tip.Dispose(); avatar.Image?.Dispose(); };
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
        private static void LayoutTeammateCard(AntdUI.Panel card, int cardWidth)
        {
            const int compactThreshold = 118;
            card.Size = new Size(cardWidth, 56);

            var avatar = card.Controls.OfType<RoundPictureBox>().FirstOrDefault();
            var labels = card.Controls.OfType<AntdUI.Label>().ToList();
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

        /// <summary>为卡片和子控件绑定打开对局详情的操作。</summary>
        private void WireOpenDetail(Control control)
        {
            control.DoubleClick += (_, _) => OpenDetail();
        }

        /// <summary>异步加载英雄或玩家头像并更新控件。</summary>
        private async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            try
            {
                Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(championId));
                if (icon != null && !box.IsDisposed)
                {
                    box.Image = icon;
                }
                else
                {
                    icon?.Dispose();
                }
            }
            catch
            {
                // 队友头像加载失败不影响文字信息。
            }
        }

        /// <summary>根据英雄标识取得展示名称。</summary>
        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        /// <summary>资源服务返回原始内容，由表现层解码为控件所需的位图。</summary>
        private static Image? ToImage(GameAsset? asset)
        {
            try
            {
                if (asset == null || asset.IsEmpty) return null;
                using var stream = new MemoryStream(asset.Content);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>绘制当前控件的自定义外观。</summary>
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

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            _hoverTimer.Stop();
            _hoverActive = false;
            _isHovered = false;
            _baseBack = palette.IsDark ? palette.SurfaceRaised : _isWin
                ? Color.FromArgb(236, 247, 238) : Color.FromArgb(253, 238, 238);
            _accent = _isWin ? Color.FromArgb(76, 175, 80) : Color.FromArgb(229, 57, 53);
            _hoverBack = UiAnimation.LerpColor(_baseBack, _accent, palette.IsDark ? .14 : .08);
            BackColor = _baseBack;
        }

        /// <summary>根据鼠标位置更新战绩行的悬停状态。</summary>
        internal void UpdateHoverState(Point screenPosition)
        {
            bool inside = IsHandleCreated && Visible && RectangleToScreen(ClientRectangle).Contains(screenPosition);
            if (inside)
            {
                StartHover(true);
                return;
            }
            _isHovered = false;
            _hoverActive = false;
            _hoverTimer.Stop();
            BackColor = _baseBack;
        }

        /// <summary>启动战绩行悬停动画。</summary>
        private void StartHover(bool hovering)
        {
            if (_isHovered == hovering) return;
            _isHovered = hovering;
            _hoverFrom = BackColor;
            _hoverTo = hovering ? _hoverBack : _baseBack;
            _hoverT = 0;
            _hoverActive = true;
            _hoverTimer.Start();
        }

        /// <summary>推进悬停动画并重绘战绩行。</summary>
        private void HoverTick()
        {
            // MouseLeave can be missed when scrolling or moving across child windows.
            UpdateHoverState(Cursor.Position);
            if (!_hoverActive) return;
            _hoverT = Math.Min(1, _hoverT + 0.14);
            BackColor = UiAnimation.LerpColor(_hoverFrom, _hoverTo, UiAnimation.EaseOutCubic(_hoverT));
            if (_hoverT >= 1)
            {
                _hoverActive = false;
                if (!_isHovered) _hoverTimer.Stop();
            }
        }

        /// <summary>打开当前对局的完整详情。</summary>
        private void OpenDetail()
        {
            if (_detail == null || string.IsNullOrEmpty(_puuid)) return;
            MatchDetailForm.OpenAndHandle(_detail, _puuid, this);
        }
    }
}
