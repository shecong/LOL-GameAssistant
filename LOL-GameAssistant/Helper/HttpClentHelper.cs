using System.Net.Http.Headers;
using System.Text;
using System.Web;

public class HttpClentHelper : IDisposable
{
    public static string? Port;
    public static string? Token;

    // 使用静态 HttpClient 实例避免 Socket 耗尽
    private static readonly HttpClient _httpClient;

    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(20, 20); // 限制并发数

    static HttpClentHelper()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = delegate { return true; };
        handler.UseProxy = false;
        handler.AllowAutoRedirect = false;
        handler.MaxConnectionsPerServer = 50; // 增加连接数限制

        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.ConnectionClose = false; // 保持长连接
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

    // 便捷方法 - 添加 CancellationToken 支持
    public Task<Stream?> GetAsync(string endpoint, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("GET", endpoint, queryParams, null, cancellationToken);
    }

    public Task<Stream?> PostAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("POST", endpoint, queryParams, body, cancellationToken);
    }

    public Task<Stream?> PutAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("PUT", endpoint, queryParams, body, cancellationToken);
    }

    public Task<Stream?> DeleteAsync(string endpoint, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("DELETE", endpoint, queryParams, null, cancellationToken);
    }

    public async Task<Stream?> SendRequestStreamAsync(string httpMethod, string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(Port) || string.IsNullOrEmpty(Token))
        {
            return null;
        }

        // 使用信号量控制并发
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // 构建基础URL
            var baseUrl = $"https://127.0.0.1:{Port}";
            if (endpoint.StartsWith("http") || endpoint.StartsWith("https"))
            {
                baseUrl = "";
            }

            // 构建完整URL（包含查询参数）
            var requestUrl = BuildRequestUrl(baseUrl, endpoint, queryParams);

            // 创建 HttpRequestMessage
            var request = new HttpRequestMessage(new HttpMethod(httpMethod), requestUrl);

            // 设置认证头
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{Token}")));

            // 设置Accept头
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

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
                // 使用 HttpCompletionOption.ResponseHeadersRead 以流式处理响应
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("请求被取消");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"请求发送异常: {ex.Message}");
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"请求失败: {response.StatusCode}");
                Console.WriteLine($"错误详情: {errorContent}");
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"请求异常: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("请求超时，请检查LOL客户端是否正在运行");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"未知错误: {ex.Message}");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // 批量请求方法 - 新增功能
    public async Task<List<Stream?>> SendBatchRequestsAsync(
        List<(string method, string endpoint, Dictionary<string, string>? queryParams, string? body)> requests,
        int maxConcurrency = 5,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Stream?>();
        var tasks = new List<Task<Stream?>>();

        // 使用 SemaphoreSlim 控制并发数
        using var batchSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        foreach (var request in requests)
        {
            await batchSemaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    return await SendRequestStreamAsync(
                        request.method,
                        request.endpoint,
                        request.queryParams,
                        request.body,
                        cancellationToken);
                }
                finally
                {
                    batchSemaphore.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        // 等待所有任务完成
        var completedTasks = await Task.WhenAll(tasks);
        results.AddRange(completedTasks);

        return results;
    }

    public void Dispose()
    {
        // 静态 HttpClient 不需要手动释放，但可以实现 IDisposable 接口以保持模式一致
        // 如果需要释放资源，可以在这里添加
    }
}