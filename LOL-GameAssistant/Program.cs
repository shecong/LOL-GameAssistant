using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant
{
    /// <summary>程序入口：配置应用启动环境与生命周期。</summary>
    internal static class Program
    {
        public static GameMain GameMain { get; private set; } = null!;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // Complete elevation before acquiring the instance lock or creating any windows.
            using (new StartupDiagnostics("权限检查"))
                if (!AdminPermissionHelper.EnsureAdminPermission()) return;
            RuntimeDiagnostics.Report("启动环境", "运行中", $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} | 进程 {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} | .NET {Environment.Version}");
            using var processStartup = new StartupDiagnostics("主窗口就绪前");
            TaskbarApplicationIdentity.InitializeProcess();
            ApplicationConfiguration.Initialize();
            if (UpdateInstaller.HasPendingRecovery())
            {
                AntdUI.Modal.open(new AntdUI.Modal.Config("更新恢复",
                    "检测到未完成的更新。请关闭助手，运行程序目录中的 Updater.exe 恢复后再启动。", AntdUI.TType.Warn) { CancelText = null });
                return;
            }

            int replaceRequested = 0;
            using var singleInstance = SingleInstanceCoordinator.StartOrReplace(() =>
            {
                Interlocked.Exchange(ref replaceRequested, 1);
                if (GameMain is { IsHandleCreated: true, IsDisposed: false } main)
                {
                    try { main.BeginInvoke(new Action(System.Windows.Forms.Application.Exit)); }
                    catch (InvalidOperationException) { }
                }
            });
            if (singleInstance == null)
            {
                AntdUI.Modal.open(new AntdUI.Modal.Config("sc助手",
                    "前一个实例未能在 15 秒内退出，请手动关闭后重试。", AntdUI.TType.Warn) { CancelText = null });
                return;
            }
            if (Volatile.Read(ref replaceRequested) != 0) return;
            RuntimeDiagnostics.ReportStartup();

            // 设置全局异常处理
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Apply the saved palette before any page or native handle is created.
            // Otherwise dark-mode startup first paints the default light surfaces.
            Domain.Settings.AssistantSettings startupSettings;
            using (new StartupDiagnostics("服务与设置"))
                startupSettings = Bootstrap.AppCompositionRoot.ApplicationSettingsStore.Load();
            UiTheme.SetMode(startupSettings.ThemeMode);
            AntdUI.Config.IsDark = UiTheme.Palette.IsDark;
            UiLanguage.SetMode(startupSettings.LanguageMode);
            using (new StartupDiagnostics("创建主窗口")) GameMain = new GameMain();
            // A replacement request may arrive while the main window is being created.
            GameMain.Shown += async (_, _) =>
            {
                if (Volatile.Read(ref replaceRequested) != 0)
                {
                    System.Windows.Forms.Application.Exit();
                    return;
                }
                // Shown 后再经过一次消息循环，确认主窗口初始化和绘制已经完成。
                GameMain.BeginInvoke(new Action(() =>
                {
                    processStartup.Dispose();
                    UpdateInstaller.ConfirmStartup();
                }));
                await AppUpdateChecker.CheckAsync(GameMain, Bootstrap.AppCompositionRoot.UpdateReleaseService, automatic: true);
            };
            if (Volatile.Read(ref replaceRequested) != 0)
            {
                GameMain.Dispose();
                Bootstrap.AppCompositionRoot.GameOverlay.Dispose();
                return;
            }
            UiLanguage.Start();

            Bootstrap.AppCompositionRoot.RankBorderLoading.Start();
            try { System.Windows.Forms.Application.Run(GameMain); }
            finally { Bootstrap.AppCompositionRoot.GameOverlay.Dispose(); }
        }

        // UI线程异常处理
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);

            GameMain?.infoMsg.AddMsg($"{e.Exception}");
        }

        // 非UI线程异常处理
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
                GameMain?.infoMsg.AddMsg($"{ex}");
            }
        }

        /// <summary>记录未处理异常，并提供用户可见的错误提示。</summary>
        private static void HandleException(Exception ex)
        {
            RuntimeDiagnostics.Report("应用", "未处理异常", ex.Message);
            try
            {
                // 用相对路径时，开机自启（注册表 Run 项没有工作目录，CWD 是 System32）
                // 会写失败；而这里本身就在异常处理路径上，再抛一次会直接把进程带崩。
                RuntimeDiagnostics.WriteException(ex);
            }
            catch
            {
                // 日志写不进去也不能影响后续处理
            }

            try
            {
                GameMain?.infoMsg.AddMsg($"{ex.Message}");
            }
            catch
            {
                // 日志窗口不可用时忽略
            }

            // 显示友好错误信息
            try
            {
                if (GameMain is { IsDisposed: false } main)
                    LOL_GameAssistant.Helper.UiMessage.error(main, $"程序发生错误: {ex.Message}\n请查看日志文件获取详细信息。");
            }
            catch
            {
                // 提示失败（例如窗口句柄已耗尽）时忽略，避免异常处理器自身再抛
            }
            // 可以选择是否退出应用
            // Application.Exit();
        }
    }
}
