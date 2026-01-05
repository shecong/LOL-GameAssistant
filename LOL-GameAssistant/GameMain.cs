using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Entity;
using Newtonsoft.Json;
using static LOL_GameAssistant.Entity.LolRankedDataParser;
using static LOL_GameAssistant.Entity.PlayerModel;
using System.Threading.Tasks;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public static InfoMsgForm infoMsg = new InfoMsgForm();
        public static HomeForm home = new HomeForm(infoMsg!);
        public static SettingForm settingForm = new SettingForm();
        public static LiveGameForm liveGameForm = new LiveGameForm();
        public Plyaer? userinfo = new Plyaer();

        /// <summary>
        /// 游戏状态枚举
        /// </summary>
        public static GameFlowPhase gameFlowPhase;

        public GameMain()
        {
            InitializeComponent();
        }

        public async void GameMain_Load(object sender, EventArgs e)
        {
            //初始化模块
            await LoadAllForm();
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = Convert.ToInt32(settingForm.inputNumber1.Text);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            //1.获取当前游戏状态
            _ = Task.Run(async () =>
           {
               var status = await Game_Api.GameFlowPhaseServer();
               if (status != null)
               {
                   if (Enum.TryParse(status, true, out GameFlowPhase parsedPhase))
                   {
                       gameFlowPhase = parsedPhase;
                       this.gameFlowPhaseName.Text = $"{gameFlowPhase.GetChineseName()}";
                   }
               }
           });
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
            tab1_grid1.Controls.Add(liveGameForm);
            //加载战绩查询
            //关于
            tab4_grid1.Controls.Add(new AboutForm() { Dock = DockStyle.Fill });
            //加载设置
            tabPage5.Controls.Clear();
            tabPage5.Controls.Add(settingForm);
            //加载日志窗口
            tab5_grid1.Controls.Clear();
            tab5_grid1.Controls.Add(infoMsg);
        }

        /// <summary>
        /// 刷新对局
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void dj_refresh_Click(object sender, EventArgs e)
        {
            await liveGameForm.AddView();
        }
    }
}