using System.Diagnostics;
using System.Text;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;

namespace LOL_GameAssistant.Helper;

internal static class UpdateInstaller
{
    public static async Task LaunchAsync(PreparedUpdate update)
    {
        UpdatePackageService.RejectReparsePoints(update.Directory);
        string active = Path.Combine(Path.GetDirectoryName(update.Directory)!, "active-job.txt");
        if (File.Exists(active)) throw new IOException("另一个更新任务正在安装或等待恢复。");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset,
            "Local\\LOLGameAssistant.Update.Ready." + update.Token);
        await using (var marker = new FileStream(active, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            await marker.WriteAsync(Encoding.ASCII.GetBytes(update.Token));
            marker.Flush(flushToDisk: true);
        }
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(update.Directory, "runner", "Updater.exe"))
            {
                UseShellExecute = false, WorkingDirectory = update.Directory
            };
            start.ArgumentList.Add("--job");
            start.ArgumentList.Add(update.JobPath);
            process = Process.Start(start) ?? throw new IOException("无法启动更新器。");
            bool accepted = await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(15)));
            if (!accepted || process.HasExited) throw new IOException("更新器未就绪，当前程序将继续运行。请查看更新日志。");
        }
        catch
        {
            // 主程序尚未退出，更新器还不允许修改任何安装文件。停止迟到的任务，避免稍后意外安装。
            if (process is { HasExited: false })
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            if (File.Exists(active) && File.ReadAllText(active) == update.Token) File.Delete(active);
            throw;
        }
        finally { process?.Dispose(); }
    }

    public static void ConfirmStartup()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--update-confirm");
        if (index < 0 || index + 1 >= args.Length || !Guid.TryParseExact(args[index + 1], "N", out _)) return;
        try
        {
            using var ready = EventWaitHandle.OpenExisting("Local\\LOLGameAssistant.Update.Confirm." + args[index + 1]);
            ready.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    public static bool HasPendingRecovery()
    {
        string active = Path.Combine(AppContext.BaseDirectory, ".update", "active-job.txt");
        if (!File.Exists(active)) return false;
        string token;
        try { token = File.ReadAllText(active).Trim(); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--update-confirm");
        return index < 0 || index + 1 >= args.Length || args[index + 1] != token;
    }
}
