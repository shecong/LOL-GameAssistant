using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using System.Data;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>首页：展示当前召唤师概况和近期战绩。</summary>
    public partial class HomeForm : UserControl, IThemeAware
    {
        public PlayerProfile? userinfo;

        public PlayerProfile? userOhterinfo;

        private IInfoMsgForm _infoMsgForm;
        private readonly IPlayerProfileService _playerProfileService;
        private readonly IProfileIconService _profileIconService;
        private readonly ILeagueClientConnection _leagueClientConnection;
        private readonly IMatchHistoryService _matchHistoryService;
        private readonly IRankedStatsService _rankedStatsService;
        private readonly IGameDataVersionService _gameDataVersionService;
        private readonly Func<RecordForm> _recordFactory;

        private MatchHistoryResponse? matchlists;
        private Image? _ownedProfileImage;

        /// <summary>
        /// 首页战绩一次拉取的场数上限。
        /// 原先是一次请求要 9999 局，响应体改为整体缓冲后会造成明显的内存尖峰，
        /// 收敛为最近 200 局（仍是单次请求，本地分页够用）；更长的历史走"战绩查询"页。
        /// </summary>
        private const int RecentGamesLimit = 200;

        /// <summary>
        /// 防止客户端后启动时重复刷新首页。
        /// </summary>
        private readonly UiRequestScope _loads = new();
        private readonly UiRequestScope _pages = new();

        /// <summary>
        /// 1=当前玩家，2=指定玩家
        /// </summary>
        public int UserStatus = 1;

        /// <summary>初始化 HomeForm 的实例状态，并保存传入的依赖或数据。</summary>
        public HomeForm(IInfoMsgForm infoMsgForm) : this(
            infoMsgForm,
            AppCompositionRoot.PlayerProfileService,
            AppCompositionRoot.ProfileIconService,
            AppCompositionRoot.LeagueClientConnection,
            AppCompositionRoot.MatchHistoryService,
            AppCompositionRoot.RankedStatsService,
            AppCompositionRoot.GameDataVersionService)
        {
        }

        /// <summary>首页通过应用端口读取连接、资料与头像，不直接处理 LCU token 或 JSON。</summary>
        internal HomeForm(
            IInfoMsgForm infoMsgForm,
            IPlayerProfileService playerProfileService,
            IProfileIconService profileIconService,
            ILeagueClientConnection leagueClientConnection,
            IMatchHistoryService matchHistoryService,
            IRankedStatsService rankedStatsService,
            IGameDataVersionService gameDataVersionService,
            Func<RecordForm>? recordFactory = null)
        {
            InitializeComponent();
            WindowLayout.ConfigureDpi(this);
            _infoMsgForm = infoMsgForm;
            _playerProfileService = playerProfileService;
            _profileIconService = profileIconService;
            _leagueClientConnection = leagueClientConnection;
            _matchHistoryService = matchHistoryService;
            _rankedStatsService = rankedStatsService;
            _gameDataVersionService = gameDataVersionService;
            _recordFactory = recordFactory ?? (() => new RecordForm(matchHistoryService, AppCompositionRoot.GameAssetService));
            rootGrid.RowStyles[1].Height = 244;
            bottomStats.Height = 48;
            bottomStats.WrapContents = true;
            bottomStats.AutoSize = true;
            bottomStats.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            foreach (AntdUI.Label value in new[]
                { game_dws, game_jjs, game_jjscount, game_dqsd, game_ycf, game_sjend })
                value.AutoSize = true;
            rankedCard.Padding = new Padding(14, 36, 14, 8);
            SizeChanged += (_, _) => LayoutSummary();
            DpiChangedAfterParent += (_, _) => LayoutSummary();
            playerCard.SizeChanged += (_, _) => LayoutPlayerSummary();
            flowSearch.SizeChanged += (_, _) => LayoutSummary();
            VisibleChanged += (_, _) => { if (Visible) LayoutSummary(); };
            UiLanguage.Changed += LanguageChanged;
            Disposed += (_, _) => UiLanguage.Changed -= LanguageChanged;
            game_pagin.Click -= game_pagin_Click;
            InitializeDashboardLayout();
            LayoutSummary();
            stackPanel1.SizeChanged += (_, _) => LayoutRecordCards();
            ApplyTheme(UiTheme.Palette);
            Disposed += (_, _) => { _loads.Dispose(); _pages.Dispose(); _ownedProfileImage?.Dispose(); };
        }

        private void LanguageChanged(object? sender, EventArgs args) => LayoutSummary();

        private void LayoutSummary()
        {
            if (IsDisposed) return;
            int Scale(int value) => value * DeviceDpi / 96;
            rootGrid.ColumnStyles[0].Width = Math.Clamp(ClientSize.Width * .29F, Scale(260), Scale(410));
            rootGrid.RowStyles[0].Height = Scale(70);
            rootGrid.RowStyles[1].SizeType = SizeType.Percent;
            rootGrid.RowStyles[1].Height = 100;
            historyHeader.Height = Scale(50);
            flowSearch.WrapContents = false;
            int actionWidth = flowSearch.Controls.Cast<Control>().Where(c => c != inp_playname)
                .Sum(c => c.Width + c.Margin.Horizontal);
            inp_playname.Width = Math.Max(Scale(120), flowSearch.ClientSize.Width - actionWidth - inp_playname.Margin.Horizontal - flowSearch.Padding.Horizontal);
            historyHeader.WrapContents = false;
            game_pagin.Height = Scale(44);
            LayoutPlayerSummary();
        }

        private void LayoutPlayerSummary()
        {
            int Scale(int value) => value * DeviceDpi / 96;
            int left = play_HeadIcon.Right + Scale(12), right = playerCard.ClientSize.Width - Scale(14);
            play_name.AutoEllipsis = play_number.AutoEllipsis = true;
            play_name.Width = play_number.Width = Math.Max(1, right - left);
            play_name.Left = play_number.Left = lblLevelCap.Left = left;
            play_dj.Left = lblLevelCap.Right + Scale(4);
            lblXpCap.Location = new Point(Scale(14), Scale(140));
            play_next.Location = new Point(lblXpCap.Right + Scale(6), Scale(138));
            play_next.Width = Math.Max(1, right - play_next.Left);
            play_next.AutoEllipsis = true;
            play_jd.Bounds = new Rectangle(Scale(14), Scale(180), Math.Max(1, right - Scale(14)), Scale(30));
            foreach (var title in new[] { game_dspT, game_lhpT })
            {
                title.AutoEllipsis = true;
                title.Width = Math.Max(1, title.Parent!.ClientSize.Width - title.Left - Scale(10));
            }
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = rootGrid.BackColor = palette.Surface;
            Color cardStart = palette.IsDark ? palette.SurfaceRaised : Color.FromArgb(252, 253, 255);
            Color cardEnd = palette.IsDark ? palette.SurfaceRaised : Color.White;
            SetCardColors(searchCard,
                palette.IsDark ? palette.SurfaceRaised : Color.FromArgb(245, 250, 255), cardEnd, palette.Border);
            SetCardColors(playerCard, cardStart, cardEnd, palette.Border);
            SetCardColors(rankedCard, cardStart, cardEnd, palette.Border);
            SetCardColors(historyCard, cardStart, cardEnd, palette.Border);
            SetCardColors(soloPanel,
                palette.IsDark ? Color.FromArgb(13, 45, 77) : Color.White,
                palette.IsDark ? palette.SurfaceRaised : Color.FromArgb(245, 250, 255), palette.Border);
            SetCardColors(flexPanel,
                palette.IsDark ? Color.FromArgb(40, 29, 58) : Color.White,
                palette.IsDark ? palette.SurfaceRaised : Color.FromArgb(255, 247, 245), palette.Border);

            flowSearch.BackColor = cardStart;
            rankGrid.BackColor = cardStart;
            bottomStats.BackColor = cardStart;
            historyHeader.BackColor = cardStart;
            stackPanel1.BackColor = cardStart;
            game_pagin.BackColor = cardStart;
            play_jd.BackColor = cardStart;
            play_jd.ForeColor = palette.TextSecondary;

            // AntdUI.Label 默认的不透明底色会在渐变卡片上形成一条条矩形色块。
            foreach (AntdUI.Label label in new AntdUI.Label[]
                { lblPlayerCardTitle, lblLevelCap, lblXpCap, lblRankedCardTitle, lblQueueHint,
                  lblDwsCap, lblJjsCap, lblJjsCountCap, lblDqsdCap, lblYcfCap, lblSjendCap,
                  lblHistoryTitle, lblPageSizeCap, play_name, play_number, play_dj, play_next, play_QF,
                  game_dws, game_jjs, game_jjscount, game_dqsd, game_ycf, game_sjend,
                  game_dspT, game_dsp_sl, game_dsp_win, game_dsp_loss, game_dsp_lp, game_dsp_highest,
                  game_lhpT, game_lhp_sl, game_lhp_win, game_lhp_loss, game_lhp_lp, game_lhp_highest })
                label.BackColor = Color.Transparent;

            lblPlayerCardTitle.ForeColor = lblRankedCardTitle.ForeColor = lblHistoryTitle.ForeColor = palette.TextPrimary;
            play_name.ForeColor = palette.TextPrimary;
            play_number.ForeColor = palette.TextSecondary;
            lblLevelCap.ForeColor = lblXpCap.ForeColor = palette.TextSecondary;
            play_dj.ForeColor = palette.Accent;
            play_next.ForeColor = palette.IsDark ? Color.FromArgb(243, 200, 121) : Color.FromArgb(120, 80, 20);
            play_QF.ForeColor = palette.TextSecondary;

            game_dspT.ForeColor = palette.Accent;
            game_lhpT.ForeColor = palette.IsDark ? Color.FromArgb(245, 158, 162) : Color.FromArgb(211, 47, 47);
            game_dsp_sl.ForeColor = game_lhp_sl.ForeColor = palette.TextSecondary;
            game_dsp_win.ForeColor = game_lhp_win.ForeColor = palette.IsDark
                ? Color.FromArgb(145, 220, 164) : Color.FromArgb(46, 125, 50);
            game_dsp_loss.ForeColor = game_lhp_loss.ForeColor = palette.IsDark
                ? Color.FromArgb(245, 158, 162) : Color.FromArgb(198, 40, 40);
            game_dsp_lp.ForeColor = game_lhp_lp.ForeColor = palette.IsDark
                ? Color.FromArgb(243, 200, 121) : Color.FromArgb(185, 104, 0);
            game_dsp_highest.ForeColor = game_lhp_highest.ForeColor = palette.TextSecondary;

            lblQueueHint.ForeColor = palette.Accent;
            foreach (Control caption in new Control[]
                { lblDwsCap, lblJjsCap, lblJjsCountCap, lblDqsdCap, lblYcfCap, lblSjendCap, lblPageSizeCap })
                caption.ForeColor = palette.TextSecondary;
            foreach (Control value in new Control[]
                { game_dws, game_jjs, game_jjscount, game_dqsd, game_ycf, game_sjend })
                value.ForeColor = palette.TextPrimary;
            inp_playname.BackColor = palette.IsDark ? palette.SurfaceMuted : Color.White;
            inp_playname.ForeColor = palette.TextPrimary;
            inp_playname.PlaceholderColor = palette.TextSecondary;
            game_count.BackColor = palette.IsDark ? palette.SurfaceMuted : Color.White;
            game_count.ForeColor = palette.TextPrimary;
            game_pagin.ForeColor = palette.TextPrimary;
            Invalidate(true);
        }

        /// <summary>同步卡片背景、文字和边框的主题颜色。</summary>
        private static void SetCardColors(GradientPanel card, Color start, Color end, Color border)
        {
            card.BackColor = start;
            card.StartColor = start;
            card.EndColor = end;
            card.BorderColor = border;
            card.Invalidate();
        }

        /// <summary>
        /// 初始化加载数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void HomeForm_Load(object sender, EventArgs e)
        {
            await LoadGame();
        }

        /// <summary>
        /// 初始化游戏
        /// </summary>
        /// <returns></returns>
        private Task LoadGame() => LoadPlayerAsync(null);
        public Task RefreshAsync() => LoadPlayerAsync(UserStatus == 2 ? userOhterinfo?.Puuid : null);
        private Task LoadGame(string puuid) => LoadPlayerAsync(puuid);

        internal async Task LoadPlayerAsync(string? puuid, string? search = null)
        {
            if (IsDisposed) return;
            using var request = _loads.Begin();
            _pages.Cancel();
            var token = request.Token;
            if (IsHandleCreated) Search_Player.Loading = refeash.Loading = true;
            try
            {
                bool connected = await Task.Run(() => _leagueClientConnection.TryConnect(), token);
                if (!request.IsCurrent) return;
                if (!connected)
                {
                    play_name.Text = "等待 LOL 客户端";
                    play_number.Text = "启动客户端并登录后自动连接";
                    return;
                }
                if (search != null)
                {
                    if (search.Contains('#'))
                    {
                        var parts = search.Split('#', 2);
                        var found = await _playerProfileService.FindByRiotIdAsync(parts[0].Trim(), parts[1].Trim(), token);
                        if (!request.IsCurrent) return;
                        puuid = found?.Puuid ?? GetUserPuuid(search);
                    }
                    else if (search.Length >= 30) puuid = search;
                    else
                    {
                        var found = await _playerProfileService.FindByNameAsync(search, token);
                        if (!request.IsCurrent) return;
                        puuid = found?.Puuid ?? GetUserPuuid(search);
                    }
                    if (string.IsNullOrWhiteSpace(puuid)) { _infoMsgForm.AddMsg("未找到该玩家，请输入完整的名称#TAG 或 PUUID"); return; }
                }
                await _gameDataVersionService.EnsureCurrentVersionAsync(token);
                var player = puuid == null ? await _playerProfileService.GetCurrentAsync(token)
                    : await _playerProfileService.GetByPuuidAsync(puuid, token);
                if (!request.IsCurrent) return;
                if (player == null) { _infoMsgForm.AddMsg("未获取到玩家资料，请检查名称#TAG 和客户端连接"); return; }
                byte[]? icon = await _profileIconService.GetProfileIconAsync(player.ProfileIconId, token);
                if (!request.IsCurrent) return;
                var ranked = await _rankedStatsService.GetAsync(player.Puuid, isCurrentUser: puuid == null, cancellationToken: token);
                if (!request.IsCurrent) return;
                var history = await _matchHistoryService.GetPageAsync(player.Puuid, 0, RecentGamesLimit - 1, token);
                if (!request.IsCurrent) return;
                UserStatus = puuid == null ? 1 : 2;
                if (UserStatus == 1) userinfo = player; else userOhterinfo = player;
                if (icon is { Length: > 0 }) ReplaceProfileIcon(icon);
                play_name.Text = player.GameName;
                play_number.Text = $"#{player.TagLine}";
                inp_playname.Text = player.RiotId;
                play_QF.Text = "";
                play_dj.Text = player.SummonerLevel.ToString();
                play_next.Text = player.XpUntilNextLevel.ToString();
                play_jd.Value = CalculateLevelProgress(player);
                ApplyRankedStats(ranked);
                matchlists = history;
                UpdateModeOptions();
                InitPagin(history);
                await GetGameInfo(player, 1);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (request.IsCurrent) _infoMsgForm.AddMsg($"首页加载失败：{ex.Message}");
                RuntimeDiagnostics.WriteException(ex);
            }
            finally
            {
                if (request.IsCurrent && !IsDisposed && IsHandleCreated) Search_Player.Loading = refeash.Loading = false;
            }
        }

        /// <summary>
        /// 获取玩家的比赛记录
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task GetGameInfo(PlayerProfile userinfo, int pageindex)
        {
            if (IsDisposed) return;
            using var request = _pages.Begin();
            CancellationToken token = request.Token;
            foreach (Control control in stackPanel1.Controls.Cast<Control>().ToArray())
            {
                stackPanel1.Controls.Remove(control);
                control.Dispose();
            }
            if (userinfo == null || matchlists?.Games?.Games == null) return;
            if (!int.TryParse(this.game_count.Text, out int pageSize) || pageSize <= 0) pageSize = 10;

            // 使用已缓存的 matchlists 数据进行本地分页，避免重复调用 API
            var sortedList = matchlists.Games.Games
                .Where(MatchesSelectedMode)
                .OrderByDescending(p => p.GameCreation)
                .ToList();

            int total = sortedList.Count;
            int skip = (pageindex - 1) * pageSize;
            if (skip >= total) return;
            var pageList = sortedList.Skip(skip).Take(pageSize).ToList();

            // 并行加载本页战绩（限制并发，避免瞬时大量请求），完成后按原顺序显示
            using var semaphore = new SemaphoreSlim(4, 4);
            var tasks = pageList.Select(async head =>
            {
                RecordForm? record = null;
                bool acquired = false;
                try
                {
                    await semaphore.WaitAsync(token);
                    acquired = true;
                }
                catch (OperationCanceledException) { return null; }
                try
                {
                    if (!request.IsCurrent) return null;
                    record = _recordFactory();
                    record.Width = GetRecordCardWidth();
                    record.Margin = new Padding(0, 0, 12, 16);
                    await record.setInfo(head, userinfo.Puuid, cancellationToken: token);
                    return record;
                }
                catch
                {
                    record?.Dispose();
                    return null;
                }
                finally
                {
                    if (acquired) semaphore.Release();
                }
            }).ToList();

            var records = await Task.WhenAll(tasks);
            if (!request.IsCurrent)
            {
                foreach (var record in records) record?.Dispose();
                return;
            }
            foreach (var record in records)
            {
                if (record != null) this.stackPanel1.Controls.Add(record);
            }
            LayoutRecordCards();
        }

        /// <summary>计算首页战绩卡片的可用宽度。</summary>
        private int GetRecordCardWidth()
        {
            const int gap = 12;
            int available = Math.Max(280, stackPanel1.ClientSize.Width - stackPanel1.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
            return available >= 1240
                ? Math.Min(700, (available - gap * 2) / 2)
                : Math.Max(280, available - gap);
        }

        /// <summary>按首页容器尺寸重新排列战绩卡片。</summary>
        private void LayoutRecordCards()
        {
            if (stackPanel1.IsDisposed) return;
            int width = GetRecordCardWidth();
            stackPanel1.SuspendLayout();
            try
            {
                foreach (RecordForm record in stackPanel1.Controls.OfType<RecordForm>())
                    if (record.Width != width) record.Width = width;
            }
            finally { stackPanel1.ResumeLayout(); }
        }

        /// <summary>
        /// 获取玩家赛季信息
        /// </summary>
        /// <param name="plyaer"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void ApplyRankedStats(RankedOverview? gameinfo)
        {
            gameinfo ??= new RankedOverview();
            //获取单双排信息
            RankedQueue? solo = gameinfo.GetQueue(RankedQueues.Solo5x5);
            //获取灵活5v5信息
            RankedQueue? flex = gameinfo.GetQueue(RankedQueues.Flex5x5);

            //===== 单双排（排位赛）卡片 =====
            if (solo != null)
            {
                this.pic_dsp.Image = CheckTierImg(solo.Tier);
                this.game_dspT.Text = HasRank(solo)
                    ? $"单双排 {CheckTierName(solo.Tier)}{solo.Division}"
                    : "单双排 未排位";
                this.game_dsp_lp.Text = $"{solo.LeaguePoints} LP";
                this.game_dsp_sl.Text = $"胜率 {solo.WinRate}%";
                this.game_dsp_win.Text = $"{solo.Wins} 胜";
                this.game_dsp_loss.Text = $"{solo.Losses} 负";
                this.game_dsp_highest.Text =
                    string.IsNullOrEmpty(solo.HighestTier) || solo.HighestTier == "NONE"
                        ? "最高 -"
                        : $"最高 {CheckTierName(solo.HighestTier)}{solo.HighestDivision}";
            }
            else
            {
                this.pic_dsp.Image = CheckTierImg("");
                this.game_dspT.Text = "单双排 未排位";
                this.game_dsp_lp.Text = "-";
                this.game_dsp_sl.Text = "胜率 -";
                this.game_dsp_win.Text = "胜场 -";
                this.game_dsp_loss.Text = "负场 -";
                this.game_dsp_highest.Text = "最高 -";
            }

            //===== 灵活组排卡片 =====
            if (flex != null)
            {
                this.pic_lhp.Image = CheckTierImg(flex.Tier);
                this.game_lhpT.Text = HasRank(flex)
                    ? $"灵活组排 {CheckTierName(flex.Tier)}{flex.Division}"
                    : "灵活组排 未排位";
                this.game_lhp_lp.Text = $"{flex.LeaguePoints} LP";
                this.game_lhp_sl.Text = $"胜率 {flex.WinRate}%";
                this.game_lhp_win.Text = $"{flex.Wins} 胜";
                this.game_lhp_loss.Text = $"{flex.Losses} 负";
                this.game_lhp_highest.Text =
                    string.IsNullOrEmpty(flex.HighestTier) || flex.HighestTier == "NONE"
                        ? "最高 -"
                        : $"最高 {CheckTierName(flex.HighestTier)}{flex.HighestDivision}";
            }
            else
            {
                this.pic_lhp.Image = CheckTierImg("");
                this.game_lhpT.Text = "灵活组排 未排位";
                this.game_lhp_lp.Text = "-";
                this.game_lhp_sl.Text = "胜率 -";
                this.game_lhp_win.Text = "胜场 -";
                this.game_lhp_loss.Text = "负场 -";
                this.game_lhp_highest.Text = "最高 -";
            }

            //===== 底部六项数据（以单双排为主，与左侧卡片对应） =====
            this.game_dws.Text = solo?.GetPlacementText() ?? "-";
            this.game_jjs.Text = solo?.GetPromotionText() ?? "-";
            this.game_jjscount.Text = solo == null ? "-" : $"{solo.TotalGames} 场";
            this.game_dqsd.Text = solo == null ? "-" : $"{solo.LeaguePoints} LP";
            this.game_sjend.Text = gameinfo.GetSeasonEndText(RankedQueues.Solo5x5);
            string ratedTierName = solo == null ? "" : RankedDisplayRules.GetRatedTierName(solo.RatedTier);
            this.game_ycf.Text = solo != null && solo.RatedRating > 0
                ? $"{solo.RatedRating}" + (string.IsNullOrEmpty(ratedTierName) ? "" : $"（{ratedTierName}）")
                : "-";
        }

        /// <summary>
        /// 将基础设施层返回的头像字节复制成独立图像，避免流释放后 WinForms 绘制失败。
        /// </summary>
        private void ReplaceProfileIcon(byte[] imageBytes)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                using var sourceImage = Image.FromStream(stream);
                var image = new Bitmap(sourceImage);
                play_HeadIcon.Image = image;
                _ownedProfileImage?.Dispose();
                _ownedProfileImage = image;
            }
            catch (ArgumentException)
            {
                // LCU 偶发返回非图像响应时保留当前头像，避免中断首页刷新。
            }
        }

        /// <summary>
        /// 根据玩家经验计算进度条比例，并处理满级或缺少经验数据的边界情况。
        /// </summary>
        private static float CalculateLevelProgress(PlayerProfile player)
        {
            int totalExperience = player.XpSinceLastLevel + player.XpUntilNextLevel;
            if (totalExperience <= 0) return 0F;

            return Math.Clamp((float)player.XpSinceLastLevel / totalExperience, 0F, 1F);
        }

        /// <summary>
        /// 判断是否已获得有效段位。
        /// </summary>
        private static bool HasRank(RankedQueue? entry) => RankedDisplayRules.HasRank(entry);

        /// <summary>
        /// 分页加载方法
        /// </summary>
        /// <param name="matchlists"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void InitPagin(MatchHistoryResponse? matchlists)
        {
            _updatingPagination = true;
            try
            {
                this.game_pagin.Total = matchlists?.Games?.Games.Count(MatchesSelectedMode) ?? 0;
                this.game_pagin.Current = 1;
                this.game_pagin.PageSize = Math.Max(1, (int)game_count.Value);
            }
            finally { _updatingPagination = false; }
        }

        /// <summary>
        /// 根据段位返回对应图片
        /// </summary>
        /// <param name="tier"></param>
        /// <returns></returns>
        private Image CheckTierImg(string tier)
        {
            switch (tier)
            {
                case "IRON":
                    return Properties.Resources._01;

                case "BRONZE":
                    return Properties.Resources._02;

                case "SILVER":
                    return Properties.Resources._03;

                case "GOLD":
                    return Properties.Resources._04;

                case "PLATINUM":
                    return Properties.Resources._05;

                case "DIAMOND":
                    return Properties.Resources._06;

                case "MASTER":
                    return Properties.Resources._07;

                case "GRANDMASTER":
                    return Properties.Resources._08;

                case "CHALLENGER":
                    return Properties.Resources._09;

                default:
                    return Properties.Resources.下载;
            }
        }

        /// <summary>
        /// 根据段位返回对应文字
        /// </summary>
        /// <param name="tier"></param>
        /// <returns></returns>
        private String CheckTierName(string tier)
        {
            switch (tier)
            {
                case "IRON":
                    return "黑铁";

                case "BRONZE":
                    return "青铜";

                case "SILVER":
                    return "白银";

                case "GOLD":
                    return "黄金";

                case "PLATINUM":
                    return "铂金";

                case "DIAMOND":
                    return "钻石";

                case "MASTER":
                    return "超凡大师";

                case "GRANDMASTER":
                    return "宗师";

                case "CHALLENGER":
                    return "最强王者";

                default:
                    return "无段位";
            }
        }

        /// <summary>
        /// 重置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btn_back_Click(object sender, EventArgs e)
        {
            await LoadGame();
        }

        /// <summary>
        /// 刷新
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void refeash_Click(object sender, EventArgs e)
        {
            await RefreshAsync();
        }

        /// <summary>
        /// 搜索指定玩家
        /// </summary>
        /// <param name="sender"></param>
        private async void PlayInfo_Click(object sender, EventArgs e)
        {
            string input = inp_playname.Text.Trim();
            if (input.Length > 0) await LoadPlayerAsync(null, input);
        }

        /// <summary>
        /// 循环历史游戏数据找到匹配的puuid
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        private string GetUserPuuid(string playername)
        {
            if (matchlists?.Games?.Games == null) return "";
            foreach (var game in matchlists.Games.Games)
            {
                if (game?.ParticipantIdentities == null) continue;
                foreach (var identity in game.ParticipantIdentities)
                {
                    var player = identity?.Player;
                    if (player == null) continue;
                    string fullName = $"{player.GameName}#{player.TagLine}";
                    if (!string.IsNullOrWhiteSpace(playername) &&
                        fullName.Contains(playername, StringComparison.OrdinalIgnoreCase))
                    {
                        return player.Puuid ?? "";
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// 分页点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void game_pagin_ValueChanged(object sender, AntdUI.PagePageEventArgs e)
        {
            if (_updatingPagination) return;
            _ = UpdateGame_paginAsync();
        }

        /// <summary>响应战绩分页操作并加载对应页面。</summary>
        private void game_pagin_Click(object? sender, EventArgs e)
        {
            _ = UpdateGame_paginAsync();
        }

        /// <summary>刷新首页战绩分页数据及卡片内容。</summary>
        private async Task UpdateGame_paginAsync()
        {
            if (UserStatus == 1 && userinfo != null)
                await GetGameInfo(userinfo, this.game_pagin.Current);
            else if (UserStatus == 2 && userOhterinfo != null)
                await GetGameInfo(userOhterinfo, this.game_pagin.Current);
        }
    }
}
