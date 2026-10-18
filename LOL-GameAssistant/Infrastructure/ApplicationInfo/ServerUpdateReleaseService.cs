using System.Text.Json;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Infrastructure.ApplicationInfo;

/// <summary>可选静态服务器优先，检查失败时沿用 GitHub。下载地址固定到版本目录。</summary>
internal sealed class ServerUpdateReleaseService : IUpdateReleaseService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _client;
    private readonly IUpdateReleaseService _github;
    private readonly string _configPath;

    public ServerUpdateReleaseService() : this(SharedClient, new GitHubUpdateReleaseService(),
        Path.Combine(AppContext.BaseDirectory, "update-source.json")) { }

    internal ServerUpdateReleaseService(HttpClient client, IUpdateReleaseService github, string configPath)
    {
        _client = client;
        _github = github;
        _configPath = configPath;
    }

    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Uri? server = null;
        bool allowHttp = false;
        try
        {
            if (File.Exists(_configPath))
            {
                if (new FileInfo(_configPath).Length > 64 * 1024) throw new InvalidDataException("下载源配置过大。");
                using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath, cancellationToken).ConfigureAwait(false));
                string? address = settings.RootElement.GetProperty("serverBaseUrl").GetString();
                allowHttp = settings.RootElement.TryGetProperty("allowHttp", out var httpSetting) && httpSetting.GetBoolean();
                if (!string.IsNullOrWhiteSpace(address))
                {
                    server = new Uri(address.TrimEnd('/') + "/", UriKind.Absolute);
                    ValidateServerUrl(server, allowHttp);
                }
            }
            if (server != null)
            {
                using var response = await _client.GetAsync(new Uri(server, "update.json"),
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                byte[] bytes = await UpdatePackageService.ReadLimitedAsync(response.Content, 64 * 1024, cancellationToken).ConfigureAwait(false);
                var config = JsonSerializer.Deserialize<UpdateManifest>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("服务器更新配置为空。");
                if (!Version.TryParse(config.Version, out var version)) throw new InvalidDataException("服务器版本号无效。");
                var release = CreateRelease(version);
                UpdatePackageService.ValidateManifest(config, release, requireNewer: false);
                return release with { ServerManifestUrl = VersionManifest(server, version), AllowHttpServer = allowHttp };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException
            or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            // 无效的本地设置不能阻止程序启动，也不能禁用原有 GitHub 检查。
            Helper.RuntimeDiagnostics.Report("软件更新", "服务器检查失败，尝试 GitHub", ex.Message);
            if (server != null)
            {
                try { ValidateServerUrl(server, allowHttp); }
                catch (InvalidDataException) { server = null; }
            }
        }
        var fallback = await _github.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        return fallback == null || server == null ? fallback
            : fallback with { ServerManifestUrl = VersionManifest(server, fallback.Version), PreferServerDownload = false, AllowHttpServer = allowHttp };
    }

    private static UpdateRelease CreateRelease(Version version) => new(version, "v" + version,
        "https://github.com/shecong/LOL-GameAssistant/releases", "");

    private static Uri VersionManifest(Uri server, Version version) =>
        new(server, "v" + (version.Revision > 0 ? version.ToString()
            : new Version(version.Major, version.Minor, Math.Max(0, version.Build)).ToString(3)) + "/update.json");

    internal static void ValidateServerUrl(Uri url, bool allowHttp = false)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps &&
            !(url.Scheme == Uri.UriSchemeHttp && (url.IsLoopback || allowHttp))) ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw new InvalidDataException("服务器地址必须是 HTTPS 地址；HTTP 需显式设置 allowHttp。本机测试允许 HTTP。地址不能包含账号、查询参数或片段。");
    }
}
