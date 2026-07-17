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
        /// 自动匹配对局（线程安全，始终在 UI 线程执行）
        /// </summary>
        public static void OpenGame(SettingForm form)
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(() => OpenGame(form));
                return;
            }

            var now = DateTime.Now;
            if ((form.lastOpenGameTime == null || (now - form.lastOpenGameTime.Value).TotalSeconds >= 10) && form.swi_open.Checked)
            {
                _ = Game_Api.OpenGameServerAsync();
                form.lastOpenGameTime = now;
            }
        }

        /// <summary>
        /// 自动接受对局（线程安全，始终在 UI 线程执行）
        /// </summary>
        public static void GameTrue(SettingForm form)
        {
            if (form.InvokeRequired)
            {
                form.BeginInvoke(() => GameTrue(form));
                return;
            }

            if (form.swi_gametrue.Checked)
            {
                _ = Game_Api.GameTrueServerAsync();
            }
        }

        #endregion 定时执行方法

        /// <summary>
        /// 初始化基础数据
        /// </summary>
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
