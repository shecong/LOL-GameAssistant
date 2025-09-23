using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Model;
using Newtonsoft.Json;
using static LOL_GameAssistant.Model.LolRankedDataParser;
using static LOL_GameAssistant.Model.PlayerModel;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public GameMain()
        {
            InitializeComponent();
            log4net.ILog log = log4net.LogManager.GetLogger("testApp.Logging");//获取一个日志记录器

            log.Info(DateTime.Now.ToString() + "1111");//写入一条新log
        }

        public void GameMain_Load(object sender, EventArgs e)
        {
            //获取客户端登陆
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(this, "未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
            }
            //获取当前召唤师信息
            Plyaer? userinfo = JsonConvert.DeserializeObject<Plyaer>(Assets_api.GetUser());
            if (userinfo != null)
            {
                //获取头像
                Stream headicon = Assets_api.GetImg(userinfo.profileIconId);
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
            }
        }

        /// <summary>
        /// 获取玩家赛季信息
        /// </summary>
        /// <param name="plyaer"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void GetGameSJ(Plyaer userinfo = null)
        {
            if (userinfo == null) return;
            LolRankedDataParser lolparser = new LolRankedDataParser();
            LolRankedDataParser.RankedData gameinfo = Game_Api.GetUserGame(userinfo.puuid);
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
            //
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
    }
}