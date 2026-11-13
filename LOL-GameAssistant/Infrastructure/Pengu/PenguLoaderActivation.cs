using Microsoft.Win32;

namespace LOL_GameAssistant.Infrastructure.Pengu;

public sealed record PenguLoaderActivation(bool? Activated, string Message)
{
    internal static PenguLoaderActivation Read(string loaderDirectory)
    {
        if (string.IsNullOrEmpty(loaderDirectory)) return new(null, "尚未选择 Loader 目录");
        try
        {
            string core = Path.Combine(loaderDirectory, "core.dll");
            if (!File.Exists(core)) return new(false, "未激活（缺少 core.dll）");
            string config = Path.Combine(loaderDirectory, "config");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(config) && new FileInfo(config).Length <= 65536)
                foreach (string line in File.ReadLines(config))
                {
                    string[] parts = line.Split('=', 2);
                    if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
                }
            if (values.TryGetValue("UseSymlink", out var mode) && (mode.Equals("true", StringComparison.OrdinalIgnoreCase) || mode == "1"))
            {
                if (!values.TryGetValue("LeaguePath", out var path) || string.IsNullOrWhiteSpace(path)) return new(false, "未激活（本地模式没有配置游戏目录）");
                var link = new FileInfo(Path.Combine(path, "version.dll"));
                var target = link.Exists ? link.ResolveLinkTarget(returnFinalTarget: true) : null;
                return target != null && Path.GetFullPath(target.FullName).Equals(Path.GetFullPath(core), StringComparison.OrdinalIgnoreCase)
                    ? new(true, "已激活（本地模式）") : new(false, "未激活（本地模式的 version.dll 未指向当前 Loader）");
            }
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\LeagueClientUx.exe");
                string? debugger = key?.GetValue("Debugger") as string;
                if (MatchesDebugger(debugger, core)) return new(true, "已激活（全局模式）");
                if (!string.IsNullOrWhiteSpace(debugger)) return new(null, "全局加载入口指向其他程序或另一个 Loader，请在 Loader 中检查");
            }
            return new(false, "未激活（Active Status 为 OFF）");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { return new(null, "无法读取 Loader 激活配置，请在 Loader 窗口查看 Active Status"); }
    }

    internal static bool MatchesDebugger(string? value, string core) =>
        string.Equals(value?.Trim(), $"rundll32 \"{core}\", #6000", StringComparison.OrdinalIgnoreCase);
}
