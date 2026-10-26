using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Failure-only metadata inspection. Does not install hooks or retry game commands.</summary>
internal static class SkinCoreTargetDiagnostics
{
    internal static SkinCoreTargetProbe Capture(SkinCoreFailureDetails details)
    {
        var errors = new List<string>();
        var result = new SkinCoreTargetProbe();
        if (!OperatingSystem.IsWindows() || details.Pid <= 0)
            return result with { Errors = ["target_process_unavailable"], Assessment = "缺少目标进程信息，无法自动检查。" };
        try
        {
            using var target = Process.GetProcessById(details.Pid);
            result = result with { TargetRunning = !target.HasExited };
            if (result.TargetRunning == true)
            {
                using var assistant = Process.GetCurrentProcess();
                result = result with { AssistantElevated = ReadElevation(assistant.Id, "assistant", errors),
                    TargetElevated = ReadElevation(target.Id, "target", errors) };
                IntPtr process = OpenProcess(0x1000, false, target.Id); // QUERY_LIMITED_INFORMATION
                if (process == IntPtr.Zero) errors.Add($"target_query:Win32={Marshal.GetLastWin32Error()}");
                else
                {
                    try
                    {
                        if (IsWow64Process2(process, out ushort machine, out ushort native))
                            result = result with { TargetArchitecture = Architecture(machine == 0 ? native : machine) };
                        else errors.Add($"target_architecture:Win32={Marshal.GetLastWin32Error()}");
                    }
                    catch (EntryPointNotFoundException) { errors.Add("target_architecture:api_unavailable"); }
                    finally { CloseHandle(process); }
                }
                IntPtr window = IntPtr.Zero;
                EnumWindows((candidate, _) =>
                {
                    uint thread = GetWindowThreadProcessId(candidate, out uint pid);
                    if (pid != details.Pid || thread != details.ThreadId) return true;
                    window = candidate;
                    return false;
                }, IntPtr.Zero);
                if (window != IntPtr.Zero)
                    result = result with { WindowHung = IsHungAppWindow(window) };
                else errors.Add("target_window_changed_or_missing");
                try
                {
                    // The host has already removed its hook. Absence now does not prove the DLL never loaded.
                    bool loaded = target.Modules.Cast<ProcessModule>().Any(module =>
                        string.Equals(module.ModuleName, "league-skin-core.dll", StringComparison.OrdinalIgnoreCase));
                    result = result with { CoreModuleLoadedAfterCleanup = loaded };
                }
                catch (Win32Exception ex) { errors.Add($"target_modules:Win32={ex.NativeErrorCode}"); }
                catch (InvalidOperationException) { errors.Add("target_modules:process_exited"); }
            }
        }
        catch (ArgumentException) { result = result with { TargetRunning = false }; }
        catch (Win32Exception ex) { errors.Add($"target_process:Win32={ex.NativeErrorCode}"); }
        catch (InvalidOperationException) { errors.Add("target_process:state_unavailable"); }
        return result with { Errors = errors.ToArray(), Assessment = Assess(result, details.HostArchitecture, details.Phase) };
    }

    internal static string Assess(SkinCoreTargetProbe probe, string hostArchitecture, int? phase)
    {
        if (probe.TargetRunning == false) return "检查时游戏进程已退出，请进入对局后重新读取。";
        if (probe.TargetArchitecture.Length > 0 && hostArchitecture.Length > 0 &&
            !probe.TargetArchitecture.Equals(hostArchitecture, StringComparison.OrdinalIgnoreCase))
            return "游戏与核心宿主位数不一致，请使用与游戏位数一致的完整核心运行包。";
        if (probe.WindowHung == true) return "Windows 检测到游戏窗口未响应，请待游戏恢复后手动读取。";
        if (probe.TargetElevated == true && probe.AssistantElevated == false)
            return "游戏已提权、助手未提权，请保持两者权限级别一致后重新读取。";
        return phase == 0
            ? "游戏侧未接手请求；现有证据不能区分消息未处理、核心加载异常或环境限制。清理后模块未加载不代表此前从未加载。"
            : "自动检查尚未确认根因；模块状态为清理后的观察值，请结合原始通信阶段判断。";
    }

    private static string Architecture(ushort machine) => machine switch
    {
        0x014c => "x86", 0x8664 => "x64", 0xaa64 => "arm64", _ => $"machine_0x{machine:x4}"
    };

    private static bool? ReadElevation(int pid, string label, List<string> errors)
    {
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) { errors.Add($"{label}_token_process:Win32={Marshal.GetLastWin32Error()}"); return null; }
        try
        {
            if (!OpenProcessToken(process, 8, out IntPtr token))
            { errors.Add($"{label}_token:Win32={Marshal.GetLastWin32Error()}"); return null; }
            try
            {
                if (GetTokenInformation(token, 20, out int elevated, sizeof(int), out _)) return elevated != 0;
                errors.Add($"{label}_elevation:Win32={Marshal.GetLastWin32Error()}"); return null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process2(IntPtr process, out ushort machine, out ushort native);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int type, out int value, int size, out int returned);
    private delegate bool WindowCallback(IntPtr window, IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
}
