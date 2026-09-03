using System.Diagnostics;

namespace LOL_Demo_Test
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine("LOL-GameAssistant 控制台示例：检测 LCU 连接信息");
            var (port, token) = await GetLcuAuthAsync();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                Console.WriteLine("未检测到 LOL 客户端（LeagueClientUx/LeagueClient 进程）。");
                return;
            }

            Console.WriteLine($"LCU 端口: {port}");
            Console.WriteLine($"认证令牌: {token[..Math.Min(4, token.Length)]}...（已脱敏）");
            Console.WriteLine("调用示例：请求 https://127.0.0.1:{port}/lol-summoner/v1/current-summoner");
            Console.WriteLine("并携带请求头 Authorization: Basic base64(\"riot:{token}\")。");
        }

        /// <summary>
        /// 从 LOL 客户端 lockfile 读取 LCU 端口与令牌。
        /// </summary>
        private static async Task<(string? Port, string? Token)> GetLcuAuthAsync()
        {
            var processes = Process.GetProcessesByName("LeagueClientUx");
            if (processes.Length == 0)
                processes = Process.GetProcessesByName("LeagueClient");

            foreach (var process in processes)
            {
                try
                {
                    string dir = Path.GetDirectoryName(process.MainModule?.FileName) ?? "";
                    string lockfile = Path.Combine(dir, "lockfile");
                    if (!File.Exists(lockfile)) continue;

                    string content = (await File.ReadAllTextAsync(lockfile)).Trim();
                    // 格式: LeagueClient:port:token:protocol:PID
                    var parts = content.Split(':');
                    if (parts.Length >= 3)
                        return (parts[1], parts[2]);
                }
                catch
                {
                    // 单个进程读取失败时继续尝试下一个
                }
            }

            return (null, null);
        }
    }
}