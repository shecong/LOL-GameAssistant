using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class SettingForm : UserControl
    {
        public DateTime? lastOpenGameTime = DateTime.Now;

        public SettingForm()
        {
            InitializeComponent();
        }

        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadBase();
        }

        #region 定时执行方法

        /// <summary>
        /// 自动匹配对局
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public static void OpenGame(SettingForm form)
        {
            var now = DateTime.Now;
            if ((form.lastOpenGameTime == null || (now - form.lastOpenGameTime.Value).TotalSeconds >= 10) && form.swi_open.Checked)
            {
                Game_Api.OpenGameServer();
                form.lastOpenGameTime = now;
            }
        }

        /// <summary>
        /// 自动接受对局
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public static void GameTrue(SettingForm form)
        {
            if (form.swi_gametrue.Checked)
            {
                Game_Api.GameTrueServer();
            }
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
    }
}