using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.UIA3;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed class TclsAutomaticClientLauncher(LocalGameClientLauncher inner, Func<AssistantSettings> settings,
    ITclsAccountStore accounts) : IGameClientLauncher
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    public string NormalizeConfiguredDirectory(string? path) => inner.NormalizeConfiguredDirectory(path);
    public GameClientLaunchResult Start(string? path) => inner.Start(path);
    public void Cancel() { lock (_sync) _active?.Cancel(); }

    public async Task<GameClientLaunchResult> StartAndVerifyAsync(string? path, CancellationToken cancellationToken = default)
    {
        var config = settings();
        if (!config.TclsAutoLoginEnabled) return await inner.StartAndVerifyAsync(path, cancellationToken);
        if (!await _gate.WaitAsync(0, cancellationToken)) return new(false, "已有 TCLS 自动登录任务，请等待或取消后再操作。");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync) _active = lifetime;
        try
        {
            return await Task.Run(() => Run(path, config.TclsSelectedAccountId, lifetime.Token), lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            RuntimeDiagnostics.Report("TCLS 自动登录", "已取消", "已停止后续自动操作。");throw;
        }
        catch (Exception error)
        {
            // UI providers may echo submitted values in exception messages. Never log those messages.
            RuntimeDiagnostics.Report("TCLS 自动登录", "已暂停", $"自动化未完成，错误类型：{error.GetType().Name}。请检查账号设置或在登录器中手动完成。");
            return new(false, "TCLS 自动登录未完成，请检查账号是否保存完整、密码能否解密，或在登录器中手动完成。详情已记录日志。");
        }
        finally { lock (_sync) _active = null;_gate.Release(); }
    }

    private GameClientLaunchResult Run(string? path, string accountId, CancellationToken token)
    {
        var account = accounts.Load().SingleOrDefault(account => account.Id == accountId);
        if (account == null) return new(false, "请先在设置中添加并选择 TCLS 登录账号。");
        string? resolved = LocalGameClientLauncher.ResolveExecutable(path);
        string? executable = resolved == null ? null : GameClientExecutablePaths.PreferTencentLauncher(resolved);
        if (executable == null || !GameClientExecutablePaths.IsTencentLauncher(executable))
            return new(false, "所选安装目录没有 TCLS/client.exe，当前自动登录仅适配 TCLS，请选择正确的游戏安装目录。");
        if (LocalGameClientLauncher.IsClientReady(executable))
            return new(true, "LOL 客户端已经登录；为避免切换正在使用的账号，本次没有自动填写。", executable, true, true);
        string password = accounts.ReadPassword(account.Id);
        token.ThrowIfCancellationRequested();
        var started = inner.Start(executable);
        if (!started.Started) return started;
        if (started.IsWindowVisible)
            return started with { Message = "LOL 客户端已运行，本次不切换账号。请退出客户端后再使用所选账号登录。" };
        var session = new TclsLoginSession();
        using var renderedLogin = new TclsRenderedLoginAutomation();
        using var automation = new UIA3Automation { ConnectionTimeout = TimeSpan.FromSeconds(2), TransactionTimeout = TimeSpan.FromSeconds(2) };
        var clock = Stopwatch.StartNew();string previous = "";
        while (clock.Elapsed < TimeSpan.FromSeconds(90))
        {
            token.ThrowIfCancellationRequested();
            if (LocalGameClientLauncher.IsClientReady(executable))
            {
                RuntimeDiagnostics.Report("TCLS 自动登录", "已连接", "LOL 客户端 LCU 已就绪。账号与密码不写入日志。");
                return new(true, session.RegionSelected ? "客户端已连接，已在登录器中选择默认大区。"
                    : "客户端已连接，但本次未自动确认账号和大区，请在客户端核对。", executable, true, true);
            }
            var processes = Process.GetProcessesByName("client");
            try
            {
                string launcher = Path.Combine(Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName, "Launcher", "Client.exe");
                var roots = processes.Where(process => MatchesPath(process, executable) || MatchesPath(process, launcher)).SelectMany(process =>
                    automation.GetDesktop().FindAllChildren(factory => factory.ByProcessId(process.Id))).ToArray();
                var step = roots.Length == 0 ? new TclsLoginStep("等待 TCLS 登录窗口；如果出现新版启动器，请在该窗口手动登录。")
                    : TclsLoginAutomation.Step(roots, session, account.Username, password, account.Region, token);
                if (!step.Pause && roots.Length > 0)
                    step = renderedLogin.Step(roots, session, account.Username, password, token) ?? step;
                if (step.Message != previous)
                {
                    previous = step.Message;
                    RuntimeDiagnostics.Report("TCLS 自动登录", step.Pause ? "需手动处理" : "进行中", step.Message);
                }
                if (step.Pause) return new(true, step.Message, executable);
            }
            finally { foreach (var process in processes) process.Dispose(); }
            if (token.WaitHandle.WaitOne(700)) token.ThrowIfCancellationRequested();
        }
        string message = session.Submitted
            ? "TCLS 登录等待超时，已停止自动操作。请检查登录结果或验证码；未重复提交密码。"
            : "当前 TCLS 窗口未暴露可识别的账号密码控件，已停止自动操作。请手动登录，并反馈登录器版本以便继续适配。";
        RuntimeDiagnostics.Report("TCLS 自动登录", "已暂停", message);
        return new(true, message, executable);
    }

    private static bool MatchesPath(Process process, string executable)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) return false;
            var image = new StringBuilder(32768);int length = image.Capacity;
            return QueryFullProcessImageName(handle, 0, image, ref length) &&
                string.Equals(image.ToString(), executable, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
        finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder image, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
