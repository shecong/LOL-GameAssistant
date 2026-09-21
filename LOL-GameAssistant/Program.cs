using System.Diagnostics;
using System.Runtime.InteropServices;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant
{
    internal static class Program
    {
        public static GameMain GameMain { get; private set; } = new GameMain();

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 设置 DPI 感知模式（必须放在程序启动最开始）
            //SetProcessDPIAware(); // Windows 7/8
            // 或者使用以下方式（推荐）：
            SetProcessDpiAwareness(_Process_DPI_Awareness.Process_Per_Monitor_DPI_Aware);

            // 设置全局异常处理
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            ApplicationConfiguration.Initialize();

            // 说明：读取 LCU lockfile / WMI 命令行并不需要管理员权限，
            // 因此不再强制 UAC 提权，避免每次启动都弹窗。
            // 如后续功能确实需要管理员权限，可调用 AdminPermissionHelper.EnsureAdminPermission()。
            Application.Run(GameMain);
        }

        // UI线程异常处理
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);

            GameMain.infoMsg.AddMsg($"{e.Exception}");
        }

        // 非UI线程异常处理
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
                GameMain.infoMsg.AddMsg($"{ex}");
            }
        }

        private static void HandleException(Exception ex)
        {
            try
            {
                // 用相对路径时，开机自启（注册表 Run 项没有工作目录，CWD 是 System32）
                // 会写失败；而这里本身就在异常处理路径上，再抛一次会直接把进程带崩。
                string logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "error.log");
                string logMessage = $"[{DateTime.Now}] 异常信息: {ex.Message}\n堆栈跟踪: {ex.StackTrace}\n";
                System.IO.File.AppendAllText(logPath, logMessage);
            }
            catch
            {
                // 日志写不进去也不能影响后续处理
            }

            try
            {
                GameMain.infoMsg.AddMsg($"{ex.Message}");
            }
            catch
            {
                // 日志窗口不可用时忽略
            }

            // 显示友好错误信息
            try
            {
                AntdUI.Message.error(GameMain, $"程序发生错误: {ex.Message}\n请查看日志文件获取详细信息。");
            }
            catch
            {
                // 提示失败（例如窗口句柄已耗尽）时忽略，避免异常处理器自身再抛
            }
            // 可以选择是否退出应用
            // Application.Exit();
        }

        // DPI 感知 API
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(_Process_DPI_Awareness value);

        private enum _Process_DPI_Awareness
        {
            Process_DPI_Unaware = 0,
            Process_System_DPI_Aware = 1,
            Process_Per_Monitor_DPI_Aware = 2
        }
    }
}
