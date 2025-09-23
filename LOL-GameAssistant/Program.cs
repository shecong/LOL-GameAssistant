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
            // 设置全局异常处理
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            log4net.Config.XmlConfigurator.Configure();

            ApplicationConfiguration.Initialize();
            GameMain = new GameMain();
            Application.Run(GameMain);
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