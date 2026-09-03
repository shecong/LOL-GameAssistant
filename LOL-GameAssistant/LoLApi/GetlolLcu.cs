using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant.LoLApi
{
    public static class GetlolLcu
    {
        public static IInfoMsgForm? _infoMsgForm;

        /// <summary>
        /// 从 LCU lockfile 读取端口和 token（比 WMI 命令行解析更可靠）
        /// 格式: LeagueClient:port:token:protocol:PID
        /// </summary>
        public static (string? port, string? token) GetFromLockfile()
        {
            try
            {
                // 通过 WMI 获取进程路径
                string query = "SELECT ExecutablePath, ProcessId FROM Win32_Process WHERE Name = 'LeagueClientUx.exe' OR Name = 'LeagueClient.exe'";
                using var searcher = new ManagementObjectSearcher(query);
                using var results = searcher.Get();

                foreach (ManagementObject obj in results)
                {
                    string exePath = obj["ExecutablePath"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(exePath)) continue;

                    string dir = Path.GetDirectoryName(exePath) ?? "";
                    string lockfile = Path.Combine(dir, "lockfile");
                    if (!File.Exists(lockfile)) continue;

                    string content = File.ReadAllText(lockfile).Trim();
                    var parts = content.Split(':');
                    // Format: LeagueClient:port:token:protocol:PID
                    if (parts.Length >= 4)
                    {
                        string? port = parts[1];
                        string? token = parts[2];
                        if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                        {
                            _infoMsgForm?.AddMsg($"lockfile 连接成功: {port}");
                            return (port, token);
                        }
                    }
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
        public static (string? port, string? token) GetAuth()
        {
            var (port, token) = GetFromLockfile();
            if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                return (port, token);
            return GetlolLcuCmd();
        }

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

                for (int i = 0; i < processes.Length; i++)
                {
                    string commandLine = GetCommandLineUsingWmi(processes[i].Id) ?? "";
                    if (string.IsNullOrEmpty(commandLine))
                    {
                        Console.WriteLine("无法获取进程命令行参数");
                        _infoMsgForm?.AddMsg("无法获取进程命令行参数");
                        return (null, null);
                    }

                    var portMatch = Regex.Match(commandLine, @"--app-port=(\d+)");
                    var tokenMatch = Regex.Match(commandLine, @"--remoting-auth-token=([^\s""]+)");

                    if (!portMatch.Success || !tokenMatch.Success)
                    {
                        Console.WriteLine("无法从命令行参数中解析端口和令牌");
                        _infoMsgForm?.AddMsg("无法从命令行参数中解析端口和令牌");
                        return (null, null);
                    }
                    if (portMatch != null && tokenMatch != null)
                    {
                        return (portMatch.Groups[1].Value, tokenMatch.Groups[1].Value);
                    }
                }
                return (null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取认证信息时出错: {ex.Message}");
                _infoMsgForm?.AddMsg($"获取认证信息时出错: {ex.Message}");
                return (null, null);
            }
        }

        public static string? GetCommandLineUsingWmi(int processId)
        {
            string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject obj in results)
                {
                    return obj["CommandLine"]?.ToString();
                }
            }
            return null;
        }
    }
}
