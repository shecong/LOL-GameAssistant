using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LOL_Demo_Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
           await GetlolInfoAsync();
        }

        // 将 GetlolInfoAsync 方法声明为 static，以便可以从 Main 静态方法中调用
        public static async Task GetlolInfoAsync()
        {
            // 1. 获取LOL客户端进程信息
            var (port, token) = GetLcuCredentials();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                Console.WriteLine("未找到运行中的LOL客户端或无法获取认证信息");
                return;
            }

            // 2. 创建HTTP客户端并设置认证头
            using (var client = new HttpClient())
            {
                // 忽略SSL证书验证（仅用于开发环境）
                client.BaseAddress = new Uri($"https://127.0.0.1:{port}/");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"riot:{token}")));
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                // 3. 测试API调用 - 获取当前召唤师信息
                try
                {
                    var response = await client.GetAsync("lol-summoner/v1/current-summoner");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        Console.WriteLine("认证成功！当前召唤师信息：");
                        Console.WriteLine(content);
                    }
                    else
                    {
                        Console.WriteLine($"API调用失败: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发生错误: {ex.Message}");
                }
            }

        }
        static (string port, string token) GetLcuCredentials()
        {
            try
            {
                // 使用WMIC命令获取LOL客户端命令行参数
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = "PROCESS WHERE name='LeagueClientUx.exe' GET commandline",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // 从输出中提取端口和令牌
                var portMatch = Regex.Match(output, @"--app-port=(\d+)");
                var tokenMatch = Regex.Match(output, @"--remoting-auth-token=([^\s""]+)");

                return (portMatch.Success ? portMatch.Groups[1].Value : null,
                        tokenMatch.Success ? tokenMatch.Groups[1].Value : null);
            }
            catch
            {
                return (null, null);
            }
        }
    }
}
