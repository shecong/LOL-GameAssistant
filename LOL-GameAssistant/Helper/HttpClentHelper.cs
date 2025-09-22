using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

public class HttpClentHelper
{
    public static string? Port;
    public static string? Token;
    private static readonly HttpClient _httpClient;

    static HttpClentHelper()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = delegate { return true; };

        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<string> SendRequestAsync(
        string httpMethod,
        string endpoint,
        Dictionary<string, string>? queryParams = null,
        string? body = null)
    {
        if (string.IsNullOrEmpty(Port) || string.IsNullOrEmpty(Token))
        {
            return "未找到运行中的LOL客户端或无法获取认证信息";
        }

        try
        {
            // 构建基础URL
            var baseUrl = $"https://127.0.0.1:{Port}";

            // 构建完整URL（包含查询参数）
            var requestUrl = BuildRequestUrl(baseUrl, endpoint, queryParams);

            // 创建请求消息
            var request = new HttpRequestMessage(new HttpMethod(httpMethod), requestUrl);

            // 设置认证头
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{Token}")));

            // 设置Accept头
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // 添加请求体（如果有）
            if (!string.IsNullOrEmpty(body) && httpMethod != "GET")
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            // 发送请求
            Console.WriteLine($"正在发送 {httpMethod} 请求到: {requestUrl}");
            if (!string.IsNullOrEmpty(body))
            {
                Console.WriteLine($"请求体: {body}");
            }

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"请求成功，响应内容: {content}");
                return content;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"请求失败: {response.StatusCode}");
                Console.WriteLine($"错误详情: {errorContent}");
                return $"请求失败: {response.StatusCode} - {errorContent}";
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"请求异常: {ex.Message}");
            return $"请求异常: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("请求超时，请检查LOL客户端是否正在运行");
            return "请求超时，请检查LOL客户端是否正在运行";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"未知错误: {ex.Message}");
            return $"未知错误: {ex.Message}";
        }
    }

    private string BuildRequestUrl(string baseUrl, string endpoint, Dictionary<string, string>? queryParams)
    {
        var url = $"{baseUrl}{endpoint}";

        if (queryParams != null && queryParams.Count > 0)
        {
            var queryString = HttpUtility.ParseQueryString(string.Empty);
            foreach (var param in queryParams)
            {
                queryString[param.Key] = param.Value;
            }
            url += $"?{queryString}";
        }

        return url;
    }

    // 便捷方法
    public Task<string> GetAsync(string endpoint, Dictionary<string, string>? queryParams = null)
    {
        return SendRequestAsync("GET", endpoint, queryParams);
    }

    public Task<string> PostAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null)
    {
        return SendRequestAsync("POST", endpoint, queryParams, body);
    }

    public Task<string> PutAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null)
    {
        return SendRequestAsync("PUT", endpoint, queryParams, body);
    }

    public Task<string> DeleteAsync(string endpoint, Dictionary<string, string>? queryParams = null)
    {
        return SendRequestAsync("DELETE", endpoint, queryParams);
    }
}
