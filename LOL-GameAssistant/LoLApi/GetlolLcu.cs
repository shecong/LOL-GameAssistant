using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using static LOL_GameAssistant.BaseViewForm.InfoMsgForm;

namespace LOL_GameAssistant.LoLApi
{
    public static class GetlolLcu
    {
        public static IInfoMsgForm? _infoMsgForm;

        public static (string? port, string? token) GetlolLcuCmd()
        {
            try
            {
                // 尝试多种方式获取LOL客户端进程
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

                // 获取第一个匹配进程的命令行参数
                for (int i = 0; i < processes.Length; i++)
                {
                    string commandLine = GetCommandLineUsingWmi(processes[i].Id) ?? "";
                    if (string.IsNullOrEmpty(commandLine))
                    {
                        Console.WriteLine("无法获取进程命令行参数");
                        _infoMsgForm?.AddMsg("无法获取进程命令行参数");
                        return (null, null);
                    }

                    // 从命令行参数中提取端口和令牌
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
            {
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        return obj["CommandLine"]?.ToString();
                    }
                }
            }
            return null;
        }
    }
}