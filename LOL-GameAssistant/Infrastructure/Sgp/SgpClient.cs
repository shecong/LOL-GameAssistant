using LOL_GameAssistant.Helper;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Sgp;

public sealed record SgpContext(string Platform, Uri Server, string Session)
{
    public string? LoginPlatform { get; init; }
    public bool IsCrossRegion => LoginPlatform != null && !string.Equals(Platform, LoginPlatform, StringComparison.OrdinalIgnoreCase);
}

/// <summary>SGP 认证仅从本机读取，不持久化、不跟随重定向、不禁用远端证书验证。</summary>
public sealed class SgpClient
{
    private readonly ILcuRequestSender _lcu;
    private readonly HttpClient _http;
    private readonly IReadOnlyDictionary<string, string> _servers;
    private readonly string? _targetPlatform;
    private readonly SemaphoreSlim _gate = new(6);
    private static readonly HttpClient SharedHttp = new(NetworkTrafficService.Handler(NetworkModule.Ranked, new HttpClientHandler { AllowAutoRedirect = false }))
        { Timeout = TimeSpan.FromSeconds(25) };

    // Only captured/verified addresses. Do not synthesize hosts from realm names.
    private static readonly IReadOnlyDictionary<string, string> BuiltinServers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HN1"] = "https://hn1-k8s-sgp.lol.qq.com:21019",
        ["HN10"] = "https://hn10-k8s-sgp.lol.qq.com:21019",
        ["CQ100"] = "https://cq100-sgp.lol.qq.com:21019"
    };

    public SgpClient(ILcuRequestSender lcu, HttpClient? http = null, IReadOnlyDictionary<string, string>? servers = null, string? targetPlatform = null)
    { _lcu = lcu; _http = http ?? SharedHttp; _servers = servers ?? BuiltinServers; _targetPlatform = targetPlatform?.Trim().ToUpperInvariant(); }

    public async Task<SgpContext> GetContextAsync(CancellationToken token)
    {
        string session = PlayerHistorySession.Key;
        var raw = await _lcu.GetStringAsync("/lol-chat/v1/me", token).ConfigureAwait(false);
        string? platform = string.IsNullOrWhiteSpace(raw) ? null : JObject.Parse(raw).Value<string>("platformId");
        if (string.IsNullOrWhiteSpace(platform))
        {
            raw = await _lcu.GetStringAsync("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", token).ConfigureAwait(false);
            platform = string.IsNullOrWhiteSpace(raw) ? null : (JToken.Parse(raw) as JValue)?.Value<string>();
        }
        platform = platform?.Trim().ToUpperInvariant();
        string? loginPlatform = platform;
        raw = await _lcu.GetStringAsync("/lol-platform-config/v1/namespaces/PlayerPreferences/ServiceEndpoint", token).ConfigureAwait(false);
        string? address = string.IsNullOrWhiteSpace(raw) ? null : (JToken.Parse(raw) as JValue)?.Value<string>();
        if (!string.IsNullOrWhiteSpace(_targetPlatform) && !string.Equals(_targetPlatform, platform, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(loginPlatform)) throw new IOException("尚未确认当前客户端登录大区。");
            if (!loginPlatform.StartsWith("HN", StringComparison.Ordinal) && !loginPlatform.StartsWith("CQ", StringComparison.Ordinal) &&
                !loginPlatform.StartsWith("BGP", StringComparison.Ordinal) && !loginPlatform.StartsWith("WT", StringComparison.Ordinal) && !loginPlatform.StartsWith("EDU", StringComparison.Ordinal))
                throw new NotSupportedException("当前仅开放腾讯服范围内的跨区战绩查询。");
            platform = _targetPlatform;
            _servers.TryGetValue(platform, out address);
        }
        if (string.IsNullOrWhiteSpace(address) && platform != null) _servers.TryGetValue(platform, out address);
        if (platform == null || !System.Text.RegularExpressions.Regex.IsMatch(platform, "^[A-Z0-9_-]{2,24}$") || string.IsNullOrWhiteSpace(address))
            throw new NotSupportedException($"当前大区 {platform ?? "未知"} 尚无经过验证的 SGP 地址，不能读取云端战绩。");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var server) || !IsTrustedServer(server))
            throw new InvalidOperationException("SGP 地址必须是受信任的 Riot/腾讯 HTTPS 服务。");
        if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
        return new(platform, server, session) { LoginPlatform = loginPlatform };
    }

    public async Task<JToken> ReadAsync(SgpContext context, string path, bool leagueSession, CancellationToken token,
        JToken? queryBody = null)
    {
        if (!IsTrustedServer(context.Server)) throw new InvalidOperationException("SGP 地址不受信任。");
        if (context.IsCrossRegion && leagueSession) throw new NotSupportedException("跨区仅支持战绩，排位和玩家资料接口不可跨区使用。");
        if (!path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\')) throw new ArgumentException("无效 SGP 路径。", nameof(path));
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (context.Session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
                string route = leagueSession ? "/lol-league-session/v1/league-session-token" : "/entitlements/v1/token";
                string? raw = await _lcu.GetStringAsync(route, token).ConfigureAwait(false);
                JToken? credential = string.IsNullOrWhiteSpace(raw) ? null : JToken.Parse(raw);
                string? bearer = leagueSession ? (credential as JValue)?.Value<string>() : credential?.Value<string>("accessToken");
                if (string.IsNullOrWhiteSpace(bearer)) throw new IOException("无法从当前客户端取得 SGP 登录凭据，请确认已登录。");
                if (context.Session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
                using var request = new HttpRequestMessage(queryBody == null ? HttpMethod.Get : HttpMethod.Post, new Uri(context.Server, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (queryBody != null) request.Content = new StringContent(queryBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"SGP 请求失败（HTTP {(int)response.StatusCode}）。", null, response.StatusCode);
                string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (context.Session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
                return JToken.Parse(json);
            }
            throw new IOException("SGP 登录凭据已失效。");
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && context.Session == PlayerHistorySession.Key)
        { throw new IOException("SGP 请求超时。", error); }
        finally { _gate.Release(); }
    }

    private static bool IsTrustedServer(Uri server) => server.IsAbsoluteUri && server.Scheme == "https" &&
        server.UserInfo.Length == 0 && server.AbsolutePath == "/" && server.Query.Length == 0 && server.Fragment.Length == 0 &&
        server.Host.Contains("sgp", StringComparison.OrdinalIgnoreCase) &&
        (server.Host.EndsWith(".lol.qq.com", StringComparison.OrdinalIgnoreCase) || server.Host.EndsWith(".riotgames.com", StringComparison.OrdinalIgnoreCase));
}
