using LOL_GameAssistant.Helper;
using System.Diagnostics;
using System.Security.Principal;

namespace LOL_GameAssistant
{
    internal static class Program
    {
        public static Form GameMain { get; private set; } = null!;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            if (Environment.OSVersion.Version >= new Version(6, 3))
            {
                //Application.SetHighDpiMode(HighDpiMode.SystemAware);
                // 或者使用 PerMonitorV2，这是目前最好的模式
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            }

            // 设置全局异常处理
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            log4net.Config.XmlConfigurator.Configure();

            ApplicationConfiguration.Initialize();

            //获得当前登录的Windows用户标示
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            //判断当前登录用户是否为管理员
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                //如果是管理员，则直接运行
                Application.Run(new GameMain());
            }
            else
            {
                //创建启动对象
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Application.ExecutablePath;
                //设置启动动作,确保以管理员身份运行
                startInfo.Verb = "runas";
                try
                {
                    Process.Start(startInfo);
                }
                catch
                {
                    return;
                }
                //退出
                Application.Exit();
            }
        }

        // UI线程异常处理
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);

            log4net.ILog log = log4net.LogManager.GetLogger("testApp.Logging");//获取一个日志记录器

            log.Error(DateTime.Now.ToString() + e.Exception);//写入一条新log
        }

        // 非UI线程异常处理
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
                log4net.ILog log = log4net.LogManager.GetLogger("testApp.Logging");//获取一个日志记录器
                log.Error(DateTime.Now.ToString() + ex);//写入一条新log
            }
        }

        private static void HandleException(Exception ex)
        {
            // 记录日志
            string logMessage = $"[{DateTime.Now}] 异常信息: {ex.Message}\n堆栈跟踪: {ex.StackTrace}\n";
            System.IO.File.AppendAllText("error.log", logMessage);

            // 显示友好错误信息
            AntdUI.Message.error(GameMain, $"程序发生错误: {ex.Message}\n请查看日志文件获取详细信息。");

            // 可以选择是否退出应用
            // Application.Exit();
        }
    }
}