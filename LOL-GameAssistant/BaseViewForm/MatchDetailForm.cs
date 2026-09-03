using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局详情弹窗：完整展示本局 10 名玩家（我方/敌方、头像、英雄、KDA、伤害），
    /// 底部展示当前玩家详细数据。
    /// </summary>
    public partial class MatchDetailForm : Form
    {
        private readonly GameDetailModel.GameInfo _gameInfo;
        private readonly string _puuid;

        /// <summary>点击玩家头像后选中的玩家 puuid（用于跳转战绩查询）。</summary>
        public string? SelectedPlayerPuuid { get; private set; }

        public MatchDetailForm(GameDetailModel.GameInfo? gameInfo, string? puuid)
        {
            if (gameInfo == null) throw new ArgumentNullException(nameof(gameInfo));
            if (string.IsNullOrEmpty(puuid)) throw new ArgumentNullException(nameof(puuid));
            _gameInfo = gameInfo;
            _puuid = puuid;
            InitializeComponent();
            this.Load += async (_, _) => await LoadDataAsync();
        }

        /// <summary>
        /// 打开对局详情；若用户点击了某位玩家头像，关闭后自动跳转到战绩查询该玩家。
        /// </summary>
        public static void OpenAndHandle(GameDetailModel.GameInfo detail, string? puuid, Control? parent)
        {
            var form = new MatchDetailForm(detail, puuid)
            {
                StartPosition = FormStartPosition.CenterParent
            };
            form.ShowDialog(parent?.FindForm() ?? Program.GameMain);

            if (!string.IsNullOrEmpty(form.SelectedPlayerPuuid))
            {
                _ = BattleQueryForm.QueryPlayerAsync(form.SelectedPlayerPuuid);
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var gamer = _gameInfo.GetParticipant(_puuid);
                if (gamer == null) return;

                bool isWin = gamer.IsWin();
                lblTitle.Text = $"{_gameInfo.GetModeText()} · {(isWin ? "胜利" : "失败")} · {_gameInfo.GetDurationText()}";
                lblTitle.ForeColor = isWin ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);

                var champ = ChampionMap.GetChampion(gamer.championId);
                string champName = champ?.RealName ?? $"英雄{gamer.championId}";
                lblChampion.Text = $"{champName}  |  等级 {gamer.stats?.champLevel}";
                lblKda.Text = $"KDA: {gamer.GetKdaText()}  ({gamer.GetKdaRatio()})";

                var s = gamer.stats;
                int cs = (s?.totalMinionsKilled ?? 0) + (s?.neutralMinionsKilled ?? 0);
                lblStats.Text =
                    $"补刀: {cs} (CS/min: {Math.Round(cs / Math.Max(1, (double)_gameInfo.gameDuration / 60), 1)})  |  " +
                    $"金币: {s?.goldEarned ?? 0}  |  伤害: {s?.totalDamageDealtToChampions ?? 0}  |  " +
                    $"承受: {s?.totalDamageTaken ?? 0}  |  治疗: {s?.totalHeal ?? 0}\n" +
                    $"视野分: {s?.visionScore ?? 0}  |  控制: {s?.totalTimeCrowdControlDealt ?? 0}s  |  " +
                    $"多杀: {s?.doubleKills ?? 0}双 {s?.tripleKills ?? 0}三 {s?.quadraKills ?? 0}四 {s?.pentaKills ?? 0}五";

                int[] items = { s?.item0 ?? 0, s?.item1 ?? 0, s?.item2 ?? 0,
                                s?.item3 ?? 0, s?.item4 ?? 0, s?.item5 ?? 0, s?.item6 ?? 0 };
                var nameTasks = items.Where(i => i > 0).Select(Game_Api.GetItemNameAsync).ToList();
                var resolvedNames = nameTasks.Count > 0 ? await Task.WhenAll(nameTasks) : Array.Empty<string?>();
                int nameIndex = 0;
                var itemNames = items.Select(itemId =>
                {
                    if (itemId <= 0) return "(空)";
                    string? name = nameIndex < resolvedNames.Length ? resolvedNames[nameIndex++] : null;
                    return string.IsNullOrEmpty(name) ? $"装备{itemId}" : name;
                }).ToList();
                lblItems.Text = $"装备: {string.Join(" | ", itemNames)}";

                BuildPlayerPanels();
            }
            catch (Exception ex)
            {
                lblTitle.Text = "加载失败";
                lblStats.Text = $"错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 构建本局 10 名玩家面板（我方/敌方各 5 人，当前玩家高亮）。
        /// </summary>
        private void BuildPlayerPanels()
        {
            flowAlly.Controls.Clear();
            flowEnemy.Controls.Clear();

            var participants = _gameInfo.participants ?? new List<GameDetailModel.ParticipantsItem>();
            var identities = _gameInfo.participantIdentities ?? new List<GameDetailModel.ParticipantIdentitiesItem>();
            int myTeamId = _gameInfo.GetParticipant(_puuid)?.teamId ?? 100;

            foreach (var p in participants.OrderBy(p => p.participantId))
            {
                var identity = identities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
                bool isMe = identity?.puuid == _puuid;
                var cell = CreatePlayerCell(p, identity, isMe);

                if (p.teamId == myTeamId)
                    flowAlly.Controls.Add(cell);
                else
                    flowEnemy.Controls.Add(cell);
            }
        }

        /// <summary>
        /// 创建单个玩家信息卡片（头像、名称、英雄、KDA、伤害、胜负）。
        /// </summary>
        private Control CreatePlayerCell(
            GameDetailModel.ParticipantsItem p,
            GameDetailModel.Player? identity,
            bool isMe)
        {
            bool win = p.IsWin();
            var panel = new Panel
            {
                Size = new Size(455, 76),
                Margin = new Padding(0, 0, 0, 6),
                BackColor = isMe ? Color.FromArgb(255, 249, 230) : Color.FromArgb(250, 250, 252)
            };

            var avatar = new RoundPictureBox
            {
                Size = new Size(54, 54),
                Location = new Point(8, 10),
                BorderWidth = isMe ? 3 : 1,
                BorderColor = isMe ? Color.FromArgb(255, 193, 7) : Color.FromArgb(120, 255, 255, 255)
            };
            _ = LoadAvatarAsync(avatar, p.championId);
            panel.Controls.Add(avatar);

            string displayName = (isMe ? "★ " : "") + (identity?.gameName ?? $"玩家{p.participantId}");

            // 双击英雄 → 跳转战绩查询该玩家
            string? playerPuuid = identity?.puuid;
            if (!string.IsNullOrEmpty(playerPuuid))
            {
                avatar.Cursor = Cursors.Hand;
                var tip = new ToolTip();
                tip.SetToolTip(avatar, $"双击查询 {displayName} 的战绩");
                avatar.DoubleClick += (_, _) =>
                {
                    SelectedPlayerPuuid = playerPuuid;
                    Close();
                };
            }

            panel.Controls.Add(new Label
            {
                Text = displayName,
                Location = new Point(70, 6),
                Size = new Size(240, 22),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent
            });
            var championLabel = new Label
            {
                Text = ChampionMap.GetChampion(p.championId)?.RealName ?? $"英雄{p.championId}",
                Location = new Point(70, 28),
                Size = new Size(200, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SystemColors.GrayText,
                BackColor = Color.Transparent
            };
            if (!string.IsNullOrEmpty(playerPuuid))
            {
                championLabel.Cursor = Cursors.Hand;
                championLabel.DoubleClick += (_, _) =>
                {
                    SelectedPlayerPuuid = playerPuuid;
                    Close();
                };
            }
            panel.Controls.Add(championLabel);
            panel.Controls.Add(new Label
            {
                Text = $"KDA {p.GetKdaText()} ({p.GetKdaRatio()})",
                Location = new Point(70, 48),
                Size = new Size(200, 20),
                Font = new Font("Microsoft YaHei UI", 9F),
                BackColor = Color.Transparent
            });
            panel.Controls.Add(new Label
            {
                Text = win ? "胜利" : "失败",
                Location = new Point(330, 6),
                Size = new Size(80, 22),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = win ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40),
                BackColor = Color.Transparent
            });
            panel.Controls.Add(new Label
            {
                Text = $"伤害 {p.stats?.totalDamageDealtToChampions ?? 0}",
                Location = new Point(330, 30),
                Size = new Size(120, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SystemColors.GrayText,
                BackColor = Color.Transparent
            });
            panel.Controls.Add(new Label
            {
                Text = $"补刀 {(p.stats?.totalMinionsKilled ?? 0) + (p.stats?.neutralMinionsKilled ?? 0)}",
                Location = new Point(330, 50),
                Size = new Size(120, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SystemColors.GrayText,
                BackColor = Color.Transparent
            });
            return panel;
        }

        /// <summary>
        /// 异步加载英雄头像（全局缓存）。
        /// </summary>
        private static async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            var icon = await Game_Api.GetGameChampionIconAsync(championId);
            if (icon != null && !box.IsDisposed)
            {
                box.Image = icon;
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
