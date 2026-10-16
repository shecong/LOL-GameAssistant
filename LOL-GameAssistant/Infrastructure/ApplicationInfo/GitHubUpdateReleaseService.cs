using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.ApplicationInfo;

/// <summary>通过 GitHub Releases 查询最新稳定发布版本。</summary>
public sealed class GitHubUpdateReleaseService : IUpdateReleaseService
{
    private const string ReleasesUrl = "https://github.com/shecong/LOL-GameAssistant/releases";
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;
    private UpdateRelease? _cachedRelease;
    private DateTimeOffset _cacheExpires;

    public GitHubUpdateReleaseService() : this(SharedClient) { }

    internal GitHubUpdateReleaseService(HttpClient client) => _client = client;

    /// <summary>查询最新发布版本信息。</summary>
    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cachedRelease != null && DateTimeOffset.UtcNow < _cacheExpires) return _cachedRelease;

        UpdateRelease? release;
        try
        {
            release = await GetFromApiAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // 公共出口 IP 的匿名 API 配额可能被其他用户耗尽，改用网页重定向查询。
            release = await GetFromReleasePageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // API 超时也可尝试网页；调用方主动取消时不发起额外请求。
            release = await GetFromReleasePageAsync(cancellationToken).ConfigureAwait(false);
        }

        if (release != null)
        {
            _cachedRelease = release;
            _cacheExpires = DateTimeOffset.UtcNow.AddMinutes(5);
        }
        return release;
    }

    private async Task<UpdateRelease?> GetFromApiAsync(CancellationToken cancellationToken)
    {
        string json = await _client.GetStringAsync(
            "https://api.github.com/repos/shecong/LOL-GameAssistant/releases/latest",
            cancellationToken).ConfigureAwait(false);
        var data = JObject.Parse(json);
        string? tagName = data["tag_name"]?.ToString();
        if (string.IsNullOrWhiteSpace(tagName)) return null;

        string versionText = tagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out Version? version)) return null;

        return new UpdateRelease(
            version,
            tagName,
            data["html_url"]?.ToString() ?? "https://github.com/shecong/LOL-GameAssistant/releases",
            data["body"]?.ToString() ?? "");
    }

    private async Task<UpdateRelease?> GetFromReleasePageAsync(CancellationToken cancellationToken)
    {
        // HttpClient 自动跟随 /latest 到 /tag/{tag}，无需解析 HTML 或调用 GitHub API。
        using var response = await _client.GetAsync(ReleasesUrl + "/latest",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        Uri? finalUrl = response.RequestMessage?.RequestUri;
        const string tagPath = "/shecong/LOL-GameAssistant/releases/tag/";
        if (finalUrl == null || finalUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(finalUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !finalUrl.AbsolutePath.StartsWith(tagPath, StringComparison.Ordinal))
            return null;

        string tagName = Uri.UnescapeDataString(finalUrl.AbsolutePath[tagPath.Length..]);
        if (!Version.TryParse(tagName.TrimStart('v', 'V'), out Version? version)) return null;
        return new UpdateRelease(version, tagName, finalUrl.AbsoluteUri, "");
    }

    /// <summary>创建当前服务使用的 HTTP 客户端及请求配置。</summary>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "LOL-GameAssistant");
        return client;
    }
}
