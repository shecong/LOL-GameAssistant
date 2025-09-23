using System.Net.Http.Headers;
using System.Text;
using System.Web;

public class HttpClentHelper
{
    public static string? Port;
    public static string? Token;

    public async Task<string> SendRequestAsync(string httpMethod, string endpoint, Dictionary<string, string>? queryParams = null, string? body = null)
    {
        HttpClient _httpClient;
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = delegate { return true; };
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        if (string.IsNullOrEmpty(Port) || string.IsNullOrEmpty(Token))
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("未找到运行中的LOL客户端或无法获取认证信息"));
        }

        try
        {
            // 构建基础URL
            var baseUrl = $"https://127.0.0.1:{Port}";

            // 构建完整URL（包含查询参数）
            var requestUrl = BuildRequestUrl(baseUrl, endpoint, queryParams);

            // 每次请求时创建新的 HttpRequestMessage 实例
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

            HttpResponseMessage response;
            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(10);  // 设置10秒超时
                response = await _httpClient.SendAsync(request).ConfigureAwait(false); // 使用新的 HttpRequestMessage 实例
            }
            catch (Exception ex)
            {
                // 捕获并打印异常
                Console.WriteLine(ex.Message);
                return Convert.ToBase64String(Encoding.UTF8.GetBytes($"请求发送失败: {ex.Message}"));
            }

            if (response.IsSuccessStatusCode)
            {
                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                return Convert.ToBase64String(responseBytes);
            }
            else
            {
                var errorContent = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"请求失败: {response.StatusCode}");
                Console.WriteLine($"错误详情: {Encoding.UTF8.GetString(errorContent)}");
                return Convert.ToBase64String(Encoding.UTF8.GetBytes($"请求失败: {response.StatusCode} - {Encoding.UTF8.GetString(errorContent)}"));
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"请求异常: {ex.Message}");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"请求异常: {ex.Message}"));
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("请求超时，请检查LOL客户端是否正在运行");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("请求超时，请检查LOL客户端是否正在运行"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"未知错误: {ex.Message}");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"未知错误: {ex.Message}"));
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
    public Task<String> GetAsync(string endpoint, Dictionary<string, string>? queryParams = null)
    {
        return SendRequestAsync("GET", endpoint, queryParams);
    }

    public Task<String> PostAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null)
    {
        return SendRequestAsync("POST", endpoint, queryParams, body);
    }

    public Task<String> PutAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null)
    {
        return SendRequestAsync("PUT", endpoint, queryParams, body);
    }

    public Task<String> DeleteAsync(string endpoint, Dictionary<string, string>? queryParams = null)
    {
        return SendRequestAsync("DELETE", endpoint, queryParams);
    }
}