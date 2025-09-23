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
            // ����ȫ���쳣����
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            log4net.Config.XmlConfigurator.Configure();

            ApplicationConfiguration.Initialize();
            GameMain = new GameMain();
            Application.Run(GameMain);
        }

        // UI�߳��쳣����
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);

            log4net.ILog log = log4net.LogManager.GetLogger("testApp.Logging");//��ȡһ����־��¼��

            log.Error(DateTime.Now.ToString() + e.Exception);//д��һ����log
        }

        // ��UI�߳��쳣����
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
                log4net.ILog log = log4net.LogManager.GetLogger("testApp.Logging");//��ȡһ����־��¼��
                log.Error(DateTime.Now.ToString() + ex);//д��һ����log
            }
        }

        private static void HandleException(Exception ex)
        {
            // ��¼��־
            string logMessage = $"[{DateTime.Now}] �쳣��Ϣ: {ex.Message}\n��ջ����: {ex.StackTrace}\n";
            System.IO.File.AppendAllText("error.log", logMessage);

            // ��ʾ�Ѻô�����Ϣ
            AntdUI.Message.error(GameMain, $"����������: {ex.Message}\n��鿴��־�ļ���ȡ��ϸ��Ϣ��");

            // ����ѡ���Ƿ��˳�Ӧ��
            // Application.Exit();
        }
    }
}