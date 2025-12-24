using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Entity;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
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
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(Program.GameMain, "未找到正在  运行的LOL客户端，请确保客户端已启动并登录。");
                _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                _infoMsgForm.AddMsg($"{port}:{token}");
            }
            //获取游戏版本号
            Game_Api.GetGameversion();
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
        /// 获取指定玩家信息
        /// </summary>
        /// <returns></returns>
        private async Task LoadGame(string puuid)
        {
            //获取客户端登陆
            GetlolLcu._infoMsgForm = _infoMsgForm;
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(Program.GameMain, "未找到正在  运行的LOL客户端，请确保客户端已启动并登录。");
                _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                _infoMsgForm.AddMsg($"{port}:{token}");
            }
            //获取游戏版本号
            Game_Api.GetGameversion();
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
                this.play_next.Text = Convert.ToString(userOhterinfo.xpSinceLastLevel);
                this.play_jd.Value = (float)userOhterinfo.xpUntilNextLevel / (float)(userOhterinfo.xpSinceLastLevel + userOhterinfo.xpUntilNextLevel);

                //获取当前召唤师游戏赛季信息
                GetGameSJ(userOhterinfo);
                //获取玩家比赛记录
                GetGameInfo(userOhterinfo);
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
            String begIndex = "1";
            String endIndex = "9999";

             matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

            //加载分页
            InitPagin(matchlists);
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

             matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

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
            await LoadGame();
        }

        /// <summary>
        /// 搜索指定玩家
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void PlayInfo_Click(object sender, EventArgs e)
        {
            string puuid = this.inp_playname.Text.Trim();
            if (string.IsNullOrEmpty(puuid))
            {
                AntdUI.Message.error(ParentForm!, "请输入puuid进行查询，召唤师名字查询仅支持在以往对局中匹配过的！");
                _infoMsgForm.AddMsg("请输入puuid进行查询，召唤师名字查询仅支持在以往对局中匹配过的！");
            }
            if (puuid.Length < 30)
            {
                //循环获取puuid
                  puuid = GetUserPuuid(puuid);
                if (puuid == "")
                {
                    AntdUI.Message.error(ParentForm!, "召唤师名字查询未找到在以往对局中匹配过的！");
                }
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
            for (int i = 0; i < matchlists.Games.Games.Count; i++)
            { 
                for (int j = 0; j < matchlists.Games.Games[i].ParticipantIdentities.Count; j++)
                {
                    if ($"{matchlists.Games.Games[i].ParticipantIdentities[j].Player.GameName}#{matchlists.Games.Games[i].ParticipantIdentities[j].Player.TagLine}".Contains(playername))
                    {
                        return matchlists.Games.Games[i].ParticipantIdentities[j].Player.Puuid;
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
            if (UserStatus == 1)
                GetGameInfo(userinfo, this.game_pagin.Current < 0 ? Convert.ToInt32(this.game_count) : this.game_pagin.Current);
            else if (UserStatus == 2)
                GetGameInfo(userOhterinfo, this.game_pagin.Current < 0 ? Convert.ToInt32(this.game_count) : this.game_pagin.Current);
        }
    }
}