using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using System.Collections.Concurrent;
using System.Data;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class RecordForm : UserControl
    {
        private MatchDetail? _gameDetail;
        private string? _playerPuuid;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IGameAssetService _gameAssetService;
        private readonly Label _performanceTag = new();
        private readonly ToolTip _performanceTip = new();
        private readonly Dictionary<Control, Image> _ownedImages = new();

        /// <summary>判定取数范围：先拉最近这些场摘要，再从中筛出同模式对局。</summary>
        private const int HistoryFetchCount = 100;

        /// <summary>最多取最新这些场同模式对局参与判定。</summary>
        private const int RecentSampleSize = 12;

        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<MatchHistoryGame[]> Games)> RecentHistoryCache = new(StringComparer.Ordinal);
        private static readonly TimeSpan RecentHistoryCacheTtl = TimeSpan.FromMinutes(3);

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
            this.BackColor = win ? System.Drawing.Color.FromArgb(250, 250, 250) : System.Drawing.Color.FromArgb(242, 242, 242);
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

        /// <summary>将单局 DTO 转换为领域快照后交给纯领域服务评测。</summary>
        private static MatchPerformanceAssessment EvaluatePostGamePerformance(MatchDetail game, string puuid)
        {
            var snapshots = game.participants
                .Where(item => item.stats != null)
                .Select(item => new MatchPerformanceSnapshot(
                    game.participantIdentities
                        .FirstOrDefault(identity => identity.participantId == item.participantId)?.player?.puuid
                        ?? $"participant-{item.participantId}",
                    item.teamId,
                    item.IsWin(),
                    item.stats!.kills,
                    item.stats.deaths,
                    item.stats.assists,
                    item.stats.totalDamageDealtToChampions,
                    item.stats.goldEarned,
                    item.stats.visionScore))
                .ToList();
            MatchPerformanceSnapshot? player = snapshots.FirstOrDefault(item => item.PlayerId == puuid);
            return MatchPerformanceEvaluator.Evaluate(player, snapshots);
        }

        /// <summary>
        /// “上/中/下等马”只依据该玩家最近同一模式的已结束战绩，而非当前单局。
        /// 先从最近 100 场摘要里筛出同模式对局，再只拉这些对局的详情参与判定；
        /// 同一玩家的摘要与详情在短时间内共享缓存，首页同时渲染多张卡片不会重复拉取。
        /// </summary>
        private async Task ApplyRecentModePerformanceTagAsync(MatchDetail currentGame, string puuid)
        {
            string mode = currentGame.GetModeText();
            MatchHistoryGame[] sameMode = (await GetRecentHistoryAsync(puuid))
                .Where(game => SameMode(game, currentGame))
                .OrderByDescending(game => game.GameCreation)
                .Take(RecentSampleSize)
                .ToArray();

            MatchDetail[] comparable = await LoadDetailsAsync(sameMode);
            var assessments = new List<MatchPerformanceAssessment>();
            var wins = new List<bool>();
            foreach (var detail in comparable)
            {
                var participant = detail.GetParticipant(puuid);
                if (participant?.stats == null) continue;
                assessments.Add(EvaluatePostGamePerformance(detail, puuid));
                wins.Add(participant.IsWin());
            }
            ApplyPostGamePerformanceTag(RecentModePerformanceEvaluator.Evaluate(mode, assessments, wins));
        }

        private async Task<MatchHistoryGame[]> GetRecentHistoryAsync(string puuid)
        {
            if (RecentHistoryCache.TryGetValue(puuid, out var cached) && DateTime.UtcNow - cached.CachedAt < RecentHistoryCacheTtl)
                return await cached.Games;

            Task<MatchHistoryGame[]> task = LoadRecentHistoryAsync(puuid);
            RecentHistoryCache[puuid] = (DateTime.UtcNow, task);
            try
            {
                return await task;
            }
            catch
            {
                RecentHistoryCache.TryRemove(puuid, out _);
                throw;
            }
        }

        private async Task<MatchHistoryGame[]> LoadRecentHistoryAsync(string puuid)
        {
            MatchHistoryResponse? history = await _matchHistoryService.GetPageAsync(puuid, 0, HistoryFetchCount - 1);
            return history?.Games?.Games
                .OrderByDescending(game => game.GameCreation)
                .Take(HistoryFetchCount)
                .ToArray() ?? Array.Empty<MatchHistoryGame>();
        }

        private async Task<MatchDetail[]> LoadDetailsAsync(IReadOnlyList<MatchHistoryGame> heads)
        {
            using var gate = new SemaphoreSlim(4, 4);
            var tasks = heads.Select(async head =>
            {
                await gate.WaitAsync();
                try { return await _matchHistoryService.GetDetailAsync(head.GameId); }
                finally { gate.Release(); }
            }).ToList();
            return (await Task.WhenAll(tasks)).Where(detail => detail != null).Cast<MatchDetail>().ToArray();
        }

        private static bool SameMode(MatchHistoryGame candidate, MatchDetail current)
        {
            string currentQueue = current.queueId ?? current._queueId ?? "";
            if (int.TryParse(currentQueue, out int queueId) && candidate.QueueId > 0)
                return candidate.QueueId == queueId;
            return string.Equals(candidate.GameMode, current.gameMode, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.GameMode, current.GetModeText(), StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyPostGamePerformanceTag(RecentModePerformanceAssessment assessment)
        {
            if (!assessment.HasEnoughSample)
            {
                _performanceTag.Text = "样本不足";
                _performanceTag.ForeColor = UiTheme.Palette.TextSecondary;
                _performanceTag.BackColor = UiTheme.Palette.IsDark ? UiTheme.Palette.SurfaceMuted : Color.FromArgb(245, 245, 245);
                _performanceTip.SetToolTip(_performanceTag, assessment.Detail);
                return;
            }

            string tag = RecentPerformanceLabelFormatter.GetText(assessment);
            _performanceTag.Text = tag;
            _performanceTag.ForeColor = assessment.Label switch
            {
                RecentPerformanceLabel.Upper => Color.FromArgb(27, 94, 32),
                RecentPerformanceLabel.Human => Color.FromArgb(123, 31, 162),
                RecentPerformanceLabel.Lower => Color.FromArgb(183, 28, 28),
                _ => Color.FromArgb(85, 85, 85)
            };
            _performanceTag.BackColor = assessment.Label switch
            {
                RecentPerformanceLabel.Upper => Color.FromArgb(232, 245, 233),
                RecentPerformanceLabel.Human => Color.FromArgb(243, 229, 245),
                RecentPerformanceLabel.Lower => Color.FromArgb(255, 235, 238),
                _ => Color.FromArgb(245, 245, 245)
            };
            _performanceTip.SetToolTip(_performanceTag, $"同模式近期表现 · {assessment.Score} 分\n{assessment.Detail}");
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

        private void ReplaceImage(AntdUI.Avatar box, Image? image)
        {
            if (_ownedImages.Remove(box, out Image? old)) old.Dispose();
            box.Image = image;
            if (image != null) _ownedImages[box] = image;
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