using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.LeagueClient;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Web;

/// <summary>
/// LCU/HTTP 请求客户端封装：静态 HttpClient、Basic 认证、并发限制、批量请求。
/// </summary>
public class HttpClientHelper : IDisposable
{
    private static LeagueClientCredentials? _credentials;
    public static string? Port => Credentials?.Port;
    public static string? Token => Credentials?.Token;
    internal static LeagueClientCredentials? Credentials => Volatile.Read(ref _credentials);
    internal static void SetCredentials(LeagueClientCredentials credentials)
    {
        var previous = Interlocked.Exchange(ref _credentials, credentials);
        if (previous?.Port != credentials.Port || previous.Token != credentials.Token)
            LOL_GameAssistant.Application.Matches.PlayerHistorySession.SetClient(Guid.NewGuid().ToString("N"));
    }

    // 使用静态 HttpClient 实例避免 Socket 耗尽
    private static readonly HttpClient _httpClient;

    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(50, 50); // 限制并发数

    /// <summary>初始化 HttpClientHelper 使用的共享状态。</summary>
    static HttpClientHelper()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
            IsLcuUri(request.RequestUri) || errors == SslPolicyErrors.None;
        handler.UseProxy = false;
        handler.AllowAutoRedirect = false;
        handler.MaxConnectionsPerServer = 50; // 增加连接数限制

