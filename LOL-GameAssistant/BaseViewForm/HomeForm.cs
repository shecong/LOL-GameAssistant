using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using System.Data;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;
using static LOL_GameAssistant.Entity.LolRankedDataParser;
using static LOL_GameAssistant.Entity.PlayerModel;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class HomeForm : UserControl
    {
        public Plyaer? userinfo = new Plyaer();

        public Plyaer? userOhterinfo = new Plyaer();

        private IInfoMsgForm _infoMsgForm;

        private GameHeadModel.MatchHistoryResponse? matchlists;

        /// <summary>
        /// 防止客户端后启动时重复刷新首页。
        /// </summary>
        private bool _refreshingFromConnection;

        /// <summary>
        /// 1=当前玩家，2=指定玩家
        /// </summary>
        public static int UserStatus = 1;

        public HomeForm(IInfoMsgForm infoMsgForm)
        {
            InitializeComponent();
            _infoMsgForm = infoMsgForm;
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
        private async Task LoadGame()
        {
            //获取客户端登陆
            GetlolLcu._infoMsgForm = _infoMsgForm;
            (string? port, string? token) = GetlolLcu.GetAuth();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(Program.GameMain, "未找到正在  运行的LOL客户端，请确保客户端已启动并登录。");
                _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                _infoMsgForm.AddMsg($"LCU 连接成功，端口: {port}");
            }
            //获取游戏版本号
            await Game_Api.GetGameversion();
            //获取当前召唤师信息
            userinfo = JsonConvert.DeserializeObject<Plyaer>(await Assets_api.GetUser());
            if (userinfo != null)
            {
                UserStatus = 1;
                //获取头像
                Stream headicon = await Assets_api.GetImg(userinfo.profileIconId);
                if (headicon != null)
                {
                    // 使用 Image.FromStream() 方法将 Stream 转换为 Image
                    Image profileImage = Image.FromStream(headicon);
                    this.play_HeadIcon.Image = profileImage;
                }
                this.play_name.Text = userinfo.gameName;
                this.play_number.Text = $"#{userinfo.tagLine}";
                this.inp_playname.Text = $"{userinfo.gameName}#{userinfo.tagLine}";
                this.play_QF.Text = "";
                this.play_dj.Text = userinfo.summonerLevel;
                this.play_next.Text = Convert.ToString(userinfo.xpUntilNextLevel);
                this.play_jd.Value = (float)userinfo.xpSinceLastLevel / (float)(userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel);

                //获取当前召唤师游戏赛季信息
                await GetGameSJAsync(userinfo);
                //获取玩家比赛记录
                await GetGameInfo(userinfo);
            }
        }

        /// <summary>
        /// 客户端后启动时由主窗体调用，刷新当前玩家数据（带防重入）。
        /// </summary>
        public async Task RefreshAsync()
        {
            if (_refreshingFromConnection) return;
            _refreshingFromConnection = true;
            try
            {
                await LoadGame();
            }
            catch
            {
                // 刷新失败不阻塞主流程
            }
            finally
            {
                _refreshingFromConnection = false;
            }
        }

        /// <summary>
        /// 获取指定玩家信息
        /// </summary>
        /// <returns></returns>
        private async Task LoadGame(string puuid)
        {
            //获取客户端登陆
            GetlolLcu._infoMsgForm = _infoMsgForm;
            (string? port, string? token) = GetlolLcu.GetAuth();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(Program.GameMain, "未找到正在  运行的LOL客户端，请确保客户端已启动并登录。");
                _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                _infoMsgForm.AddMsg($"LCU 连接成功，端口: {port}");
            }
            //获取游戏版本号
            await Game_Api.GetGameversion();
            //获取当前召唤师信息
            userOhterinfo = JsonConvert.DeserializeObject<Plyaer>(await Assets_api.GetUser(puuid));
            if (userOhterinfo != null)
            {
                UserStatus = 2;
                //获取头像
                Stream headicon = await Assets_api.GetImg(userOhterinfo.profileIconId);
                if (headicon != null)
                {
                    // 使用 Image.FromStream() 方法将 Stream 转换为 Image
                    Image profileImage = Image.FromStream(headicon);
                    this.play_HeadIcon.Image = profileImage;
                }
                this.play_name.Text = userOhterinfo.gameName;
                this.play_number.Text = $"#{userOhterinfo.tagLine}";
                this.play_QF.Text = "";
                this.play_dj.Text = userOhterinfo.summonerLevel;
                this.play_next.Text = Convert.ToString(userOhterinfo.xpUntilNextLevel);
                this.play_jd.Value = (float)userOhterinfo.xpSinceLastLevel / (float)(userOhterinfo.xpSinceLastLevel + userOhterinfo.xpUntilNextLevel);

                //获取当前召唤师游戏赛季信息
                await GetGameSJAsync(userOhterinfo);
                //获取玩家比赛记录
                await GetGameInfo(userOhterinfo);
            }
        }

        /// <summary>
        /// 获取玩家的比赛记录
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task GetGameInfo(Plyaer? userinfo)
        {
            if (userinfo == null) return;
            matchlists = await Game_Api.GetUserGame(userinfo.puuid, "0", "9999");

            //加载分页
            InitPagin(matchlists);
            await GetGameInfo(userinfo, 1);
        }

        /// <summary>
        /// 获取玩家的比赛记录（分页）
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        /// <summary>
        /// 获取玩家的比赛记录（分页）
        /// </summary>
        private async Task GetGameInfo(Plyaer userinfo, int pageindex)
        {
            stackPanel1.Controls.Clear();
            if (userinfo == null || matchlists?.Games?.Games == null) return;
            if (!int.TryParse(this.game_count.Text, out int pageSize) || pageSize <= 0) pageSize = 10;

            // 使用已缓存的 matchlists 数据进行本地分页，避免重复调用 API
            var sortedList = matchlists.Games.Games
                .OrderByDescending(p => p.GameCreation)
                .ToList();

            int total = sortedList.Count;
            int skip = (pageindex - 1) * pageSize;
            if (skip >= total) return;
            var pageList = sortedList.Skip(skip).Take(pageSize).ToList();

            // 保持近期战绩卡片的紧凑宽度，避免随首页宽度被无限拉伸；宽屏仍自动多列展示
            const int RecordCardWidth = 600;
            const int CardGap = 12;
            int clientWidth = Math.Max(RecordCardWidth, this.stackPanel1.ClientSize.Width - 20);
            int columns = Math.Max(1, (clientWidth + CardGap) / (RecordCardWidth + CardGap));
            int cardWidth = RecordCardWidth;

            // 并行加载本页战绩（限制并发，避免瞬时大量请求），完成后按原顺序显示
            var semaphore = new SemaphoreSlim(4, 4);
            var tasks = pageList.Select(async head =>
            {
                await semaphore.WaitAsync();
                try
                {
                    recordForm record = new recordForm
                    {
                        Width = cardWidth,
                        Margin = new Padding(0, 0, CardGap, 10)
                    };
                    await record.setInfo(head, userinfo.puuid);
                    return record;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var records = await Task.WhenAll(tasks);
            foreach (var record in records)
            {
                if (record != null) this.stackPanel1.Controls.Add(record);
            }
        }

        /// <summary>
        /// 获取玩家赛季信息
        /// </summary>
        /// <param name="plyaer"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task GetGameSJAsync(Plyaer? userinfo = null)
        {
            if (userinfo == null) return;
            LolRankedDataParser lolparser = new LolRankedDataParser();
            var gameinfo = await Game_Api.GetRankedStatsAsync(userinfo.puuid, isCurrentUser: UserStatus == 1);
            if (gameinfo == null) return;
            //获取单双排信息
            LolRankedDataParser.RankedEntry? solo = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_SOLO_5x5);
            //获取灵活5v5信息
            LolRankedDataParser.RankedEntry? flex = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_FLEX_SR);

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
            this.game_dws.Text = GetPlacementText(solo);
            this.game_jjs.Text = GetPromotionText(solo);
            this.game_jjscount.Text = solo == null ? "-" : $"{solo.TotalGames} 场";
            this.game_dqsd.Text = solo == null ? "-" : $"{solo.LeaguePoints} LP";
            this.game_sjend.Text = GetSeasonEndText(gameinfo, QueueTypes.RANKED_SOLO_5x5);
            string ratedTierName = solo == null ? "" : GetRatedTierName(solo.RatedTier);
            this.game_ycf.Text = solo != null && solo.RatedRating > 0
                ? $"{solo.RatedRating}" + (string.IsNullOrEmpty(ratedTierName) ? "" : $"（{ratedTierName}）")
                : "-";
        }

        /// <summary>
        /// 判断是否已获得有效段位。
        /// </summary>
        private static bool HasRank(LolRankedDataParser.RankedEntry? entry)
        {
            return entry != null
                && !string.IsNullOrEmpty(entry.Tier)
                && !string.Equals(entry.Tier, "NONE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 分页加载方法
        /// </summary>
        /// <param name="matchlists"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void InitPagin(GameHeadModel.MatchHistoryResponse? matchlists)
        {
            if (matchlists != null && matchlists.Games != null && matchlists.Games.Games != null && matchlists.Games.Games.Count > 0)
            {
                this.game_pagin.Total = matchlists.Games.Games.Count;
                this.game_pagin.Current = 1;
                this.game_pagin.PageSize = Convert.ToInt32(this.game_count.Text);
            }
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
            await UpdateGame_paginAsync();
        }

        /// <summary>
        /// 刷新
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void refeash_Click(object sender, EventArgs e)
        {
            await LoadGame();
            await UpdateGame_paginAsync();
        }

        /// <summary>
        /// 搜索指定玩家
        /// </summary>
        /// <param name="sender"></param>
        private async void PlayInfo_Click(object sender, EventArgs e)
        {
            string input = this.inp_playname.Text.Trim();
            if (string.IsNullOrEmpty(input))
            {
                AntdUI.Message.error(ParentForm!, "请输入 puuid 或 名称#TAG 进行查询");
                _infoMsgForm.AddMsg("请输入 puuid 或 名称#TAG 进行查询");
                return;
            }

            string? puuid = null;

            // 情况 1：包含 #，按 Riot ID（名称#TAG）解析
            if (input.Contains('#'))
            {
                var parts = input.Split('#', 2);
                string gameName = parts[0].Trim();
                string tagLine = parts[1].Trim();

                if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(tagLine))
                {
                    AntdUI.Message.error(ParentForm!, "格式错误，正确格式：名称#TAG（例如 玩家名#CN1）");
                    _infoMsgForm.AddMsg("格式错误，正确格式：名称#TAG");
                    return;
                }

                _infoMsgForm.AddMsg($"正在通过 Riot ID 搜索: {gameName}#{tagLine}");

                // 尝试通过 LCU API 直接搜索
                string summonerJson = await Assets_api.SearchSummonerByRiotId(gameName, tagLine);
                if (!string.IsNullOrEmpty(summonerJson))
                {
                    try
                    {
                        var player = JsonConvert.DeserializeObject<Plyaer>(summonerJson);
                        puuid = player?.puuid;
                    }
                    catch { }
                }

                // API 搜索失败，退回 match history 扫描
                if (string.IsNullOrEmpty(puuid))
                {
                    _infoMsgForm.AddMsg("LCU 搜索未返回结果，尝试从历史对局中匹配...");
                    puuid = GetUserPuuid(input);
                }
            }
            // 情况 2：长度 >= 30，视为 puuid
            else if (input.Length >= 30)
            {
                puuid = input;
            }
            // 情况 3：视为短名称，扫描历史对局
            else
            {
                _infoMsgForm.AddMsg($"正在从历史对局中搜索: {input}");
                puuid = GetUserPuuid(input);
            }

            if (string.IsNullOrEmpty(puuid))
            {
                AntdUI.Message.error(ParentForm!, "未找到该玩家，请检查输入是否正确");
                _infoMsgForm.AddMsg("未找到该玩家");
                return;
            }

            await LoadGame(puuid);
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
            _ = UpdateGame_paginAsync();
        }

        private void game_pagin_Click(object sender, EventArgs e)
        {
            _ = UpdateGame_paginAsync();
        }

        private async Task UpdateGame_paginAsync()
        {
            if (UserStatus == 1 && userinfo != null)
                await GetGameInfo(userinfo, this.game_pagin.Current);
            else if (UserStatus == 2 && userOhterinfo != null)
                await GetGameInfo(userOhterinfo, this.game_pagin.Current);
        }
    }
}

