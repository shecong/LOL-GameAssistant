using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Friends;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 好友页：展示好友在线状态，双击好友卡片可查询该好友战绩。
    /// </summary>
    public sealed class FriendsForm : UserControl
    {
        private readonly Label _statusLabel;
        private readonly Label _emptyLabel;
        private readonly FlowLayoutPanel _friendList;
        private readonly Button _refreshButton;
        private readonly IFriendDirectoryService _friendDirectoryService;
        private readonly IFriendSpectateService _friendSpectateService;
        private readonly IProfileIconService _profileIconService;
        private bool _loading;

        public FriendsForm() : this(
            AppCompositionRoot.FriendDirectoryService,
            AppCompositionRoot.FriendSpectateService,
            AppCompositionRoot.ProfileIconService)
        {
        }

        /// <summary>表现层仅依赖应用服务，便于替换数据来源或使用测试替身。</summary>
        internal FriendsForm(
            IFriendDirectoryService friendDirectoryService,
            IFriendSpectateService friendSpectateService,
            IProfileIconService profileIconService)
        {
            _friendDirectoryService = friendDirectoryService;
            _friendSpectateService = friendSpectateService;
            _profileIconService = profileIconService;
            BackColor = Color.FromArgb(245, 247, 250);
            AutoScaleMode = AutoScaleMode.Dpi;
            Dock = DockStyle.Fill;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(20, 10, 20, 8),
                BackColor = Color.White
            };

            var titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                Text = "好友",
                ForeColor = Color.FromArgb(38, 50, 56)
            };
            _statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SystemColors.GrayText,
                Text = "正在准备好友列表..."
            };
            _refreshButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 86,
                Height = 32,
                Text = "刷新",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 136, 229),
                ForeColor = Color.White,
                Margin = new Padding(0, 8, 0, 0),
                Cursor = Cursors.Hand
            };
            _refreshButton.FlatAppearance.BorderSize = 0;
            _refreshButton.Click += async (_, _) => await RefreshAsync();

            header.Controls.Add(_statusLabel);
            header.Controls.Add(titleLabel);
            header.Controls.Add(_refreshButton);

            _friendList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(14),
                BackColor = Color.FromArgb(245, 247, 250)
            };
            _emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = SystemColors.GrayText,
                Text = "暂无好友数据"
            };

            Controls.Add(_friendList);
            Controls.Add(_emptyLabel);
            Controls.Add(header);

            Load += async (_, _) => await RefreshAsync();
        }

        /// <summary>
        /// 刷新好友列表。切换到好友页时可由主窗体调用。
        /// </summary>
        public async Task RefreshAsync()
        {
            if (_loading || IsDisposed) return;
            _loading = true;
            _refreshButton.Enabled = false;
            _statusLabel.Text = "正在刷新好友列表...";

            try
            {
                var friends = await _friendDirectoryService.GetFriendsAsync();
                RenderFriends(friends);
            }
            catch (Exception ex)
            {
                _friendList.Controls.Clear();
                _emptyLabel.Visible = true;
                _emptyLabel.Text = "好友列表加载失败，请确认 LOL 客户端已启动并登录";
                _statusLabel.Text = $"加载失败：{ex.Message}";
            }
            finally
            {
                _refreshButton.Enabled = true;
                _loading = false;
            }
        }

        private void RenderFriends(IReadOnlyList<FriendProfile> friends)
        {
            _friendList.SuspendLayout();
            try
            {
                _friendList.Controls.Clear();

                var ordered = friends
                    .OrderByDescending(friend => friend.IsOnline)
                    .ThenBy(friend => friend.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                _emptyLabel.Visible = ordered.Count == 0;
                if (ordered.Count == 0)
                {
                    _emptyLabel.Text = "暂无好友数据，或 LOL 客户端暂未返回好友列表";
                    _statusLabel.Text = "共 0 位好友";
                    return;
                }

                int onlineCount = ordered.Count(friend => friend.IsOnline);
                _statusLabel.Text =
                    $"共 {ordered.Count} 位好友 · 在线 {onlineCount} 位 · 更新时间 {DateTime.Now:HH:mm:ss} · 双击好友查看战绩";

                foreach (var friend in ordered)
                {
                    var card = new FriendCard(friend, _friendSpectateService, _profileIconService)
                    {
                        Width = GetCardWidth()
                    };
                    _friendList.Controls.Add(card);
                }
            }
            finally
            {
                _friendList.ResumeLayout(true);
            }
        }

        private int GetCardWidth()
        {
            int availableWidth = _friendList.ClientSize.Width - _friendList.Padding.Horizontal - 8;
            return Math.Max(320, Math.Min(520, availableWidth));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int width = GetCardWidth();
            foreach (Control control in _friendList.Controls)
            {
                control.Width = width;
            }
        }

        private sealed class FriendCard : Panel
        {
            private readonly FriendProfile _friend;
            private readonly IFriendSpectateService _spectateService;
            private readonly IProfileIconService _profileIconService;
            private bool _querying;
            private bool _spectating;

            public FriendCard(
                FriendProfile friend,
                IFriendSpectateService spectateService,
                IProfileIconService profileIconService)
            {
                _friend = friend;
                _spectateService = spectateService;
                _profileIconService = profileIconService;
                Height = 82;
                Margin = new Padding(6);
                Padding = new Padding(8);
                BackColor = Color.White;
                BorderStyle = BorderStyle.FixedSingle;
                Cursor = string.IsNullOrWhiteSpace(friend.Puuid) ? Cursors.Default : Cursors.Hand;

                var avatar = new RoundPictureBox
                {
                    Size = new Size(54, 54),
                    Location = new Point(8, 10),
                    BorderWidth = 2,
                    BorderColor = GetStatusColor(friend.Presence),
                    Cursor = Cursor
                };
                Controls.Add(avatar);
                _ = LoadAvatarAsync(avatar, friend.ProfileIconId, _profileIconService);

                string displayName = friend.DisplayName;
                var nameLabel = new Label
                {
                    AutoEllipsis = true,
                    Location = new Point(74, 9),
                    Size = new Size(260, 24),
                    Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                    Text = displayName,
                    BackColor = Color.Transparent,
                    Cursor = Cursor
                };
                Controls.Add(nameLabel);

                var statusLabel = new Label
                {
                    AutoEllipsis = true,
                    Location = new Point(74, 36),
                    Size = new Size(300, 21),
                    Font = new Font("Microsoft YaHei UI", 8.5F),
                    ForeColor = GetStatusColor(friend.Presence),
                    Text = GetStatusText(friend),
                    BackColor = Color.Transparent,
                    Cursor = Cursor
                };
                Controls.Add(statusLabel);

                bool canQuery = !string.IsNullOrWhiteSpace(friend.Puuid);
                bool canSpectate = friend.CanSpectate;
                long gameId = friend.ActiveGameId ?? 0;
                var actionLabel = new Label
                {
                    AutoSize = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(330, 52),
                    Size = new Size(140, 18),
                    Font = new Font("Microsoft YaHei UI", 8F),
                    ForeColor = Color.FromArgb(117, 117, 117),
                    Text = canSpectate ? "对局中，可发起观战" : canQuery ? "双击卡片查看战绩" : "暂无可用操作",
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent,
                    Cursor = Cursor
                };
                Controls.Add(actionLabel);

                var queryButton = CreateActionButton("战绩", Color.FromArgb(30, 136, 229));
                queryButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                queryButton.Location = new Point(Width - 158, 14);
                queryButton.Enabled = canQuery;
                queryButton.Click += (_, _) => OpenBattleQuery();
                Controls.Add(queryButton);

                var spectateButton = CreateActionButton("观战", Color.FromArgb(123, 31, 162));
                spectateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                spectateButton.Location = new Point(Width - 82, 14);
                spectateButton.Enabled = canSpectate;
                spectateButton.Click += async (_, _) => await StartSpectateAsync(gameId, spectateButton, actionLabel);
                Controls.Add(spectateButton);

                string? note = friend.StatusMessage;
                var toolTip = new ToolTip();
                toolTip.SetToolTip(this, BuildToolTip(displayName, note));
                toolTip.SetToolTip(avatar, BuildToolTip(displayName, note));
                toolTip.SetToolTip(nameLabel, BuildToolTip(displayName, note));
                toolTip.SetToolTip(statusLabel, BuildToolTip(displayName, note));

                AttachDoubleClick(this);
                MouseEnter += (_, _) => BackColor = Color.FromArgb(235, 242, 252);
                MouseLeave += (_, _) => BackColor = Color.White;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using var pen = new Pen(Color.FromArgb(225, 229, 234));
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            private void AttachDoubleClick(Control control)
            {
                if (control is Button) return;
                control.DoubleClick += (_, _) => OpenBattleQuery();
                foreach (Control child in control.Controls)
                {
                    AttachDoubleClick(child);
                }
            }

            private void OpenBattleQuery()
            {
                if (_querying || string.IsNullOrWhiteSpace(_friend.Puuid)) return;
                _querying = true;
                _ = BattleQueryForm.QueryPlayerAsync(_friend.Puuid);
            }

            /// <summary>用户点击观战按钮后才调用 LCU，不会自动观战任何好友。</summary>
            private async Task StartSpectateAsync(long gameId, Button button, Label actionLabel)
            {
                if (_spectating || string.IsNullOrWhiteSpace(_friend.Puuid) || gameId <= 0) return;
                _spectating = true;
                button.Enabled = false;
                actionLabel.Text = "正在向客户端发起观战...";
                try
                {
                    SpectateResult result = await _spectateService.LaunchAsync(new SpectateRequest(_friend.Puuid, gameId));
                    actionLabel.Text = result.Message;
                    if (result.Succeeded) AntdUI.Message.success(Program.GameMain, result.Message);
                    else AntdUI.Message.error(Program.GameMain, result.Message);
                }
                finally
                {
                    _spectating = false;
                    button.Enabled = _friend.CanSpectate;
                }
            }

            private static Button CreateActionButton(string text, Color color)
            {
                var button = new Button
                {
                    AutoSize = false,
                    BackColor = color,
                    Cursor = Cursors.Hand,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Size = new Size(70, 28),
                    Text = text,
                    UseVisualStyleBackColor = false
                };
                button.FlatAppearance.BorderSize = 0;
                return button;
            }

            private static string BuildToolTip(string name, string? note)
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{name}\n双击查看该好友战绩"
                    : $"{name}\n{note}\n双击查看该好友战绩";
            }

            private static string GetStatusText(FriendProfile friend)
            {
                string status = friend.Presence switch
                {
                    FriendPresence.Online => "在线",
                    FriendPresence.Away => "离开",
                    FriendPresence.DoNotDisturb => "请勿打扰",
                    FriendPresence.Mobile => "手机在线",
                    FriendPresence.InGame => "游戏中",
                    FriendPresence.Spectating => "观战中",
                    FriendPresence.Offline => "离线",
                    _ => "状态未知"
                };

                if (!string.IsNullOrWhiteSpace(friend.GameQueueType) && friend.Presence == FriendPresence.InGame)
                {
                    status += $" · {friend.GameQueueType}";
                }
                return status;
            }

            private static Color GetStatusColor(FriendPresence presence)
            {
                return presence switch
                {
                    FriendPresence.Online or FriendPresence.Mobile => Color.FromArgb(46, 125, 50),
                    FriendPresence.Away or FriendPresence.DoNotDisturb => Color.FromArgb(245, 124, 0),
                    FriendPresence.InGame or FriendPresence.Spectating => Color.FromArgb(30, 136, 229),
                    _ => Color.FromArgb(158, 158, 158)
                };
            }

            private static async Task LoadAvatarAsync(RoundPictureBox box, int iconId, IProfileIconService profileIconService)
            {
                if (iconId <= 0) return;
                try
                {
                    byte[]? bytes = await profileIconService.GetProfileIconAsync(iconId);
                    if (bytes == null || bytes.Length == 0) return;
                    using var memory = new MemoryStream(bytes);
                    using var temp = Image.FromStream(memory);
                    var image = new Bitmap(temp);
                    if (box.IsDisposed)
                    {
                        image.Dispose();
                        return;
                    }
                    box.Image = image;
                }
                catch
                {
                    // 好友头像加载失败不影响好友状态和查询功能。
                }
            }
        }
    }
}
