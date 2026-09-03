using System.Drawing.Drawing2D;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using static LOL_GameAssistant.Entity.PlayerModel;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局玩家卡片：圆角描边 + 悬停发光，玩家信息 + 当前英雄/位置 + 近 10 场战绩（英雄头像、滑入动效）。
    /// </summary>
    public partial class LivePlayerForm : UserControl
    {
        private readonly string? _playerPuuid;
        private readonly int _championId;
        private readonly string _position;
        private readonly bool _isBot;
        private readonly bool _isAlly;
        private readonly bool _teamKnown;
        private readonly Color _teamColor;
        private const int RecentGamesCount = 10;
        private Image? _ownedProfileImage;
        private ToolTip? _premadeTip;

        /// <summary>当前卡片对应玩家的 puuid（供开黑检测结果回填）。</summary>
        public string? Puuid => _playerPuuid;

        /// <summary>开黑小组配色（按组号轮换）。</summary>
        private static readonly Color[] PremadeColors =
        {
            Color.FromArgb(230, 126, 34),
            Color.FromArgb(142, 68, 173),
            Color.FromArgb(22, 160, 133),
            Color.FromArgb(231, 76, 60),
            Color.FromArgb(52, 152, 219)
        };

        private readonly System.Windows.Forms.Timer _glowTimer;
        private bool _glowTarget;
        private double _glowT;

        public LivePlayerForm(
            string? playerPuuid,
            string? fallbackName = null,
            int championId = 0,
            string? position = null,
            bool isBot = false,
            bool isAlly = false,
            bool teamKnown = false)
        {
            InitializeComponent();
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _playerPuuid = playerPuuid;
            _championId = championId;
            _position = position ?? "";
            _isBot = isBot;
            _isAlly = isAlly;
            _teamKnown = teamKnown;
            _teamColor = !teamKnown || isAlly
                ? Color.FromArgb(30, 136, 229)
                : Color.FromArgb(211, 47, 47);
            BackColor = Color.FromArgb(252, 252, 253);

            lblName.Text = string.IsNullOrEmpty(fallbackName) ? "未知玩家" : fallbackName!;
            lblSub.Text = _isBot ? "机器人" : "";
            btnCopy.Visible = !_isBot && !string.IsNullOrEmpty(_playerPuuid);

            // 队友/对手标识：同队显示“队友”（蓝色），异队显示“对手”（红色）
            lblTeamTag.Text = isAlly ? "队友" : "对手";
            lblTeamTag.ForeColor = _teamColor;
            lblTeamTag.BackColor = Color.FromArgb(
                isAlly ? 226 : 253,
                isAlly ? 240 : 236,
                isAlly ? 253 : 236);
            lblTeamTag.Visible = teamKnown;
            if (teamKnown)
            {
                headerPanel.BackColor = Color.FromArgb(
                    isAlly ? 235 : 253,
                    isAlly ? 244 : 240,
                    isAlly ? 252 : 240);
            }

            // 复制按钮悬停提示：显示可复制的完整 ID
            var copyTip = new ToolTip();
            copyTip.SetToolTip(btnCopy, "复制该玩家 PUUID（可用于精确查询）");
            if (!string.IsNullOrEmpty(_playerPuuid))
            {
                copyTip.SetToolTip(this, $"PUUID: {_playerPuuid}");
            }

            _glowTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _glowTimer.Tick += (_, _) => GlowTick();
            Disposed += (_, _) =>
            {
                _glowTimer.Dispose();
                _ownedProfileImage?.Dispose();
            };

            this.MouseEnter += (_, _) => StartGlow(true);
            this.MouseLeave += (_, _) => StartGlow(false);
            foreach (Control child in Controls)
            {
                child.MouseEnter += (_, _) => StartGlow(true);
                child.MouseLeave += (_, _) => StartGlow(false);
            }

            this.Resize += (_, _) => RecalcHeaderLayout();
            RecalcHeaderLayout();
            this.Load += async (_, _) => await LoadAsync();
        }

        /// <summary>
        /// 根据卡片宽度自适应排列头部控件（窄卡片时名称收缩、右侧控件贴边）。
        /// </summary>
        private void RecalcHeaderLayout()
        {
            int right = Width - 12;
            int leftEnd = right - 240;

            // 队友/对手标识放在第二行（当前英雄文字右侧），避免挤压名称和胜率
            if (_teamKnown)
            {
                lblTeamTag.Location = new Point(right - 138, 32);
                lblTeamTag.Size = new Size(36, 20);
            }
            // 开黑标记放在队友/对手标识左侧
            if (lblPremadeTag.Visible)
            {
                lblPremadeTag.Location = new Point(right - 190, 32);
                lblPremadeTag.Size = new Size(46, 20);
            }
            btnCopy.Location = new Point(right - 62, 8);
            picCurrent.Location = new Point(right - 96, 10);
            lblSummary.Location = new Point(leftEnd, 8);
            lblSummary.Width = Math.Max(60, right - 96 - leftEnd - 4);
            lblChampionNow.Location = new Point(leftEnd, 32);
            int championEnd = lblPremadeTag.Visible
                ? right - 190 - 4
                : _teamKnown
                    ? right - 138 - 4
                    : right - 62 - 4;
            lblChampionNow.Width = Math.Max(60, championEnd - leftEnd);

            int nameWidth = Math.Max(80, leftEnd - 56 - 8);
            lblName.Width = nameWidth;
            lblSub.Width = nameWidth;
        }

        /// <summary>
        /// 设置/清除开黑标记（由对局页开黑检测结果回填）。
        /// </summary>
        /// <param name="groupIndex">开黑组号（1 起），null 表示不在任何开黑小组。</param>
        /// <param name="memberNames">同组玩家名称（用于悬停提示）。</param>
        public void SetPremadeGroup(int? groupIndex, List<string>? memberNames)
        {
            if (!groupIndex.HasValue)
            {
                lblPremadeTag.Visible = false;
                RecalcHeaderLayout();
                return;
            }

            var color = PremadeColors[(groupIndex.Value - 1) % PremadeColors.Length];
            lblPremadeTag.Text = $"开黑{groupIndex.Value}";
            lblPremadeTag.ForeColor = color;
            lblPremadeTag.BackColor = Color.FromArgb(
                255,
                Math.Min(255, color.R + 95),
                Math.Min(255, color.G + 95),
                Math.Min(255, color.B + 95));

            _premadeTip ??= new ToolTip();
            string tooltip = memberNames is { Count: > 0 }
                ? $"开黑小组：{string.Join("、", memberNames)}（近期多次同队）"
                : "开黑小组（近期多次同队）";
            _premadeTip.SetToolTip(lblPremadeTag, tooltip);

            lblPremadeTag.Visible = true;
            RecalcHeaderLayout();
        }

        private async Task LoadAsync()
        {
            if (_isBot || string.IsNullOrEmpty(_playerPuuid))
            {
                await RenderBotHeaderAsync();
                return;
            }

            ShowShimmer();

            // ── 玩家信息（名称/等级/头像） ──
            string displayName = lblName.Text ?? "未知玩家";
            string? tagLine = "";
            string? level = null;
            string? profileIconId = null;
            try
            {
                string json = await Assets_api.GetUser(_playerPuuid);
                if (!string.IsNullOrEmpty(json))
                {
                    var info = JsonConvert.DeserializeObject<Plyaer>(json);
                    if (info != null)
                    {
                        if (!string.IsNullOrEmpty(info.gameName)) displayName = info.gameName;
                        tagLine = info.tagLine;
                        level = info.summonerLevel;
                        profileIconId = info.profileIconId;
                    }
                }
            }
            catch
            {
                // 玩家信息获取失败时使用兜底名称
            }

            if (IsDisposed) return;
            lblName.Text = displayName;
            string positionText = GetPositionText(_position);
            lblSub.Text = string.IsNullOrEmpty(level)
                ? (string.IsNullOrEmpty(tagLine) ? positionText : $"#{tagLine} {positionText}")
                : $"Lv.{level}  #{tagLine} {positionText}".Trim();

            await LoadProfileIconAsync(profileIconId);
            await LoadCurrentChampionAsync();

            // ── 近 10 场战绩 ──
            var matchlists = await Game_Api.GetUserGame(_playerPuuid, "0", (RecentGamesCount - 1).ToString());
            if (matchlists?.Games?.Games == null || IsDisposed) return;

            var games = matchlists.Games.Games
                .OrderByDescending(g => g.GameCreation)
                .Take(RecentGamesCount)
                .ToList();

            // 并发加载每场详情
            var semaphore = new SemaphoreSlim(4, 4);
            var tasks = games.Select(async head =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var detail = await Game_Api.GetGameDetail(head.GameId);
                    if (detail == null || string.IsNullOrEmpty(_playerPuuid))
                        return (detail: (GameDetailModel.GameInfo?)null, gamer: (GameDetailModel.ParticipantsItem?)null);
                    var gamer = detail.GetParticipant(_playerPuuid);
                    return (detail, gamer);
                }
                catch
                {
                    return (detail: (GameDetailModel.GameInfo?)null, gamer: (GameDetailModel.ParticipantsItem?)null);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = (await Task.WhenAll(tasks))
                .Where(r => r.detail != null && r.gamer != null)
                .Select(r => (detail: r.detail!, gamer: r.gamer!))
                .ToList();

            if (IsDisposed) return;

            int wins = results.Count(r => r.gamer.IsWin());
            int losses = results.Count - wins;
            double rate = results.Count > 0 ? Math.Round((double)wins / results.Count * 100, 1) : 0;
            lblSummary.Text = results.Count > 0 ? $"近{results.Count}场 {wins}胜{losses}负 · {rate}%" : "暂无战绩";

            // 清掉加载微光，手工定位渲染战绩行（新→旧）
            panelMatches.Controls.Clear();
            int y = 0;
            for (int i = 0; i < results.Count; i++)
            {
                var (detail, gamer) = results[i];
                var row = new RecentMatchRow
                {
                    Location = new Point(0, y),
                    Width = Math.Max(100, panelMatches.ClientSize.Width - 18),
                    Height = RecentMatchRow.RowHeight
                };
                panelMatches.Controls.Add(row);
                y += RecentMatchRow.RowHeight;
                UiAnimation.SlideIn(row, -16, 220, i * 35);
                _ = row.SetDataAsync(detail, gamer, _playerPuuid);
            }
            panelMatches.AutoScrollMinSize = new Size(panelMatches.ClientSize.Width, y);
        }

        private void ShowShimmer()
        {
            panelMatches.Controls.Clear();
            var shimmer = new ShimmerPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(246, 248, 251)
            };
            panelMatches.Controls.Add(shimmer);
        }

        private async Task RenderBotHeaderAsync()
        {
            lblName.Text = string.IsNullOrEmpty(lblName.Text) ? "机器人" : lblName.Text;
            lblSub.Text = "机器人";
            btnCopy.Visible = false;
            lblSummary.Text = "";
            if (_championId > 0)
            {
                lblChampionNow.Text = $"当前: {ChampionMap.GetChampion(_championId)?.RealName ?? $"英雄{_championId}"}";
                lblChampionNow.Visible = true;
                var icon = await Game_Api.GetGameChampionIconAsync(_championId);
                if (icon != null && !IsDisposed) picCurrent.Image = icon;
            }
            panelMatches.Controls.Clear();
            panelMatches.Controls.Add(new AntdUI.Label
            {
                Text = "机器人没有战绩数据",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(8)
            });
        }

        private async Task LoadProfileIconAsync(string? profileIconId)
        {
            if (string.IsNullOrEmpty(profileIconId)) return;
            try
            {
                using Stream? stream = await Assets_api.GetImg(profileIconId);
                if (stream != null && stream != Stream.Null && !IsDisposed)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    ms.Position = 0;
                    using var temp = Image.FromStream(ms);
                    _ownedProfileImage = new Bitmap(temp);
                    picProfile.Image = _ownedProfileImage;
                }
            }
            catch
            {
                // 头像加载失败不影响卡片
            }
        }

        private async Task LoadCurrentChampionAsync()
        {
            if (_championId <= 0) return;
            try
            {
                lblChampionNow.Text = $"当前: {ChampionMap.GetChampion(_championId)?.RealName ?? $"英雄{_championId}"}";
                lblChampionNow.Visible = true;
                var icon = await Game_Api.GetGameChampionIconAsync(_championId);
                if (icon != null && !IsDisposed) picCurrent.Image = icon;
            }
            catch
            {
                // 当前英雄加载失败不影响卡片
            }
        }

        private static string GetPositionText(string position)
        {
            return position.ToLowerInvariant() switch
            {
                "top" => "上路",
                "jungle" => "打野",
                "middle" => "中路",
                "bottom" => "下路",
                "utility" or "support" => "辅助",
                "fill" => "补位",
                _ => position
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = GradientPanel.RoundedRect(rect, 12);

            // 悬停发光外圈
            if (_glowAlpha > 0.02)
            {
                using var glow = new Pen(
                    Color.FromArgb((int)(_glowAlpha * 70), _teamColor.R, _teamColor.G, _teamColor.B), 5);
                g.DrawPath(glow, path);
            }

            using var border = new Pen(
                Color.FromArgb((int)(80 + _glowAlpha * 90), _teamColor.R, _teamColor.G, _teamColor.B), 1.5f);
            g.DrawPath(border, path);
        }

        private void StartGlow(bool hovering)
        {
            _glowTarget = hovering;
            _glowT = 0;
            _glowTimer.Start();
        }

        private void GlowTick()
        {
            _glowT = Math.Min(1, _glowT + 0.12);
            double eased = UiAnimation.EaseOutCubic(_glowT);
            if (_glowTarget)
            {
                _glowAlpha = eased;
            }
            else
            {
                _glowAlpha = 1 - eased;
            }
            Invalidate();
            if (_glowT >= 1) _glowTimer.Stop();
        }

        private double _glowAlpha;

        private void BtnCopy_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_playerPuuid)) return;
            try
            {
                Clipboard.SetText(_playerPuuid);
                string preview = _playerPuuid.Length > 16
                    ? _playerPuuid[..16] + "..."
                    : _playerPuuid;
                AntdUI.Message.success(
                    ParentForm ?? FindForm() ?? Program.GameMain,
                    $"已复制玩家 ID（{preview}）");
            }
            catch
            {
                // 剪贴板被占用时忽略
            }
        }
    }
}
