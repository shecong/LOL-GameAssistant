using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LOL_Demo_Test
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            await GetlolInfoAsync();
        }

        public static async Task GetlolInfoAsync()
        {
            // 1. 获取LOL客户端进程信息
            var (port, token) = GetLcuCredentials();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                Console.WriteLine("未找到运行中的LOL客户端或无法获取认证信息");
                return;
            }

            Console.WriteLine($"找到LOL客户端 - 端口: {port}, 令牌: {token.Substring(0, Math.Min(10, token.Length))}...");
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = delegate { return true; };
            // 2. 创建HTTP客户端并设置认证头
            using (var client = new HttpClient(handler))
            {
                // 设置基础地址和超时
                client.BaseAddress = new Uri($"https://127.0.0.1:{port}/");
                client.Timeout = TimeSpan.FromSeconds(10);

                // 设置认证头
                client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes($"riot:{token}")));
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                // 3. 测试API调用 - 获取当前召唤师信息
                try
                {
                    Console.WriteLine("正在尝试连接LCU API...");
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
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"错误详情: {errorContent}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"HTTP请求错误: {ex.Message}");

                    // 检查是否是SSL错误
                    if (ex.InnerException?.Message.Contains("SSL") == true ||
                        ex.InnerException?.Message.Contains("certificate") == true)
                    {
                        Console.WriteLine("SSL证书验证失败。请确保：");
                        Console.WriteLine("1. 英雄联盟客户端正在运行");
                        Console.WriteLine("2. 您已以管理员权限运行此程序");
                        Console.WriteLine("3. 客户端使用的是有效的自签名证书");
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("请求超时，请检查LOL客户端是否正在运行");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发生未预期的错误: {ex.Message}");
                    Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                }
            }
        }

        private static (string port, string token) GetLcuCredentials()
        {
            try
            {
                // 尝试多种方式获取LOL客户端进程
                Process[] processes = Process.GetProcessesByName("LeagueClientUx");
                if (processes.Length == 0)
                {
                    Console.WriteLine("未找到LeagueClientUx进程");
                    return ("", "");
                }

                // 获取第一个匹配进程的命令行参数
                string commandLine = GetCommandLine(processes[0].Id);
                if (string.IsNullOrEmpty(commandLine))
                {
                    Console.WriteLine("无法获取进程命令行参数");
                    return ("", "");
                }

                // 从命令行参数中提取端口和令牌
                var portMatch = Regex.Match(commandLine, @"--app-port=(\d+)");
                var tokenMatch = Regex.Match(commandLine, @"--remoting-auth-token=([^\s""]+)");

                if (!portMatch.Success || !tokenMatch.Success)
                {
                    Console.WriteLine("无法从命令行参数中解析端口和令牌");
                    return ("", "");
                }

                return (portMatch.Groups[1].Value, tokenMatch.Groups[1].Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取认证信息时出错: {ex.Message}");
                return ("", "");
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

        // 忽略SSL证书验证（仅用于开发环境）
        private static void IgnoreCertificateValidation()
        {
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, certificate, chain, sslPolicyErrors) => true;

            Console.WriteLine("已禁用SSL证书验证（仅用于开发环境）");
        }
    }
}