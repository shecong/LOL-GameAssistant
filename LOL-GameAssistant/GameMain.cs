using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Entity;
using Newtonsoft.Json;
using static LOL_GameAssistant.Entity.LolRankedDataParser;
using static LOL_GameAssistant.Entity.PlayerModel;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public static InfoMsgForm infoMsg = new InfoMsgForm();
        public static HomeForm home = new HomeForm(infoMsg!);
        public static SettingForm settingForm = new SettingForm();
        public Plyaer? userinfo = new Plyaer();

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            //初始化模块
            await LoadAllForm();
        }

        /// <summary>
        /// 初始化加载所有窗体
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private async Task LoadAllForm()
        {
            //加载首页
            tab0_grid1.Controls.Clear();
            tab0_grid1.Controls.Add(home);

            //加载对局
            tab1_grid1.Controls.Clear();
            tab1_grid1.Controls.Add(new LiveGameForm());
            //加载战绩查询
            //关于
            //加载设置
            tabPage5.Controls.Clear();
            tabPage5.Controls.Add(settingForm);
            //加载日志窗口
            tab5_grid1.Controls.Clear();
            tab5_grid1.Controls.Add(infoMsg);
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