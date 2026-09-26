using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using System.Collections.Concurrent;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局详情弹窗：完整展示本局 10 名玩家（我方/敌方、头像、英雄、KDA、伤害），
    /// 底部展示当前玩家详细数据。
    /// </summary>
    public partial class MatchDetailForm : Form, IThemeAware
    {
        private readonly MatchDetail _gameInfo;
        private readonly string _puuid;
        private readonly IGameAssetService _gameAssetService;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IPremadeDetectionService _premadeDetectionService;
        private readonly Dictionary<string, Label> _premadeTagsByPuuid = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _performanceTagsByPuuid = new(StringComparer.Ordinal);
        private readonly ToolTip _assetToolTip = new();
        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, Task<MatchDetail[]> Details)> RecentModeDetailCache = new();
        private static readonly TimeSpan RecentModeDetailCacheTtl = TimeSpan.FromMinutes(3);
        private static readonly SemaphoreSlim RecentModeDetailLoadGate = new(4, 4);

        /// <summary>判定取数范围：先拉最近这些场摘要，再从中筛出同模式对局。</summary>
        private const int HistoryFetchCount = 100;

        /// <summary>最多取最新这些场同模式对局参与判定。</summary>
        private const int RecentSampleSize = 12;

        /// <summary>点击玩家头像后选中的玩家 puuid（用于跳转战绩查询）。</summary>
        public string? SelectedPlayerPuuid { get; private set; }

        public MatchDetailForm(MatchDetail? gameInfo, string? puuid)
            : this(gameInfo, puuid, AppCompositionRoot.GameAssetService)
        {
        }

        /// <summary>对局详情只经由资源应用服务读取装备名与英雄头像。</summary>
        internal MatchDetailForm(MatchDetail? gameInfo, string? puuid, IGameAssetService gameAssetService)
        {
            if (gameInfo == null) throw new ArgumentNullException(nameof(gameInfo));
            if (string.IsNullOrEmpty(puuid)) throw new ArgumentNullException(nameof(puuid));
            _gameInfo = gameInfo;
            _puuid = puuid;
            _gameAssetService = gameAssetService;
            _matchHistoryService = AppCompositionRoot.MatchHistoryService;
            _premadeDetectionService = AppCompositionRoot.PremadeDetectionService;
            InitializeComponent();
            UiTheme.Apply(this);
            this.Load += async (_, _) => await LoadDataAsync();
            Disposed += (_, _) => _assetToolTip.Dispose();
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Surface;
            flowAlly.BackColor = palette.Surface;
            flowEnemy.BackColor = palette.Surface;
            lblAllyHeader.ForeColor = palette.TextPrimary;
            lblEnemyHeader.ForeColor = palette.TextPrimary;
        }

        /// <summary>
        /// 打开对局详情；若用户点击了某位玩家头像，关闭后自动跳转到战绩查询该玩家。
        /// </summary>
        public static void OpenAndHandle(MatchDetail detail, string? puuid, Control? parent)
        {
            using var form = new MatchDetailForm(detail, puuid)
            {
                StartPosition = FormStartPosition.CenterParent
            };
            Form owner = parent?.FindForm() ?? Program.GameMain;
            Rectangle available = Screen.FromControl(owner).WorkingArea;
            form.Size = new Size(Math.Min(form.Width, Math.Max(1, available.Width - 32)),
                Math.Min(form.Height, Math.Max(1, available.Height - 32)));
            form.ShowDialog(owner);

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

                string champName = GetChampionDisplayName(gamer.championId);
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
                var nameTasks = items.Where(i => i > 0).Select(itemId => _gameAssetService.GetItemNameAsync(itemId)).ToList();
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
            ControlLifetime.ClearAndDispose(flowAlly);
            ControlLifetime.ClearAndDispose(flowEnemy);
            _premadeTagsByPuuid.Clear();
            _performanceTagsByPuuid.Clear();

            var participants = _gameInfo.participants;
            var identities = _gameInfo.participantIdentities;
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

            AddBanRow(flowAlly, _gameInfo.GetBannedChampionIds(myTeamId));
            int enemyTeamId = participants.FirstOrDefault(p => p.teamId != myTeamId)?.teamId ?? 200;
            AddBanRow(flowEnemy, _gameInfo.GetBannedChampionIds(enemyTeamId));

            _ = DetectPremadesAsync(myTeamId);
            _ = ApplyRecentModePerformanceTagsAsync();
        }

        /// <summary>
        /// 创建单个玩家信息卡片（头像、名称、英雄、KDA、伤害、胜负）。
        /// </summary>
        private Control CreatePlayerCell(
            MatchParticipant p,
            MatchPlayer? identity,
            bool isMe)
        {
            bool win = p.IsWin();
            ThemePalette palette = UiTheme.Palette;
            var panel = new Panel
            {
                Size = new Size(455, _gameInfo.IsAugmentAram() ? 166 : 118),
                Margin = new Padding(0, 0, 0, 6),
                BackColor = isMe
                    ? (palette.IsDark ? Color.FromArgb(75, 60, 25) : Color.FromArgb(255, 249, 230))
                    : palette.SurfaceRaised
            };

            var avatar = new RoundPictureBox
            {
                Size = new Size(54, 54),
                Location = new Point(8, 12),
                BorderWidth = isMe ? 3 : 1,
                BorderColor = isMe ? Color.FromArgb(255, 193, 7) : Color.FromArgb(120, 255, 255, 255)
            };
            avatar.Disposed += (_, _) => avatar.Image?.Dispose();
            _ = LoadAvatarAsync(avatar, p.championId);
            panel.Controls.Add(avatar);

            string displayName = (isMe ? "★ " : "") + (identity?.gameName ?? $"玩家{p.participantId}");

            // 双击英雄 → 跳转战绩查询该玩家
            string? playerPuuid = identity?.puuid;
            if (!string.IsNullOrEmpty(playerPuuid))
            {
                avatar.Cursor = Cursors.Hand;
                _assetToolTip.SetToolTip(avatar, $"双击查询 {displayName} 的战绩");
                avatar.DoubleClick += (_, _) =>
                {
                    SelectedPlayerPuuid = playerPuuid;
                    Close();
                };
            }

            panel.Controls.Add(new Label
            {
                Text = displayName,
                Location = new Point(70, 8),
                Size = new Size(190, 22),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = palette.TextPrimary
            });
            var championLabel = new Label
            {
                Text = $"{GetChampionDisplayName(p.championId)} · KDA {p.GetKdaText()} ({p.GetKdaRatio()})",
                Location = new Point(70, 31),
                Size = new Size(225, 20),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = palette.TextSecondary,
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
            if (!string.IsNullOrWhiteSpace(playerPuuid))
            {
                var performanceTag = new Label
                {
                    AutoSize = false,
                    BackColor = Color.FromArgb(238, 241, 245),
                    ForeColor = Color.FromArgb(90, 90, 90),
                    Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                    Location = new Point(270, 8),
                    Size = new Size(82, 22),
                    Text = "评估中",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                panel.Controls.Add(performanceTag);
                _performanceTagsByPuuid[playerPuuid] = performanceTag;
            }
            panel.Controls.Add(new Label
            {
                Text = win ? "胜利" : "失败",
                Location = new Point(365, 8),
                Size = new Size(72, 22),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = win
                    ? (palette.IsDark ? Color.FromArgb(129, 199, 132) : Color.FromArgb(46, 125, 50))
                    : (palette.IsDark ? Color.FromArgb(239, 154, 154) : Color.FromArgb(198, 40, 40)),
                BackColor = Color.Transparent
            });
            panel.Controls.Add(new Label
            {
                Text = $"伤害 {p.stats?.totalDamageDealtToChampions ?? 0:N0} · 补刀 {(p.stats?.totalMinionsKilled ?? 0) + (p.stats?.neutralMinionsKilled ?? 0)}",
                Location = new Point(70, 54),
                Size = new Size(250, 18),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = palette.TextSecondary,
                BackColor = Color.Transparent
            });
            AddSummonerSpellIcons(panel, p, 300, 30);
            AddItemIcons(panel, p, 70, 78);

            if (_gameInfo.IsAugmentAram() && p.stats?.AugmentIds.Count > 0)
            {
                _ = LoadAugmentTagsAsync(panel, p.stats.AugmentIds);
            }

            if (!string.IsNullOrWhiteSpace(playerPuuid))
            {
                var premadeTag = new Label
                {
                    AutoSize = false,
                    BackColor = Color.FromArgb(255, 243, 224),
                    ForeColor = Color.FromArgb(191, 104, 0),
                    Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                    Location = new Point(365, 30),
                    Size = new Size(70, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Visible = false
                };
                panel.Controls.Add(premadeTag);
                _premadeTagsByPuuid[playerPuuid] = premadeTag;
            }
            return panel;
        }

        private void AddBanRow(FlowLayoutPanel target, IReadOnlyList<int> championIds)
        {
            if (championIds.Count == 0) return;
            var row = new FlowLayoutPanel
            {
                Size = new Size(455, 34),
                WrapContents = false,
                BackColor = UiTheme.Palette.SurfaceRaised,
                Margin = new Padding(0, 3, 0, 3)
            };
            row.Controls.Add(new AntdUI.Label { Text = "禁用", Width = 42, Height = 27, ForeColor = UiTheme.Palette.TextSecondary });
            foreach (int championId in championIds)
            {
                var tag = new AntdUI.Tag
                {
                    Text = GetChampionDisplayName(championId),
                    Width = 76,
                    Height = 27,
                    AutoEllipsis = true,
                    Margin = new Padding(0, 0, 4, 0)
                };
                row.Controls.Add(tag);
                _ = LoadChampionTagIconAsync(tag, championId);
            }
            target.Controls.Add(row);
        }

        private static async Task LoadAugmentTagsAsync(Panel host, IReadOnlyList<int> ids)
        {
            var tags = new List<AntdUI.Tag>();
            for (int index = 0; index < ids.Count; index++)
            {
                var placeholder = new AntdUI.Tag
                {
                    Text = $"强化 #{ids[index]}",
                    Width = 112,
                    Height = 23,
                    Location = new Point(70 + index % 3 * 118, 109 + index / 3 * 25),
                    AutoEllipsis = true,
                    ForeColor = UiTheme.Palette.TextPrimary,
                    BackColor = UiTheme.Palette.SurfaceMuted
                };
                host.Controls.Add(placeholder);
                tags.Add(placeholder);
            }
            IReadOnlyList<AugmentCatalog.AugmentDisplay> augments = await AugmentCatalog.ResolveAsync(ids);
            if (host.IsDisposed) return;
            for (int index = 0; index < augments.Count && index < tags.Count; index++)
            {
                AugmentCatalog.AugmentDisplay augment = augments[index];
                AntdUI.Tag tag = tags[index];
                tag.Text = augment.Name;
                if (augment.IconUrl != null) _ = LoadAugmentTagIconAsync(tag, augment.IconUrl);
            }
        }

        private static async Task LoadAugmentTagIconAsync(AntdUI.Tag tag, string url)
        {
            byte[]? bytes = await AugmentCatalog.GetIconAsync(url);
            if (bytes == null || tag.IsDisposed) return;
            try
            {
                using var stream = new MemoryStream(bytes);
                using var source = Image.FromStream(stream);
                var image = new Bitmap(source);
                if (tag.IsDisposed) { image.Dispose(); return; }
                tag.Image = image;
                tag.Disposed += (_, _) => image.Dispose();
            }
            catch { }
        }

        private async Task LoadChampionTagIconAsync(AntdUI.Tag tag, int championId)
        {
            try
            {
                Image? image = ToImage(await _gameAssetService.GetChampionIconAsync(championId));
                if (image == null) return;
                if (tag.IsDisposed) { image.Dispose(); return; }
                tag.Image = image;
                tag.Disposed += (_, _) => image.Dispose();
            }
            catch { /* 图标失败时保留英雄名称 */ }
        }

        private void AddSummonerSpellIcons(Panel panel, MatchParticipant participant, int x, int y)
        {
            int[] spells = { participant.Spell1Id, participant.Spell2Id };
            for (int index = 0; index < spells.Length; index++)
            {
                var icon = new PictureBox
                {
                    Location = new Point(x + index * 28, y),
                    Size = new Size(24, 24),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(238, 241, 245)
                };
                icon.Disposed += (_, _) => icon.Image?.Dispose();
                panel.Controls.Add(icon);
                int spellId = spells[index];
                _ = LoadSpellIconAsync(icon, spellId);
            }
        }

        private void AddItemIcons(Panel panel, MatchParticipant participant, int x, int y)
        {
            int[] items =
            {
                participant.stats?.item0 ?? 0, participant.stats?.item1 ?? 0, participant.stats?.item2 ?? 0,
                participant.stats?.item3 ?? 0, participant.stats?.item4 ?? 0, participant.stats?.item5 ?? 0,
                participant.stats?.item6 ?? 0
            };
            for (int index = 0; index < items.Length; index++)
            {
                var icon = new PictureBox
                {
                    Location = new Point(x + index * 29, y),
                    Size = new Size(25, 25),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(238, 241, 245)
                };
                icon.Disposed += (_, _) => icon.Image?.Dispose();
                panel.Controls.Add(icon);
                int itemId = items[index];
                _ = LoadItemIconAsync(icon, itemId);
            }
        }

        private async Task LoadSpellIconAsync(PictureBox box, int spellId)
        {
            if (spellId <= 0) return;
            Image? image = ToImage(await _gameAssetService.GetSummonerSpellIconAsync(spellId));
            if (image != null && !box.IsDisposed) box.Image = image;
            else image?.Dispose();
            string? name = await _gameAssetService.GetSummonerSpellNameAsync(spellId);
            if (!box.IsDisposed) _assetToolTip.SetToolTip(box, name ?? $"召唤师技能 {spellId}");
        }

        private async Task LoadItemIconAsync(PictureBox box, int itemId)
        {
            if (itemId <= 0)
            {
                _assetToolTip.SetToolTip(box, "空装备栏");
                return;
            }
            Image? image = ToImage(await _gameAssetService.GetItemIconAsync(itemId));
            if (image != null && !box.IsDisposed) box.Image = image;
            else image?.Dispose();
            string? name = await _gameAssetService.GetItemNameAsync(itemId);
            if (!box.IsDisposed) _assetToolTip.SetToolTip(box, name ?? $"装备 {itemId}");
        }

        private async Task DetectPremadesAsync(int myTeamId)
        {
            try
            {
                var teamOne = BuildTeamIdentities(myTeamId);
                var teamTwo = BuildTeamIdentities(_gameInfo.participants.FirstOrDefault(participant => participant.teamId != myTeamId)?.teamId ?? 0);
                if (teamOne.Count < 2 || teamTwo.Count < 2) return;
                var result = await _premadeDetectionService.DetectAsync(teamOne, teamTwo);
                if (IsDisposed) return;
                ApplyPremadeResult(result);
            }
            catch
            {
                if (!IsDisposed)
                {
                    lblAllyHeader.Text = "我方 · 组队情况未知";
                    lblEnemyHeader.Text = "敌方 · 组队情况未知";
                }
            }
        }

        /// <summary>
        /// 详情页的“上/中/下等马”依据玩家近期同模式已结束对局计算，而非这一局的偶然表现。
        /// 每名玩家先显示“评估中”，网络请求则在后台并发受限地完成，不阻塞详情窗口。
        /// </summary>
        private async Task ApplyRecentModePerformanceTagsAsync()
        {
            string[] playerPuuids = _performanceTagsByPuuid.Keys.ToArray();
            await Task.WhenAll(playerPuuids.Select(ApplyRecentModePerformanceTagAsync));
        }

        private async Task ApplyRecentModePerformanceTagAsync(string puuid)
        {
            try
            {
                MatchDetail[] details = await GetRecentModeDetailsAsync(puuid);
                var assessments = new List<MatchPerformanceAssessment>();
                var wins = new List<bool>();
                foreach (MatchDetail detail in details)
                {
                    MatchParticipant? participant = detail.GetParticipant(puuid);
                    if (participant?.stats == null) continue;
                    assessments.Add(EvaluatePerformance(detail, puuid));
                    wins.Add(participant.IsWin());
                }

                RecentModePerformanceAssessment assessment = RecentModePerformanceEvaluator.Evaluate(
                    _gameInfo.GetModeText(), assessments, wins);
                SetPerformanceTag(puuid, assessment);
            }
            catch
            {
                if (_performanceTagsByPuuid.TryGetValue(puuid, out Label? tag) && !tag.IsDisposed)
                {
                    tag.Text = "数据不足";
                    tag.BackColor = Color.FromArgb(245, 245, 245);
                    tag.ForeColor = SystemColors.GrayText;
                    _assetToolTip.SetToolTip(tag, "近期同模式战绩暂时无法读取，未作表现判定。");
                }
            }
        }

        private async Task<MatchDetail[]> GetRecentModeDetailsAsync(string puuid)
        {
            string key = $"{puuid}|{GetModeCacheKey(_gameInfo)}";
            if (RecentModeDetailCache.TryGetValue(key, out var cached) &&
                DateTime.UtcNow - cached.CachedAt < RecentModeDetailCacheTtl)
                return await cached.Details;

            Task<MatchDetail[]> task = LoadRecentModeDetailsAsync(puuid);
            RecentModeDetailCache[key] = (DateTime.UtcNow, task);
            try
            {
                return await task;
            }
            catch
            {
                RecentModeDetailCache.TryRemove(key, out _);
                throw;
            }
        }

        private async Task<MatchDetail[]> LoadRecentModeDetailsAsync(string puuid)
        {
            MatchHistoryResponse? history = await _matchHistoryService.GetPageAsync(puuid, 0, HistoryFetchCount - 1);
            var heads = history?.Games?.Games
                .Where(head => SameMode(head, _gameInfo))
                .OrderByDescending(head => head.GameCreation)
                .Take(RecentSampleSize)
                .ToList() ?? new List<MatchHistoryGame>();

            var tasks = heads.Select(async head =>
            {
                await RecentModeDetailLoadGate.WaitAsync();
                try { return await _matchHistoryService.GetDetailAsync(head.GameId); }
                finally { RecentModeDetailLoadGate.Release(); }
            });
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

        private static string GetModeCacheKey(MatchDetail detail)
        {
            string queue = detail.queueId ?? detail._queueId ?? "";
            return string.IsNullOrWhiteSpace(queue) ? detail.GetModeText() : queue;
        }

        private static MatchPerformanceAssessment EvaluatePerformance(MatchDetail game, string puuid)
        {
            var snapshots = game.participants
                .Where(participant => participant.stats != null)
                .Select(participant => new MatchPerformanceSnapshot(
                    game.participantIdentities.FirstOrDefault(identity => identity.participantId == participant.participantId)?.player?.puuid
                    ?? $"participant-{participant.participantId}",
                    participant.teamId,
                    participant.IsWin(),
                    participant.stats!.kills,
                    participant.stats.deaths,
                    participant.stats.assists,
                    participant.stats.totalDamageDealtToChampions,
                    participant.stats.goldEarned,
                    participant.stats.visionScore))
                .ToList();
            return MatchPerformanceEvaluator.Evaluate(snapshots.FirstOrDefault(snapshot => snapshot.PlayerId == puuid), snapshots);
        }

        private void SetPerformanceTag(string puuid, RecentModePerformanceAssessment assessment)
        {
            if (IsDisposed || !_performanceTagsByPuuid.TryGetValue(puuid, out Label? tag) || tag.IsDisposed) return;
            if (!assessment.HasEnoughSample)
            {
                tag.Text = "数据不足";
                tag.BackColor = Color.FromArgb(245, 245, 245);
                tag.ForeColor = SystemColors.GrayText;
                _assetToolTip.SetToolTip(tag, assessment.Detail);
                return;
            }
            tag.Text = RecentPerformanceLabelFormatter.GetText(assessment);
            (tag.BackColor, tag.ForeColor) = assessment.Label switch
            {
                RecentPerformanceLabel.Upper => (Color.FromArgb(232, 245, 233), Color.FromArgb(27, 94, 32)),
                RecentPerformanceLabel.Human => (Color.FromArgb(243, 229, 245), Color.FromArgb(123, 31, 162)),
                RecentPerformanceLabel.Lower => (Color.FromArgb(255, 235, 238), Color.FromArgb(183, 28, 28)),
                _ => (Color.FromArgb(245, 245, 245), Color.FromArgb(85, 85, 85))
            };
            _assetToolTip.SetToolTip(tag, $"同模式近期表现 · {assessment.Score} 分\n{assessment.Detail}");
        }

        private List<TeamMemberIdentity> BuildTeamIdentities(int teamId) => _gameInfo.participants
            .Where(participant => participant.teamId == teamId)
            .Select(participant => _gameInfo.participantIdentities.FirstOrDefault(identity => identity.participantId == participant.participantId)?.player)
            .Where(player => !string.IsNullOrWhiteSpace(player?.puuid))
            .Select(player => new TeamMemberIdentity(player!.puuid, player.gameName ?? player.summonerName ?? "玩家"))
            .ToList();

        private void ApplyPremadeResult(PremadeDetectionResult result)
        {
            lblAllyHeader.Text = $"我方 · {result.GetTeamQueueStatus(0)}";
            lblEnemyHeader.Text = $"敌方 · {result.GetTeamQueueStatus(1)}";
            foreach (var pair in _premadeTagsByPuuid)
            {
                PremadeGroup? group = result.GroupByPuuid.GetValueOrDefault(pair.Key);
                pair.Value.Visible = group != null;
                if (group == null) continue;
                pair.Value.Text = $"开黑 {group.Index}";
                _assetToolTip.SetToolTip(pair.Value, $"{group.Puuids.Count} 人组队：{string.Join("、", group.Names)}（近期多次同队推断）");
            }
        }

        /// <summary>
        /// 异步加载英雄头像（全局缓存）。
        /// </summary>
        private async Task LoadAvatarAsync(RoundPictureBox box, int championId)
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

        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        /// <summary>二进制资源在窗体边界解码，避免应用服务依赖 WinForms。</summary>
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

        private void btnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}