        _httpClient = new HttpClient(NetworkTrafficService.Handler(inner: handler));
        _httpClient.Timeout = LOL_GameAssistant.Application.Matches.MatchDataPolicy.TransportTimeout;
        _httpClient.DefaultRequestHeaders.ConnectionClose = false; // 保持长连接
    }

    /// <summary>组合请求地址与查询参数，并对查询参数进行编码。</summary>
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

    /// <summary>判断请求是否指向当前本机 LCU 端口。</summary>
    private static bool IsLcuUri(Uri? uri)
    {
        return uri?.Host == "127.0.0.1"
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && int.TryParse(Port, out int port)
            && uri.Port == port;
    }

    // 便捷方法 - 添加 CancellationToken 支持
    public Task<Stream?> GetAsync(string endpoint, Dictionary<string, string>? queryParams = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("GET", endpoint, queryParams, null, cancellationToken);
    }

    public async Task<Stream?> GetRequiredAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestResultAsync("GET", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Failure == LcuHttpFailure.None) return result.Content;
        result.Content?.Dispose();
        var failure = result.Failure switch
        {
            LcuHttpFailure.NoCredentials => LOL_GameAssistant.Application.LeagueClient.LcuFailure.NoCredentials,
            LcuHttpFailure.Timeout => LOL_GameAssistant.Application.LeagueClient.LcuFailure.Timeout,
            LcuHttpFailure.Network => LOL_GameAssistant.Application.LeagueClient.LcuFailure.Network,
            LcuHttpFailure.HttpStatus => LOL_GameAssistant.Application.LeagueClient.LcuFailure.HttpStatus,
            _ => LOL_GameAssistant.Application.LeagueClient.LcuFailure.Unexpected
        };
        throw new LOL_GameAssistant.Application.LeagueClient.LcuRequestException(failure, result.StatusCode);
    }

    /// <summary>发送 PATCH 请求，修改目标资源的部分字段。</summary>
    public Task<Stream?> PatchAsync(string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default)
    {
        return SendRequestStreamAsync("PATCH", endpoint, queryParams, body, cancellationToken);
    }

    /// <summary>发送请求并将响应内容作为流返回调用方。</summary>
    public async Task<Stream?> SendRequestStreamAsync(string httpMethod, string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default) =>
        (await SendRequestResultAsync(httpMethod, endpoint, queryParams, body, cancellationToken).ConfigureAwait(false)).Content;

    /// <summary>保留 HTTP 状态和失败类别，供需要区分 401/404/超时的调用方使用。</summary>
    public async Task<LcuHttpResult> SendRequestResultAsync(string httpMethod, string endpoint, Dictionary<string, string>? queryParams = null, string? body = null, CancellationToken cancellationToken = default)
    {
        bool external = Uri.TryCreate(endpoint, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https" && !IsLcuUri(absolute);
        if (!external && (string.IsNullOrEmpty(Port) || string.IsNullOrEmpty(Token)))
        {
            if (!await TryRefreshLcuCredentialsAsync(cancellationToken).ConfigureAwait(false))
            {
                RuntimeDiagnostics.Report("LCU HTTP", "未连接", "未读取到可用 lockfile 凭据");
                return new LcuHttpResult(null, null, LcuHttpFailure.NoCredentials);
            }
        }

        // 401 刷新后最多重试一次；重试必须先释放并发槽，避免并发请求互相等待。
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LeagueClientCredentials? credentials = Credentials;
                // 构建基础URL
                var baseUrl = $"https://127.0.0.1:{credentials?.Port}";
                if (endpoint.StartsWith("http") || endpoint.StartsWith("https"))
                {
                    baseUrl = "";
                }

                // 构建完整URL（包含查询参数）
                var requestUrl = BuildRequestUrl(baseUrl, endpoint, queryParams);

                // 创建 HttpRequestMessage
                using var request = new HttpRequestMessage(new HttpMethod(httpMethod), requestUrl);

                // LCU 凭据仅可发送给当前检测到的本机 LCU 端点。
                if (request.RequestUri?.Host == "127.0.0.1" &&
                    request.RequestUri.Scheme == Uri.UriSchemeHttps &&
                    int.TryParse(credentials?.Port, out int credentialPort) && request.RequestUri.Port == credentialPort)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{credentials!.Token}")));
                }

                // 设置Accept头
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                // 添加请求体（如果有）
                if (!string.IsNullOrEmpty(body) && httpMethod != "GET")
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response;
                try
                {
                    // 缓冲整个响应体（HttpClient 默认行为）：这样 HttpClient.Timeout 能覆盖到
                    // body 读取，不会出现"响应头已到、读 body 却无限期挂住"的情况。
                    using var transportDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    transportDeadline.CancelAfter(endpoint.StartsWith("/lol-match-history/", StringComparison.Ordinal)
                        ? LOL_GameAssistant.Application.Matches.MatchDataPolicy.TransportTimeout : TimeSpan.FromSeconds(30));
                    response = await _httpClient.SendAsync(request, transportDeadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return new LcuHttpResult(null, null, LcuHttpFailure.Timeout);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"请求发送异常: {ex.Message}");
                    return new LcuHttpResult(null, null, LcuHttpFailure.Network);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"请求失败: {response.StatusCode} {requestUrl}");
                        if (!external && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            string? previousToken = credentials?.Token;
                            if (attempt == 0 && await TryRefreshLcuCredentialsAsync(cancellationToken).ConfigureAwait(false) &&
                                !string.Equals(previousToken, Token, StringComparison.Ordinal))
                            {
                                RuntimeDiagnostics.Report("LCU HTTP", "已刷新", "客户端重启后已刷新本机 LCU 凭据");
                                continue;
                            }
                        }
                        RuntimeDiagnostics.Report("LCU HTTP", $"HTTP {(int)response.StatusCode}", $"{httpMethod} {endpoint}");
                        string serverError = "";
                        if (!external) {
                            // Error messages can contain account identifiers. Retain
                            // only a known category, never their raw body or token.
                            using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                            byte[] errorPrefix = new byte[8192];int used = 0;
                            while (used < errorPrefix.Length) {
                                int read = await errorStream.ReadAsync(errorPrefix.AsMemory(used), cancellationToken).ConfigureAwait(false);
                                if (read == 0)break;used += read;
                            }
                            serverError = ClassifyServerError(Encoding.UTF8.GetString(errorPrefix, 0, used));
                        }
                        return new LcuHttpResult(null, response.StatusCode, LcuHttpFailure.HttpStatus) { ServerErrorCode = serverError };
                    }

                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    RuntimeDiagnostics.Report(external ? "公网资源 HTTP" : "LCU HTTP", "可用", external ? "公网资源请求正常" : "本机 LCU 请求正常");
                    return new LcuHttpResult(new MemoryStream(bytes), response.StatusCode, LcuHttpFailure.None);
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"请求异常: {ex.Message}");
                RuntimeDiagnostics.Report("LCU HTTP", "连接失败", ex.Message);
                return new LcuHttpResult(null, null, LcuHttpFailure.Network);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("请求超时，请检查LOL客户端是否正在运行");
                RuntimeDiagnostics.Report("LCU HTTP", "超时", "请求超时，请检查客户端是否正在运行");
                return new LcuHttpResult(null, null, LcuHttpFailure.Timeout);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"未知错误: {ex.Message}");
                RuntimeDiagnostics.Report("LCU HTTP", "异常", ex.Message);
                return new LcuHttpResult(null, null, LcuHttpFailure.Unexpected);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        return new LcuHttpResult(null, HttpStatusCode.Unauthorized, LcuHttpFailure.HttpStatus);
    }

    internal static async Task<List<Stream?>> SendBatchCoreAsync(
        IEnumerable<Func<CancellationToken, Task<Stream?>>> operations, int maxConcurrency,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        var factories = operations.ToArray();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = factories.Select(async operation =>
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                return await operation(lifetime.Token).ConfigureAwait(false);
            }
            catch { lifetime.Cancel(); throw; }
            finally { gate.Release(); }
        }).ToArray();
        try
        {
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return results.ToList();
        }
        catch
        {
            foreach (var task in tasks)
                if (task.IsCompletedSuccessfully) task.Result?.Dispose();
            throw;
        }
    }

    /// <summary>保留共享 HTTP 客户端的生命周期，不随单次请求包装器释放。</summary>
    public void Dispose()
    {
        // 静态 HttpClient 不需要手动释放，但可以实现 IDisposable 接口以保持模式一致
        // 如果需要释放资源，可以在这里添加
    }

    /// <summary>尝试重新读取本机客户端连接凭据。</summary>
    internal static string ClassifyServerError(string text) => text.Contains("does not own", StringComparison.OrdinalIgnoreCase) ? "not_owned" : "";
    private static async Task<bool> TryRefreshLcuCredentialsAsync(CancellationToken cancellationToken)
    {
        (string? port, string? token) = await LOL_GameAssistant.LoLApi.GetlolLcu.GetAuthAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token)) return false;
        SetCredentials(new LeagueClientCredentials(port, token));
        return true;
    }
}

public enum LcuHttpFailure
{ None, NoCredentials, HttpStatus, Timeout, Network, Unexpected }

/// <summary>成功时 Content 归调用方释放；失败时通过 StatusCode 和 Failure 判断原因。</summary>
public sealed record LcuHttpResult(Stream? Content, HttpStatusCode? StatusCode, LcuHttpFailure Failure)
{
    public string ServerErrorCode { get; init; } = "";
}
