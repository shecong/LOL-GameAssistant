using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    public class AdminPermissionHelper
    {
        /// <summary>
        /// 检查并确保应用程序以管理员权限运行
        /// </summary>
        /// <returns>如果已经是以管理员运行返回true，如果需要重启返回false</returns>
        public static bool EnsureAdminPermission()
        {
            if (IsRunningAsAdministrator())
            {
                return true;
            }

            RestartAsAdministrator();
            return false;
        }

        /// <summary>
        /// 检查当前是否以管理员身份运行
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// 使用管理员权限重新启动应用程序
        /// </summary>
        private static void RestartAsAdministrator()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Application.ExecutablePath,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"需要管理员权限运行此程序。错误: {ex.Message}",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}