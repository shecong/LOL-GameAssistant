using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant
{
    internal static class Program
    {
        public static GameMain GameMain { get; private set; } = new GameMain();

        private static IInfoMsgForm? _infoMsgForm;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // ���� DPI ��֪ģʽ��������ڳ��������ʼ��
            //SetProcessDPIAware(); // Windows 7/8
            // ����ʹ�����·�ʽ���Ƽ�����
            SetProcessDpiAwareness(_Process_DPI_Awareness.Process_Per_Monitor_DPI_Aware);

            // ����ȫ���쳣����
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            ApplicationConfiguration.Initialize();

            //��õ�ǰ��¼��Windows�û���ʾ
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            //�жϵ�ǰ��¼�û��Ƿ�Ϊ����Ա
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                //����ǹ���Ա����ֱ������
                Application.Run(GameMain);
            }
            else
            {
                //������������
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Application.ExecutablePath;
                //������������,ȷ���Թ���Ա��������
                startInfo.Verb = "runas";
                try
                {
                    Process.Start(startInfo);
                }
                catch
                {
                    return;
                }
                //�˳�
                Application.Exit();
            }
        }

        // UI�߳��쳣����
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);

            GameMain.infoMsg.AddMsg($"{e.Exception}");
        }

        // ��UI�߳��쳣����
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
            // 记录日志
            string logMessage = $"[{DateTime.Now}] 异常信息: {ex.Message}\n堆栈跟踪: {ex.StackTrace}\n";
            System.IO.File.AppendAllText("error.log", logMessage);

            // 显示友好错误消息（确保在 UI 线程执行）
            GameMain.BeginInvoke(new Action(() =>
            {
                AntdUI.Message.error(GameMain, $"程序发生异常: {ex.Message}\n请查看日志文件获取详细信息。");
            }));

            GameMain.infoMsg.AddMsg($"{ex.Message}");
        }

        // DPI ��֪ API
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