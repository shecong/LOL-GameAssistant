using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Find only launchers belonging to the selected installation, without a UIA provider.</summary>
internal static class TclsLauncherWindows
{
    internal static string[] Executables(string executable)
    {
        if (!GameClientExecutablePaths.IsTencentLauncher(executable)) return [];
        string game = Directory.GetParent(Path.GetDirectoryName(executable)!)!.FullName;
        return [executable, Path.Combine(game, "Launcher", "Client.exe")];
    }

    internal static int[] ProcessIds(string executable)
    {
        var paths = Executables(executable);
        var processes = Process.GetProcessesByName("client");
        try { return processes.Where(process => paths.Any(path => MatchesPath(process, path))).Select(process => process.Id).ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static IntPtr[] Find(string executable)
    {
        var ids = ProcessIds(executable).ToHashSet();
        var windows = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out int pid);
            if (ids.Contains(pid) && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    internal static bool MatchesPath(Process process, string executable)
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

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder image, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
