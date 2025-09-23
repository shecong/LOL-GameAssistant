using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.LoLApi
{
    public static class GetlolLcu
    {
        public static (string? port, string? token) GetlolLcuCmd()
        {
            try
            {
                // 尝试多种方式获取LOL客户端进程
                Process[] processes = Process.GetProcessesByName("LeagueClientUx");
                if (processes.Length == 0)
                {
                    Console.WriteLine("未找到LeagueClientUx进程");
                    return (null, null);
                }

                // 获取第一个匹配进程的命令行参数
                string commandLine = GetCommandLine(processes[0].Id) ?? "";
                if (string.IsNullOrEmpty(commandLine))
                {
                    Console.WriteLine("无法获取进程命令行参数");
                    return (null, null);
                }

                // 从命令行参数中提取端口和令牌
                var portMatch = Regex.Match(commandLine, @"--app-port=(\d+)");
                var tokenMatch = Regex.Match(commandLine, @"--remoting-auth-token=([^\s""]+)");

                if (!portMatch.Success || !tokenMatch.Success)
                {
                    Console.WriteLine("无法从命令行参数中解析端口和令牌");
                    return (null, null);
                }

                return (portMatch.Groups[1].Value, tokenMatch.Groups[1].Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取认证信息时出错: {ex.Message}");
                return (null, null);
            }
        }

        // 获取进程命令行参数的辅助方法
        private static string? GetCommandLine(int processId)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "wmic";
                    process.StartInfo.Arguments = $"process where ProcessId={processId} get CommandLine /value";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // 解析输出获取命令行
                    var match = Regex.Match(output, @"CommandLine=([^\r\n]+)");
                    return match.Success ? match.Groups[1].Value : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}