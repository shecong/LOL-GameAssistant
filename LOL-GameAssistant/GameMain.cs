using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Model;
using LOL_GameAssistant.Models;
using Newtonsoft.Json;
using static LOL_GameAssistant.Model.LolRankedDataParser;
using static LOL_GameAssistant.Model.PlayerModel;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public Plyaer? userinfo = new Plyaer();

        private readonly System.Windows.Forms.Timer timer_open = new();
        private readonly System.Windows.Forms.Timer timer_gametrue = new();
        private readonly System.Windows.Forms.Timer timer_jyyx = new();
        private readonly System.Windows.Forms.Timer timer_xyx = new();

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            await LoadGame();

            await LoadBase();

            await LoadTimer();
        }

        /// <summary>
        /// 加载定时器
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private async Task LoadTimer()
        {
            swi_open.CheckedChanged += (s, e) =>
            {
                if (swi_open.Checked) timer_open.Start();
                else timer_open.Stop();
            };
            swi_gametrue.CheckedChanged += (s, e) =>
            {
                if (swi_gametrue.Checked) timer_gametrue.Start();
                else timer_gametrue.Stop();
            };
            swi_jyyx.CheckedChanged += (s, e) =>
            {
                if (swi_jyyx.Checked) timer_jyyx.Start();
                else timer_jyyx.Stop();
            };
            swi_xyx.CheckedChanged += (s, e) =>
            {
                if (swi_xyx.Checked) timer_xyx.Start();
                else timer_xyx.Stop();
            };
            timer_open.Interval = 1000; // 1秒
            timer_open.Tick += (s, e) => OpenGame();
            timer_gametrue.Interval = 1000; // 1秒
            timer_gametrue.Tick += (s, e) => GameTrue();
        }

        #region 定时执行方法

        /// <summary>
        /// 自动匹配对局
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        private void OpenGame()
        {
            Game_Api.OpenGameServer();
        }

        /// <summary>
        /// 自动接受对局
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        private void GameTrue()
        {
            Game_Api.GameTrueServer();
        }

        #endregion 定时执行方法

        /// <summary>
        /// 初始化基础数据
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private async Task LoadBase()
        {
            // 获取所有英雄
            var allChampions = ChampionMap.GetChampionMap();
            for (int i = 0; i < allChampions.Count; i++)
            {
                this.setting_select_jyx.Items.Add(allChampions.ElementAt(i).Value.RealName);
                this.setting_select_xyx.Items.Add(allChampions.ElementAt(i).Value.RealName);
            }
        }

        /// <summary>
        /// 初始化游戏
        /// </summary>
        /// <returns></returns>
        private async Task LoadGame()
        {
            //获取客户端登陆
            //(string? token, string? port) = LeagueAuthHelper.GetAuth();
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(this, "未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                Console.WriteLine($"{port}:{token}");
            }
            //获取游戏版本号
            Game_Api.GetGameversion();
            //获取当前召唤师信息
            userinfo = JsonConvert.DeserializeObject<Plyaer>(await Assets_api.GetUser());
            if (userinfo != null)
            {
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
                this.play_QF.Text = "";
                this.play_dj.Text = userinfo.summonerLevel;
                this.play_next.Text = Convert.ToString(userinfo.xpSinceLastLevel);
                this.play_jd.Value = (float)userinfo.xpUntilNextLevel / (float)(userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel);

                //获取当前召唤师游戏赛季信息
                GetGameSJ(userinfo);
                //获取玩家比赛记录
                GetGameInfo(userinfo);
            }
        }

        /// <summary>
        /// 获取玩家的比赛记录
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task GetGameInfo(Plyaer userinfo)
        {
            if (userinfo == null) return;
            String begIndex = "1";
            String endIndex = "9999";

            GameHeadModel.MatchHistoryResponse? matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

            //加载分页
            InitPagin(matchlists);

            //// 方法2：原地修改（如果集合是List<T>）
            //var sortedList = matchlists.Games.Games
            //    .OrderByDescending(p => p.GameCreation)
            //    .ToList();
            //matchlists.Games.Games.Clear(); // 清空原集合
            //matchlists.Games.Games.AddRange(sortedList); // 添加排序后的元素
            ////加载数据到界面matchlists?.Games?.Games?.Count.
            //stackPanel1.Controls.Clear();
            //for (int i = Convert.ToInt32(this.game_count.Text) - 1; i >= 0 & i < Convert.ToInt32(this.game_count.Text); i--)
            //{
            //    try
            //    {
            //        recordForm record = new recordForm();
            //        record.setInfo(matchlists.Games.Games[i], userinfo.puuid);
            //        this.stackPanel1.Controls.Add(record);
            //    }
            //    catch (Exception)
            //    {
            //        i += i;
            //        continue;
            //    }
            //}
        }

        /// <summary>
        /// 获取玩家的比赛记录（分页）
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async Task GetGameInfo(Plyaer userinfo, int pageindex)
        {
            stackPanel1.Controls.Clear();
            if (userinfo == null) return;
            int pageSize = Convert.ToInt32(this.game_count.Text);
            String begIndex = "0";
            String endIndex = pageSize <= 10 ? "10" : this.game_count.Text;

            GameHeadModel.MatchHistoryResponse? matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

            // 排序
            var sortedList = matchlists.Games.Games
                .OrderByDescending(p => p.GameCreation)
                .ToList();

            // 计算分页
            int total = sortedList.Count;
            int skip = (pageindex == 1 ? 1 : (pageindex - 1) * pageSize) - 1;
            var pageList = sortedList.Skip(skip).Take(pageSize).ToList();

            // 加载数据到界面
            for (int i = pageList.Count - 1; i >= 0 & i < pageList.Count; i--)
            {
                try
                {
                    recordForm record = new recordForm();

                    record.setInfo(pageList[i], userinfo.puuid);
                    this.stackPanel1.Controls.Add(record);
                }
                catch (Exception)
                {
                    i += i;
                    continue;
                }
            }
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
        /// 获取玩家赛季信息
        /// </summary>
        /// <param name="plyaer"></param>
        /// <exception cref="NotImplementedException"></exception>
        private async void GetGameSJ(Plyaer? userinfo = null)
        {
            if (userinfo == null) return;
            LolRankedDataParser lolparser = new LolRankedDataParser();
            LolRankedDataParser.RankedData gameinfo = await Game_Api.GetUserGame(userinfo.puuid);
            if (gameinfo == null) return;
            //获取单双排信息
            LolRankedDataParser.RankedEntry solo = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_SOLO_5x5);
            //获取灵活5v5信息
            LolRankedDataParser.RankedEntry flex = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_FLEX_SR);
            //计算信息
            if (solo != null)
            {
                this.pic_dsp.Image = CheckTierImg(solo.Tier);
                this.game_dspT.Text = CheckTierName(solo.Tier) + solo.Division;
                this.game_dsp_sl.Text = Convert.ToString(solo.WinRate);
                this.game_dsp_win.Text = Convert.ToString(solo.CurrentSeasonWinsForRewards);
                this.game_dsp_loss.Text = Convert.ToString(solo.Losses);
            }
            if (flex != null)
            {
                this.pic_lhp.Image = CheckTierImg(flex.Tier);
                this.game_lhpT.Text = CheckTierName(flex.Tier) + flex.Division;
                this.game_lhp_sl.Text = Convert.ToString(flex.WinRate);
                this.game_lhp_win.Text = Convert.ToString(flex.CurrentSeasonWinsForRewards);
                this.game_lhp_loss.Text = Convert.ToString(flex.Losses);
            }
            //获取赛点信息
            this.game_dws.Text = solo?.ProvisionalGamesRemaining >= 10 ? "是" : "否";
            this.game_jjs.Text = string.IsNullOrEmpty(solo?.MiniSeriesProgress) ? "非定级赛" : "solo.MiniSeriesProgress";
            this.game_jjscount.Text = "未知";
            this.game_dqsd.Text = Convert.ToString(solo?.LeaguePoints);
            if (gameinfo.Seasons.TryGetValue("RANKED_SOLO_5x5", out SeasonInfo? value))
            {
                this.game_sjend.Text = Convert.ToString(value.SeasonEndDateTime);
            }

            this.game_ycf.Text = Convert.ToString(solo?.RatedRating);
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
                    return "青铜";

                case "BRONZE":
                    return "黑铁";

                case "SILVER":
                    return "白银";

                case "GOLD":
                    return "黄金";

                case "PLATINUM":
                    return "铂金";

                case "DIAMOND":
                    return "砖石";

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
        /// 重置按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void btn_back_Click(object sender, EventArgs e)
        {
            await LoadGame();
        }

        /// <summary>
        /// 刷新数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void refeash_Click(object sender, EventArgs e)
        {
            await LoadGame();
        }

        /// <summary>
        /// 分页调整界面
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void game_pagin_ValueChanged(object sender, AntdUI.PagePageEventArgs e)
        {
            GetGameInfo(userinfo, this.game_pagin.Current < 0 ? Convert.ToInt32(this.game_count) : this.game_pagin.Current);
        }

        #region 战绩对局

        /// <summary>
        /// 加载单局游戏详情
        /// </summary>
        /// <param name="gameid"></param>
        public async void game_info(string gameid)
        {
            if (string.IsNullOrEmpty(gameid)) return;
            //根据表头获取明细信息
            GameDetailModel.GameInfo? gameInfo = new GameDetailModel.GameInfo();
            gameInfo = await Game_Api.GetGameDetail(gameid);
            if (gameInfo == null) return;
            //游戏数据
            GameDetailModel.ParticipantsItem? gamer = gameInfo.participants.Where(p => p.participantId == gameInfo.participantIdentities.Where(p => p.player.puuid == userinfo.puuid).FirstOrDefault().participantId).FirstOrDefault<GameDetailModel.ParticipantsItem>();
            if (gamer == null) return;
            //游戏详情
        }

        #endregion 战绩对局
    }
}