using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.ApplicationInfo;

/// <summary>通过 GitHub Releases 查询最新稳定发布版本。</summary>
public sealed class GitHubUpdateReleaseService : IUpdateReleaseService
{
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        string json = await Client.GetStringAsync(
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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "LOL-GameAssistant");
        return client;
    }
}
