using Newtonsoft.Json.Linq;
using System.Net.Security;

namespace LOL_GameAssistant.Infrastructure.LiveGame;

/// <summary>
/// 读取游戏客户端本机 Live Client Data API 中与当前玩家有关的数据。
/// 仅在游戏进程已运行时可用，且不会请求位置、敌方冷却等信息。
/// </summary>
internal static class LocalLiveClientDataReader
{
    private static readonly HttpClient Client;

    static LocalLiveClientDataReader()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
                request.RequestUri?.Host == "127.0.0.1" || errors == SslPolicyErrors.None
        };
        Client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    public static async Task<LocalLiveClientOwnState?> GetOwnStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                "https://127.0.0.1:2999/liveclientdata/activeplayer",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = JObject.Parse(content);
            var items = json["items"]?.Children<JObject>()
                .Select(item => item["displayName"]?.Value<string>() ?? item["itemID"]?.ToString() ?? "未知装备")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? new List<string>();

            int? gameTime = await GetGameTimeSecondsAsync(cancellationToken).ConfigureAwait(false);
            return new LocalLiveClientOwnState(
                json["championStats"]?["currentGold"]?.Value<int>() ?? 0,
                items,
                gameTime ?? 0);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            return null;
        }
    }

    public static async Task<string?> GetGameModeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                "https://127.0.0.1:2999/liveclientdata/gamestats",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JObject.Parse(content)["gameMode"]?.Value<string>();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<int?> GetGameTimeSecondsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                "https://127.0.0.1:2999/liveclientdata/gamestats",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (int)Math.Max(0, JObject.Parse(content)["gameTime"]?.Value<double>() ?? 0);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record LocalLiveClientOwnState(int CurrentGold, IReadOnlyList<string> Items, int GameTimeSeconds);
