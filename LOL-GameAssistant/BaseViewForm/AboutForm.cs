using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Security.Policy;
using System.Text;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class AboutForm : UserControl
    {
        public AboutForm()
        {
            InitializeComponent();
        }

        private void btn_opengithub_Click(object sender, EventArgs e)
        {
            try
            {
                // 这是最简单直接的方式
                Process.Start(new ProcessStartInfo("https://github.com/shecong/LOL-GameAssistant")
                {
                    UseShellExecute = true  // 关键：使用系统外壳执行
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"打开链接失败: {ex.Message}");
            }
        }

        private void btn_update_Click(object sender, EventArgs e)
        {
            AntdUI.Message.info(ParentForm!, "陆续开发中。。。");
        }

        private void btn_github_Click(object sender, EventArgs e)
        {
            try
            {
                // 这是最简单直接的方式
                Process.Start(new ProcessStartInfo("https://github.com/shecong/LOL-GameAssistant")
                {
                    UseShellExecute = true  // 关键：使用系统外壳执行
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"打开链接失败: {ex.Message}");
            }
        }
    }
}