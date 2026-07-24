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
        /// 1=当前玩家，2=指定玩家
        /// </summary>
        public static int UserStatus = 1;

        /// <summary>
        /// 防重入标志位
        /// </summary>
        private bool _isLoading = false;

        public HomeForm(IInfoMsgForm infoMsgForm)
        {
            InitializeComponent();
            _infoMsgForm = infoMsgForm;
        }

        /// <summary>
        /// 初始化加载数据
        /// </summary>
        private async void HomeForm_Load(object sender, EventArgs e)
        {
            await LoadGame();
        }

        /// <summary>
        /// 安全的 Image.FromStream 替代——将流复制到 MemoryStream 再创建 Image
        /// </summary>
        private static Image LoadImageSafe(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }

        /// <summary>
        /// 释放旧 Image（如有）
        /// </summary>
        private void DisposeOldImage(Image? oldImage)
        {
            if (oldImage != null)
            {
                try { oldImage.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 初始化游戏
        /// </summary>
        private async Task LoadGame()
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                // 获取客户端登陆
                GetlolLcu._infoMsgForm = _infoMsgForm;
                (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
                if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
                {
                    AntdUI.Message.error(Program.GameMain, "未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
                    _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
                    return;
                }

                HttpClentHelper.SetCredentials(port, token);
                // 不打印 token 到日志（安全考虑）
                _infoMsgForm.AddMsg($"已连接到LOL客户端端口:{port}");

                // 获取游戏版本号
                await Game_Api.GetGameversionAsync();

                // 获取当前召唤师信息
                var userJson = await Assets_api.GetUser();
                if (string.IsNullOrEmpty(userJson)) return;

                userinfo = JsonConvert.DeserializeObject<Plyaer>(userJson);
                if (userinfo != null)
                {
                    UserStatus = 1;
                    // 获取头像（释放旧 Image）
                    Stream headicon = await Assets_api.GetImg(userinfo.profileIconId);
                    if (headicon != null)
                    {
                        DisposeOldImage(this.play_HeadIcon.Image);
                        this.play_HeadIcon.Image = LoadImageSafe(headicon);
                    }
                    this.play_name.Text = userinfo.gameName;
                    this.play_number.Text = $"#{userinfo.tagLine}";
                    this.play_QF.Text = "";
                    this.play_dj.Text = userinfo.summonerLevel;
                    this.play_next.Text = Convert.ToString(userinfo.xpSinceLastLevel);
                    // 避免满级号除零 NaN
                    int denom = userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel;
                    this.play_jd.Value = denom > 0 ? (float)userinfo.xpUntilNextLevel / (float)denom : 0f;

                    // 获取赛季信息
                    await GetGameSJAsync(userinfo);
                    // 获取比赛记录
                    await GetGameInfo(userinfo);
                }
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"加载首页信息异常: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// 获取指定玩家信息
        /// </summary>
        private async Task LoadGame(string puuid)
        {
            if (_isLoading) return;
            _isLoading = true;
            try
            {
                // 获取客户端登陆
                GetlolLcu._infoMsgForm = _infoMsgForm;
                (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
                if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
                {
                    AntdUI.Message.error(Program.GameMain, "未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
                    _infoMsgForm.AddMsg("未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
                    return;
                }

                HttpClentHelper.SetCredentials(port, token);
                _infoMsgForm.AddMsg($"已连接到LOL客户端端口:{port}");

                // 获取游戏版本号
                await Game_Api.GetGameversionAsync();

                // 获取指定召唤师信息
                var userJson = await Assets_api.GetUser(puuid);
                if (string.IsNullOrEmpty(userJson)) return;

                userOhterinfo = JsonConvert.DeserializeObject<Plyaer>(userJson);
                if (userOhterinfo != null)
                {
                    UserStatus = 2;
                    // 获取头像
                    Stream headicon = await Assets_api.GetImg(userOhterinfo.profileIconId);
                    if (headicon != null)
                    {
                        DisposeOldImage(this.play_HeadIcon.Image);
                        this.play_HeadIcon.Image = LoadImageSafe(headicon);
                    }
                    this.play_name.Text = userOhterinfo.gameName;
                    this.play_number.Text = $"#{userOhterinfo.tagLine}";
                    this.play_QF.Text = "";
                    this.play_dj.Text = userOhterinfo.summonerLevel;
                    this.play_next.Text = Convert.ToString(userOhterinfo.xpSinceLastLevel);
                    int denom = userOhterinfo.xpSinceLastLevel + userOhterinfo.xpUntilNextLevel;
                    this.play_jd.Value = denom > 0 ? (float)userOhterinfo.xpUntilNextLevel / (float)denom : 0f;

                    // 获取赛季信息
                    await GetGameSJAsync(userOhterinfo);
                    // 获取比赛记录
                    await GetGameInfo(userOhterinfo);
                }
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"加载指定玩家信息异常: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// 获取玩家的比赛记录（初始加载）
        /// </summary>
        private async Task GetGameInfo(Plyaer? userinfo)
        {
            if (userinfo == null) return;
            String begIndex = "1";
            String endIndex = "9999";

            matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

            // 加载分页
            InitPagin(matchlists);
        }

        /// <summary>
        /// 获取玩家的比赛记录（分页）
        /// </summary>
        private async Task GetGameInfo(Plyaer userinfo, int pageindex)
        {
            if (userinfo == null) return;

            // 释放旧控件
            foreach (Control c in stackPanel1.Controls) c.Dispose();
            stackPanel1.Controls.Clear();

            int pageSize = Convert.ToInt32(this.game_count.Text);
            if (pageSize <= 0) pageSize = 10;
            String begIndex = "0";
            String endIndex = pageSize <= 10 ? "10" : this.game_count.Text;

            matchlists = await Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);

            // null 检查
            if (matchlists?.Games?.Games == null) return;

            // 排序
            var sortedList = matchlists.Games.Games
                .OrderByDescending(p => p.GameCreation)
                .ToList();

            // 计算分页
            int total = sortedList.Count;
            int skip = Math.Max(0, (pageindex - 1) * pageSize);
            var pageList = sortedList.Skip(skip).Take(pageSize).ToList();

            // 加载数据到界面
            for (int i = pageList.Count - 1; i >= 0 && i < pageList.Count; i--)
            {
                try
                {
                    recordForm record = new recordForm();
                    record.setInfo(pageList[i], userinfo.puuid);
                    this.stackPanel1.Controls.Add(record);
                }
                catch (Exception ex)
                {
                    _infoMsgForm.AddMsg($"加载比赛记录异常: {ex.Message}");
                    continue;
                }
            }
        }

        /// <summary>
        /// 获取玩家赛季信息（改为 async Task）
        /// </summary>
        private async Task GetGameSJAsync(Plyaer? userinfo = null)
        {
            if (userinfo == null) return;
            try
            {
                LolRankedDataParser lolparser = new LolRankedDataParser();
                LolRankedDataParser.RankedData? gameinfo = await Game_Api.GetUserGame(userinfo.puuid);
                if (gameinfo == null) return;

                // 获取单双排信息
                LolRankedDataParser.RankedEntry? solo = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_SOLO_5x5);
                // 获取灵活5v5信息
                LolRankedDataParser.RankedEntry? flex = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_FLEX_SR);

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

                // 获取赛点信息
                this.game_dws.Text = solo?.ProvisionalGamesRemaining >= 10 ? "是" : "否";
                // 修复：使用变量值而非字面量字符串
                this.game_jjs.Text = string.IsNullOrEmpty(solo?.MiniSeriesProgress) ? "非定级赛" : solo!.MiniSeriesProgress;
                this.game_jjscount.Text = "未知";
                this.game_dqsd.Text = Convert.ToString(solo?.LeaguePoints);
                if (gameinfo.Seasons.TryGetValue("RANKED_SOLO_5x5", out SeasonInfo? value))
                {
                    this.game_sjend.Text = Convert.ToString(value.SeasonEndDateTime);
                }

                this.game_ycf.Text = Convert.ToString(solo?.RatedRating);
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"加载赛季信息异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 分页加载方法
        /// </summary>
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
        private Image CheckTierImg(string tier)
        {
            switch (tier)
            {
                case "IRON": return Properties.Resources._01;
                case "BRONZE": return Properties.Resources._02;
                case "SILVER": return Properties.Resources._03;
                case "GOLD": return Properties.Resources._04;
                case "PLATINUM": return Properties.Resources._05;
                case "DIAMOND": return Properties.Resources._06;
                case "MASTER": return Properties.Resources._07;
                case "GRANDMASTER": return Properties.Resources._08;
                case "CHALLENGER": return Properties.Resources._09;
                default: return Properties.Resources.下载;
            }
        }

        /// <summary>
        /// 根据段位返回对应文字（修复颠倒的段位映射）
        /// </summary>
        private String CheckTierName(string tier)
        {
            switch (tier)
            {
                case "IRON": return "黑铁";
                case "BRONZE": return "青铜";
                case "SILVER": return "白银";
                case "GOLD": return "黄金";
                case "PLATINUM": return "铂金";
                case "DIAMOND": return "钻石";
                case "MASTER": return "超凡大师";
                case "GRANDMASTER": return "宗师";
                case "CHALLENGER": return "最强王者";
                default: return "无段位";
            }
        }

        /// <summary>
        /// 重置
        /// </summary>
        private async void btn_back_Click(object sender, EventArgs e)
        {
            try
            {
                await LoadGame();
                await UpdateGame_paginAsync();
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"重置异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新
        /// </summary>
        private async void refeash_Click(object sender, EventArgs e)
        {
            try
            {
                await LoadGame();
                await UpdateGame_paginAsync();
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"刷新异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 搜索指定玩家
        /// </summary>
        private async void PlayInfo_Click(object sender, EventArgs e)
        {
            try
            {
                string puuid = this.inp_playname.Text.Trim();
                if (string.IsNullOrEmpty(puuid))
                {
                    AntdUI.Message.error(ParentForm!, "请输入puuid进行查询，召唤师名字查询仅支持在以往对局中匹配过的！");
                    _infoMsgForm.AddMsg("请输入puuid进行查询，召唤师名字查询仅支持在以往对局中匹配过的！");
                    return;
                }

                if (puuid.Length < 30)
                {
                    // 循环获取puuid
                    puuid = GetUserPuuid(puuid);
                    if (string.IsNullOrEmpty(puuid))
                    {
                        AntdUI.Message.error(ParentForm!, "召唤师名字查询未找到在以往对局中匹配过的！");
                        return;
                    }
                }

                await LoadGame(puuid);
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"搜索玩家异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 循环历史游戏数据找到匹配的puuid
        /// </summary>
        private string GetUserPuuid(string playername)
        {
            if (matchlists?.Games?.Games == null) return "";

            for (int i = 0; i < matchlists.Games.Games.Count; i++)
            {
                var identities = matchlists.Games.Games[i].ParticipantIdentities;
                if (identities == null) continue;

                for (int j = 0; j < identities.Count; j++)
                {
                    var player = identities[j]?.Player;
                    if (player == null) continue;

                    var fullName = $"{player.GameName}#{player.TagLine}";
                    if (fullName.Contains(playername, StringComparison.OrdinalIgnoreCase))
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
        private void game_pagin_ValueChanged(object sender, AntdUI.PagePageEventArgs e)
        {
        }

        private async void game_pagin_Click(object sender, EventArgs e)
        {
            await UpdateGame_paginAsync();
        }

        private async Task UpdateGame_paginAsync()
        {
            try
            {
                int page = this.game_pagin.Current < 0 ? Convert.ToInt32(this.game_count.Text) : this.game_pagin.Current;
                int pageSize = Math.Max(1, Convert.ToInt32(this.game_count.Text));
                if (UserStatus == 1 && userinfo != null)
                    await GetGameInfo(userinfo, page);
                else if (UserStatus == 2 && userOhterinfo != null)
                    await GetGameInfo(userOhterinfo, page);
            }
            catch (Exception ex)
            {
                _infoMsgForm.AddMsg($"分页异常: {ex.Message}");
            }
        }
    }
}
