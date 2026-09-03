using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

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
        private bool _loading;

        public FriendsForm()
        {
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
                var friends = await Game_Api.GetFriendsAsync();
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

        private void RenderFriends(List<FriendModel> friends)
        {
            _friendList.SuspendLayout();
            try
            {
                _friendList.Controls.Clear();

                var ordered = friends
                    .OrderByDescending(IsOnline)
                    .ThenBy(GetDisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                _emptyLabel.Visible = ordered.Count == 0;
                if (ordered.Count == 0)
                {
                    _emptyLabel.Text = "暂无好友数据，或 LOL 客户端暂未返回好友列表";
                    _statusLabel.Text = "共 0 位好友";
                    return;
                }

                int onlineCount = ordered.Count(IsOnline);
                _statusLabel.Text =
                    $"共 {ordered.Count} 位好友 · 在线 {onlineCount} 位 · 更新时间 {DateTime.Now:HH:mm:ss} · 双击好友查看战绩";

                foreach (var friend in ordered)
                {
                    var card = new FriendCard(friend)
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

        private static bool IsOnline(FriendModel friend)
        {
            string availability = friend.Availability?.Trim().ToLowerInvariant() ?? "";
            return availability is not ("offline" or "invisible" or "unknown" or "");
        }

        private static string GetDisplayName(FriendModel friend)
        {
            if (!string.IsNullOrWhiteSpace(friend.DisplayName)) return friend.DisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(friend.GameName))
            {
                string name = friend.GameName.Trim();
                if (!string.IsNullOrWhiteSpace(friend.TagLine) && !name.Contains('#'))
                    return $"{name}#{friend.TagLine.TrimStart('#')}";
                return name;
            }
            return string.IsNullOrWhiteSpace(friend.SummonerName) ? "未知好友" : friend.SummonerName.Trim();
        }

        private sealed class FriendCard : Panel
        {
            private readonly FriendModel _friend;
            private bool _querying;

            public FriendCard(FriendModel friend)
            {
                _friend = friend;
                Height = 78;
                Margin = new Padding(6);
                Padding = new Padding(8);
                BackColor = Color.White;
                BorderStyle = BorderStyle.FixedSingle;
                Cursor = string.IsNullOrWhiteSpace(friend.Puuid) ? Cursors.Default : Cursors.Hand;

                int iconId = friend.Icon > 0 ? friend.Icon : friend.Lol?.Icon ?? 0;
                var avatar = new RoundPictureBox
                {
                    Size = new Size(54, 54),
                    Location = new Point(8, 10),
                    BorderWidth = 2,
                    BorderColor = GetStatusColor(friend.Availability),
                    Cursor = Cursor
                };
                Controls.Add(avatar);
                _ = LoadAvatarAsync(avatar, iconId);

                string displayName = GetDisplayName(friend);
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
                    ForeColor = GetStatusColor(friend.Availability),
                    Text = GetStatusText(friend),
                    BackColor = Color.Transparent,
                    Cursor = Cursor
                };
                Controls.Add(statusLabel);

                var actionLabel = new Label
                {
                    AutoSize = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(330, 27),
                    Size = new Size(140, 22),
                    Font = new Font("Microsoft YaHei UI", 8F),
                    ForeColor = Color.FromArgb(117, 117, 117),
                    Text = string.IsNullOrWhiteSpace(friend.Puuid) ? "暂无战绩入口" : "双击查看战绩",
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent,
                    Cursor = Cursor
                };
                Controls.Add(actionLabel);

                string? note = string.IsNullOrWhiteSpace(friend.StatusMessage)
                    ? friend.Note
                    : friend.StatusMessage;
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

            private static string BuildToolTip(string name, string? note)
            {
                return string.IsNullOrWhiteSpace(note)
                    ? $"{name}\n双击查看该好友战绩"
                    : $"{name}\n{note}\n双击查看该好友战绩";
            }

            private static string GetStatusText(FriendModel friend)
            {
                string availability = friend.Availability?.Trim().ToLowerInvariant() ?? "";
                string status = availability switch
                {
                    "online" or "chat" => "在线",
                    "away" => "离开",
                    "dnd" => "请勿打扰",
                    "mobile" => "手机在线",
                    "ingame" or "ingameother" => "游戏中",
                    "spectator" => "观战中",
                    "offline" or "invisible" => "离线",
                    _ => "状态未知"
                };

                if (!string.IsNullOrWhiteSpace(friend.Lol?.GameQueueType) &&
                    availability is "ingame" or "ingameother")
                {
                    status += $" · {friend.Lol.GameQueueType}";
                }
                return status;
            }

            private static Color GetStatusColor(string? availability)
            {
                return availability?.Trim().ToLowerInvariant() switch
                {
                    "online" or "chat" or "mobile" => Color.FromArgb(46, 125, 50),
                    "away" or "dnd" => Color.FromArgb(245, 124, 0),
                    "ingame" or "ingameother" or "spectator" => Color.FromArgb(30, 136, 229),
                    _ => Color.FromArgb(158, 158, 158)
                };
            }

            private static async Task LoadAvatarAsync(RoundPictureBox box, int iconId)
            {
                if (iconId <= 0) return;
                try
                {
                    using Stream stream = await Assets_api.GetImg(iconId.ToString());
                    if (stream == Stream.Null) return;
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory);
                    memory.Position = 0;
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
