using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class SettingForm : UserControl
    {
        private static readonly System.Windows.Forms.Timer timer_open = new();
        private static readonly System.Windows.Forms.Timer timer_gametrue = new();
        private static readonly System.Windows.Forms.Timer timer_jyyx = new();
        private static readonly System.Windows.Forms.Timer timer_xyx = new();

        public SettingForm()
        {
            InitializeComponent();
        }

        private async void SettingForm_Load(object sender, EventArgs e)
        {
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
    }
}