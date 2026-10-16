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

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 对局详情弹窗：完整展示本局 10 名玩家（我方/敌方、头像、英雄、KDA、伤害），
    /// 底部展示当前玩家详细数据。
    /// </summary>
    public partial class MatchDetailForm : AntdUI.Window, IThemeAware
    {
        private readonly UiRequestScope _lifetime = new();
        private readonly MatchDetail _gameInfo;
        private readonly string _puuid;
        private readonly IGameAssetService _gameAssetService;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IPremadeDetectionService _premadeDetectionService;
        private readonly Dictionary<string, AntdUI.Label> _premadeTagsByPuuid = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PerformanceTagLabel> _performanceTagsByPuuid = new(StringComparer.Ordinal);
        private readonly ToolTip _assetToolTip = new();
        private readonly AssetDetailToolTip _detailToolTip = new();
        /// <summary>点击玩家头像后选中的玩家 puuid（用于跳转战绩查询）。</summary>
        public string? SelectedPlayerPuuid { get; private set; }

        /// <summary>初始化 MatchDetailForm 的实例状态，并保存传入的依赖或数据。</summary>
        public MatchDetailForm(MatchDetail? gameInfo, string? puuid)
            : this(gameInfo, puuid, AppCompositionRoot.GameAssetService)
        {
        }

        /// <summary>对局详情只经由资源应用服务读取装备名与英雄头像。</summary>
        internal MatchDetailForm(MatchDetail? gameInfo, string? puuid, IGameAssetService gameAssetService,
            IMatchHistoryService? matchHistoryService = null, IPremadeDetectionService? premadeDetectionService = null)
        {
            if (gameInfo == null) throw new ArgumentNullException(nameof(gameInfo));
            if (string.IsNullOrEmpty(puuid)) throw new ArgumentNullException(nameof(puuid));
            _gameInfo = gameInfo;
            _puuid = puuid;
            _gameAssetService = gameAssetService;
            _matchHistoryService = matchHistoryService ?? AppCompositionRoot.MatchHistoryService;
            _premadeDetectionService = premadeDetectionService ?? AppCompositionRoot.PremadeDetectionService;
            InitializeComponent();
            WindowLayout.ConfigureDpi(this);
            AntdWindowChrome.Configure(this);
            Text = $"对局详情 · {_gameInfo.GetModeText()}";
            lblTitle.Text = Text;
            var detailIcon = (Icon)AppIcon.Shared.Clone();
            Icon = detailIcon;
            Disposed += (_, _) => detailIcon.Dispose();
            UiTheme.Apply(this);
            this.Load += async (_, _) => await LoadDataAsync();
            Disposed += (_, _) => { _lifetime.Dispose(); _assetToolTip.Dispose(); };
            Disposed += (_, _) => _detailToolTip.Dispose();
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Surface;
            flowAlly.BackColor = palette.Surface;
            flowEnemy.BackColor = palette.Surface;
            lblAllyHeader.ForeColor = palette.TextPrimary;
            lblEnemyHeader.ForeColor = palette.TextPrimary;
            lblTitle.BackColor = palette.Surface;
            lblTitle.UseSystemStyleColor = false;
            lblTitle.UseForeColorDrawIcons = true;
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

        /// <summary>异步读取详情展示所需的数据并更新界面。</summary>
        private static async Task ObserveAsync(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { RuntimeDiagnostics.WriteException(ex); }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var gamer = _gameInfo.GetParticipant(_puuid);
                if (gamer == null) return;

                bool isWin = gamer.IsWin();
                Text = $"对局详情 · {_gameInfo.GetModeText()} · {(isWin ? "胜利" : "失败")} · {_gameInfo.GetDurationText()}";
                lblTitle.Text = Text;
                lblTitle.ForeColor = isWin
                    ? (UiTheme.Palette.IsDark ? Color.FromArgb(129, 199, 132) : Color.FromArgb(46, 125, 50))
                    : (UiTheme.Palette.IsDark ? Color.FromArgb(239, 154, 154) : Color.FromArgb(198, 40, 40));

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
                var nameTasks = items.Where(i => i > 0).Select(itemId => _gameAssetService.GetItemNameAsync(itemId, _lifetime.Token)).ToList();
                var resolvedNames = nameTasks.Count > 0 ? await Task.WhenAll(nameTasks) : Array.Empty<string?>();
                if (IsDisposed || _lifetime.Token.IsCancellationRequested) return;
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
                if (IsDisposed || _lifetime.Token.IsCancellationRequested) return;
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

            _ = ObserveAsync(DetectPremadesAsync(myTeamId));
            _ = ObserveAsync(ApplyRecentModePerformanceTagsAsync());
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
            var panel = new MatchPlayerCard(isMe)
            {
                Size = new Size(455, (_gameInfo.IsAugmentAram() ? 166 : 118) + (p.stats?.RuneIds.Count > 0 ? 30 : 0)),
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
            _ = ObserveAsync(LoadAvatarAsync(avatar, p.championId));
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

            var nameLabel = new AntdUI.Label
            {
                Text = displayName,
                Location = new Point(70, 8),
                Size = new Size(190, 22),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = palette.TextPrimary
            };
            panel.Controls.Add(nameLabel);
            panel.TrackLabel(nameLabel, primary: true);
            var championLabel = new AntdUI.Label
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
            panel.TrackLabel(championLabel, primary: false);
            if (!string.IsNullOrWhiteSpace(playerPuuid))
            {
                var performanceTag = new PerformanceTagLabel
                {
                    AutoSize = false,
                    Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                    Location = new Point(270, 8),
                    Size = new Size(82, 22),
                    Text = "评估中",
                    TextAlign = ContentAlignment.MiddleCenter
                };
                panel.Controls.Add(performanceTag);
                _performanceTagsByPuuid[playerPuuid] = performanceTag;
            }
            panel.Controls.Add(new AntdUI.Label
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
            var damageLabel = new AntdUI.Label
            {
                Text = $"伤害 {p.stats?.totalDamageDealtToChampions ?? 0:N0} · 补刀 {(p.stats?.totalMinionsKilled ?? 0) + (p.stats?.neutralMinionsKilled ?? 0)}",
                Location = new Point(70, 54),
                Size = new Size(250, 18),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = palette.TextSecondary,
                BackColor = Color.Transparent
            };
            panel.Controls.Add(damageLabel);
            panel.TrackLabel(damageLabel, primary: false);
            AddSummonerSpellIcons(panel, p, 300, 30);
            AddItemIcons(panel, p, 70, 78);
            AddRuneIcons(panel, p);

            if (_gameInfo.IsAugmentAram() && p.stats?.AugmentIds.Count > 0)
            {
                _ = ObserveAsync(LoadAugmentTagsAsync(panel, p.stats.AugmentIds));
            }

            if (!string.IsNullOrWhiteSpace(playerPuuid))
            {
                var premadeTag = new AntdUI.Label
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
            panel.Scale(new SizeF(DeviceDpi / 96f, DeviceDpi / 96f));
            return panel;
        }

        /// <summary>在队伍区域展示本局禁用英雄。</summary>
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
                _ = ObserveAsync(LoadChampionTagIconAsync(tag, championId));
            }
            target.Controls.Add(row);
        }

        /// <summary>解析强化名称、作用和图标并更新标签。</summary>
        private async Task LoadAugmentTagsAsync(AntdUI.Panel host, IReadOnlyList<int> ids)
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
                _detailToolTip.SetDetails(placeholder, placeholder.Text, "正在加载作用说明…");
                tags.Add(placeholder);
            }
            IReadOnlyList<AugmentCatalog.AugmentDisplay> augments = await AugmentCatalog.ResolveAsync(ids);
            if (host.IsDisposed) return;
            for (int index = 0; index < augments.Count && index < tags.Count; index++)
            {
                AugmentCatalog.AugmentDisplay augment = augments[index];
                AntdUI.Tag tag = tags[index];
                tag.Text = UiLanguage.IsEnglish && !string.IsNullOrWhiteSpace(augment.EnglishName)
                    ? augment.EnglishName : augment.Name;
                _detailToolTip.SetDetails(tag, tag.Text, AugmentCatalog.GetDescription(augment.Id));
                if (augment.IconUrl != null) _ = ObserveAsync(LoadAugmentTagIconAsync(tag, augment.IconUrl));
            }
        }

        /// <summary>异步加载强化标签图标，并处理控件已释放的情况。</summary>
        private async Task LoadAugmentTagIconAsync(AntdUI.Tag tag, string url)
        {
            byte[]? bytes = await AugmentCatalog.GetIconAsync(url, _lifetime.Token);
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

        /// <summary>异步加载禁用英雄标签图标。</summary>
        private async Task LoadChampionTagIconAsync(AntdUI.Tag tag, int championId)
        {
            try
            {
                Image? image = ToImage(await _gameAssetService.GetChampionIconAsync(championId, _lifetime.Token));
                if (image == null) return;
                if (tag.IsDisposed) { image.Dispose(); return; }
                tag.Image = image;
                tag.Disposed += (_, _) => image.Dispose();
            }
            catch { /* 图标失败时保留英雄名称 */ }
        }

        /// <summary>为玩家卡片创建召唤师技能图标。</summary>
        private void AddSummonerSpellIcons(AntdUI.Panel panel, MatchParticipant participant, int x, int y)
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
                _ = ObserveAsync(LoadSpellIconAsync(icon, spellId));
            }
        }

        /// <summary>按装备栏顺序创建玩家装备图标。</summary>
        private void AddItemIcons(AntdUI.Panel panel, MatchParticipant participant, int x, int y)
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
                _ = ObserveAsync(LoadItemIconAsync(icon, itemId));
            }
        }

        /// <summary>读取召唤师技能图标及名称提示。</summary>
        private async Task LoadSpellIconAsync(PictureBox box, int spellId)
        {
            if (spellId <= 0) return;
            Image? image = ToImage(await _gameAssetService.GetSummonerSpellIconAsync(spellId, _lifetime.Token));
            if (image != null && !box.IsDisposed) box.Image = image;
            else image?.Dispose();
            string? name = await _gameAssetService.GetSummonerSpellNameAsync(spellId, _lifetime.Token);
            if (!box.IsDisposed) _assetToolTip.SetToolTip(box, name ?? $"召唤师技能 {spellId}");
        }

        /// <summary>读取装备图标并启动详细说明加载。</summary>
        private async Task LoadItemIconAsync(PictureBox box, int itemId)
        {
            if (itemId <= 0)
            {
                _assetToolTip.SetToolTip(box, "空装备栏");
                return;
            }
            _detailToolTip.SetDetails(box, $"装备 {itemId}", "正在加载作用说明…");
            _ = ObserveAsync(LoadItemDetailsAsync(box, itemId));
            Image? image = ToImage(await _gameAssetService.GetItemIconAsync(itemId, _lifetime.Token));
            if (image != null && !box.IsDisposed) box.Image = image;
            else image?.Dispose();
        }

        /// <summary>并发读取装备名称与作用，用于悬停及点击详情。</summary>
        private async Task LoadItemDetailsAsync(PictureBox box, int itemId)
        {
            try
            {
                var nameTask = _gameAssetService.GetItemNameAsync(itemId, _lifetime.Token);
                var descriptionTask = _gameAssetService.GetItemDescriptionAsync(itemId, _lifetime.Token);
                await Task.WhenAll(nameTask, descriptionTask);
                if (!IsDisposed) _detailToolTip.SetDetails(box, await nameTask ?? $"装备 {itemId}", await descriptionTask);
            }
            catch { if (!IsDisposed) _detailToolTip.SetDetails(box, $"装备 {itemId}", null); }
        }

        /// <summary>按玩家所选符文顺序创建符文图标。</summary>
        private void AddRuneIcons(AntdUI.Panel panel, MatchParticipant participant)
        {
            if (participant.stats == null) return;
            for (int index = 0; index < participant.stats.RuneIds.Count; index++)
            {
                int id = participant.stats.RuneIds[index];
                var icon = new PictureBox
                {
                    Location = new Point(70 + index * 29, _gameInfo.IsAugmentAram() ? 160 : 109),
                    Size = new Size(25, 25), SizeMode = PictureBoxSizeMode.Zoom
                };
                panel.Controls.Add(icon);
                icon.Disposed += (_, _) => icon.Image?.Dispose();
                _detailToolTip.SetDetails(icon, $"符文 {id}", "正在加载作用说明…");
                _ = ObserveAsync(LoadRuneAsync(icon, id));
            }
        }

        /// <summary>读取符文图标和作用说明并绑定详情提示。</summary>
        private async Task LoadRuneAsync(PictureBox box, int id)
        {
            try
            {
                string? details = await _gameAssetService.GetRuneDetailsAsync(id, _lifetime.Token);
                if (IsDisposed || box.IsDisposed) return;
                string[] parts = (details ?? $"符文 {id}").Split('\n', 2);
                _detailToolTip.SetDetails(box, parts[0], parts.Length > 1 ? parts[1] : null);
                Image? image = ToImage(await _gameAssetService.GetRuneIconAsync(id, _lifetime.Token));
                if (!box.IsDisposed) box.Image = image;
                else image?.Dispose();
            }
            catch { if (!IsDisposed) _detailToolTip.SetDetails(box, $"符文 {id}", null); }
        }

        /// <summary>读取双方队伍关系并刷新疑似组队标签。</summary>
        private async Task DetectPremadesAsync(int myTeamId)
        {
            try
            {
                var teamOne = BuildTeamIdentities(myTeamId);
                var teamTwo = BuildTeamIdentities(_gameInfo.participants.FirstOrDefault(participant => participant.teamId != myTeamId)?.teamId ?? 0);
                if (teamOne.Count < 2 || teamTwo.Count < 2) return;
                var result = await _premadeDetectionService.DetectAsync(teamOne, teamTwo, _lifetime.Token);
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

        /// <summary>读取单名玩家的近期同模式表现并更新评估标签。</summary>
        private async Task ApplyRecentModePerformanceTagAsync(string puuid)
        {
            try
            {
                string queueText = string.IsNullOrWhiteSpace(_gameInfo.queueId)
                    ? _gameInfo._queueId : _gameInfo.queueId;
                int.TryParse(queueText, out int queueId);
                var service = new RecentModePerformanceService(_matchHistoryService);
                RecentModePerformanceAssessment assessment = await service.EvaluateAsync(
                    puuid, queueId, _gameInfo.gameMode, _gameInfo.GetModeText(), cancellationToken: _lifetime.Token);
                SetPerformanceTag(puuid, assessment);
            }
            catch
            {
                if (_performanceTagsByPuuid.TryGetValue(puuid, out PerformanceTagLabel? tag) && !tag.IsDisposed)
                {
                    tag.Text = "数据不足";
                    tag.SetAssessment(null);
                    _assetToolTip.SetToolTip(tag, "最近 30 天同模式战绩暂时无法读取，未作表现判定。");
                }
            }
        }

        /// <summary>将评分和样本状态转换为表现标签及提示内容。</summary>
        private void SetPerformanceTag(string puuid, RecentModePerformanceAssessment assessment)
        {
            if (IsDisposed || !_performanceTagsByPuuid.TryGetValue(puuid, out PerformanceTagLabel? tag) || tag.IsDisposed) return;
            if (!assessment.HasEnoughSample)
            {
                tag.Text = "数据不足";
                tag.SetAssessment(null);
                _assetToolTip.SetToolTip(tag, assessment.Detail);
                return;
            }
            tag.Text = RecentPerformanceLabelFormatter.GetText(assessment);
            tag.SetAssessment(assessment.Label);
            _assetToolTip.SetToolTip(tag, $"同模式近期表现 · {assessment.Score} 分\n{assessment.Detail}");
        }

        /// <summary>保留评分状态，在动态添加、异步评分和主题切换后使用一致的语义配色。</summary>
        private sealed class PerformanceTagLabel : AntdUI.Label, IThemeAware
        {
            private RecentPerformanceLabel? _assessment;

            public void SetAssessment(RecentPerformanceLabel? assessment)
            {
                _assessment = assessment;
                ApplyTheme(UiTheme.Palette);
            }

            public void ApplyTheme(ThemePalette palette)
            {
                (BackColor, ForeColor) = (_assessment, palette.IsDark) switch
                {
                    (RecentPerformanceLabel.Upper, true) => (Color.FromArgb(28, 62, 47), Color.FromArgb(144, 221, 169)),
                    (RecentPerformanceLabel.Upper, false) => (Color.FromArgb(232, 245, 233), Color.FromArgb(27, 94, 32)),
                    (RecentPerformanceLabel.Human, true) => (Color.FromArgb(59, 39, 75), Color.FromArgb(216, 174, 240)),
                    (RecentPerformanceLabel.Human, false) => (Color.FromArgb(243, 229, 245), Color.FromArgb(123, 31, 162)),
                    (RecentPerformanceLabel.Lower, true) => (Color.FromArgb(72, 35, 43), Color.FromArgb(255, 163, 174)),
                    (RecentPerformanceLabel.Lower, false) => (Color.FromArgb(255, 235, 238), Color.FromArgb(183, 28, 28)),
                    (RecentPerformanceLabel.Medium, true) => (Color.FromArgb(35, 53, 76), Color.FromArgb(175, 207, 244)),
                    (RecentPerformanceLabel.Medium, false) => (Color.FromArgb(233, 241, 252), Color.FromArgb(47, 82, 126)),
                    _ => (palette.SurfaceMuted, palette.TextSecondary)
                };
            }
        }

        /// <summary>收集指定队伍中可用于查询的玩家身份。</summary>
        private List<TeamMemberIdentity> BuildTeamIdentities(int teamId) => _gameInfo.participants
            .Where(participant => participant.teamId == teamId)
            .Select(participant => _gameInfo.participantIdentities.FirstOrDefault(identity => identity.participantId == participant.participantId)?.player)
            .Where(player => !string.IsNullOrWhiteSpace(player?.puuid))
            .Select(player => new TeamMemberIdentity(player!.puuid, player.gameName ?? player.summonerName ?? "玩家"))
            .ToList();

        /// <summary>将组队推断结果同步到队伍标题和玩家标签。</summary>
        private void ApplyPremadeResult(PremadeDetectionResult result)
        {
            lblAllyHeader.Text = $"我方 · {result.GetInferredTeamStatus(0)}";
            lblEnemyHeader.Text = $"敌方 · {result.GetInferredTeamStatus(1)}";
            foreach (var pair in _premadeTagsByPuuid)
            {
                PremadeGroup? group = result.GroupByPuuid.GetValueOrDefault(pair.Key);
                pair.Value.Visible = group != null;
                if (group == null) continue;
                pair.Value.Text = $"疑似{group.Index}";
                _assetToolTip.SetToolTip(pair.Value, $"{group.Puuids.Count} 人组队：{string.Join("、", group.Names)}（近期多次同队推断）");
            }
        }

        /// <summary>
        /// 异步加载英雄头像（全局缓存）。
        /// </summary>
        private async Task LoadAvatarAsync(RoundPictureBox box, int championId)
        {
            Image? icon = ToImage(await _gameAssetService.GetChampionIconAsync(championId, _lifetime.Token));
            if (icon != null && !box.IsDisposed)
            {
                box.Image = icon;
            }
            else
            {
                icon?.Dispose();
            }
        }

        /// <summary>根据英雄标识取得展示名称。</summary>
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

    }
}
