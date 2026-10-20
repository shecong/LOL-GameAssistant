using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>从本机客户端进程读取 LCU 端口和认证信息。</summary>
    public static class GetlolLcu
    {
        public static IInfoMsgForm? _infoMsgForm;
        private static readonly Infrastructure.LeagueClient.LcuCredentialProbe Probe = new(ScanAuth, timedOut: () =>
            Helper.RuntimeDiagnostics.Report("LCU 探测", "超时", "系统探测超过 3 秒，已停止等待；后续请求共用正在进行的探测，不重复创建线程"));

        public static Task<(string? port, string? token)> GetAuthAsync(CancellationToken cancellationToken = default) =>
            Probe.GetAsync(cancellationToken);

        /// <summary>
        /// 从 LCU lockfile 读取端口和 token（比 WMI 命令行解析更可靠）
        /// 格式: LeagueClient:PID:port:token:protocol
        /// </summary>
        public static (string? port, string? token) GetFromLockfile()
        {
            try
            {
                // Read process paths directly; a broken WMI service must not block the normal lockfile path.
                foreach (string name in new[] { "LeagueClientUx", "LeagueClient" })
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    try
                    {
                        foreach (var process in processes)
                        {
                            try
                            {
                                string? directory = Path.GetDirectoryName(process.MainModule?.FileName);
                                if (string.IsNullOrWhiteSpace(directory)) continue;
                                string file = Path.Combine(directory, "lockfile");
                                if (!File.Exists(file)) continue;
                                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                                using var reader = new StreamReader(stream);
                                if (stream.Length > 4096) continue;
                                if (Infrastructure.LeagueClient.LcuLockfileParser.TryParse(reader.ReadToEnd(), out var port, out var token))
                                    return (port, token);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                            { /* The process may exit or deny access; try the next candidate. */ }
                        }
                    }
                    finally { foreach (var process in processes) process.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"lockfile 读取失败: {ex.Message}");
            }
            return (null, null);
        }

        /// <summary>
        /// 从 LCU lockfile 读取端口和 token（优先），失败则回退到 WMI 命令行解析
        /// </summary>
        public static (string? port, string? token) GetAuth() => GetAuthAsync().GetAwaiter().GetResult();

        private static (string? port, string? token) ScanAuth()
        {
            var (port, token) = GetFromLockfile();
            if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                return (port, token);
            return GetlolLcuCmd();
        }

        /// <summary>通过客户端启动命令行提取 LCU 连接信息。</summary>
        public static (string? port, string? token) GetlolLcuCmd()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("LeagueClientUx");
                if (processes.Length == 0)
                {
                    processes = Process.GetProcessesByName("LeagueClient");
                    if (processes.Length == 0)
                    {
                        Console.WriteLine("未找到LeagueClientUx进程");
                        _infoMsgForm?.AddMsg("未找到LeagueClientUx进程");
                        return (null, null);
                    }
                }

                try
                {
                    foreach (var process in processes)
                    {
                        try
                        {
                            string commandLine = GetCommandLineUsingWmi(process.Id) ?? "";
                            var portMatch = Regex.Match(commandLine, @"--app-port=(\d+)");
                            var tokenMatch = Regex.Match(commandLine, @"--remoting-auth-token=([^\s""]+)");
                            if (portMatch.Success && tokenMatch.Success)
                                return (portMatch.Groups[1].Value, tokenMatch.Groups[1].Value);
                        }
                        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or InvalidOperationException)
                        { /* A stale process or unavailable provider must not skip other candidates. */ }
                    }
                }
                finally { foreach (var process in processes) process.Dispose(); }
                return (null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取认证信息时出错: {ex.Message}");
                _infoMsgForm?.AddMsg($"获取认证信息时出错: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>使用 WMI 读取目标进程的启动命令行。</summary>
        public static string? GetCommandLineUsingWmi(int processId)
        {
            string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}";
            using (var searcher = new ManagementObjectSearcher(query))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                searcher.Options.ReturnImmediately = false;
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    return obj["CommandLine"]?.ToString();
                }
            }
            return null;
        }
    }
}
