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
        LocalLiveClientSnapshot? snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot == null
            ? null
            : new LocalLiveClientOwnState(snapshot.CurrentGold, snapshot.Items, snapshot.GameTimeSeconds);
    }

    /// <summary>
    /// 同时读取 activeplayer 与 gamestats；建议刷新使用这一个快照，避免为金币、时间、模式各发一次请求。
    /// </summary>
    public static async Task<LocalLiveClientSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Task<HttpResponseMessage> playerTask = Client.GetAsync(
                "https://127.0.0.1:2999/liveclientdata/activeplayer", cancellationToken);
            Task<HttpResponseMessage> statsTask = Client.GetAsync(
                "https://127.0.0.1:2999/liveclientdata/gamestats", cancellationToken);
            await Task.WhenAll(playerTask, statsTask).ConfigureAwait(false);
            using HttpResponseMessage playerResponse = playerTask.Result;
            using HttpResponseMessage statsResponse = statsTask.Result;
            if (!playerResponse.IsSuccessStatusCode) return null;

            string playerContent = await playerResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = JObject.Parse(playerContent);
            var items = json["items"]?.Children<JObject>()
                .Select(item => item["displayName"]?.Value<string>() ?? item["itemID"]?.ToString() ?? "未知装备")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? new List<string>();

            int gameTime = 0;
            string? gameMode = null;
            if (statsResponse.IsSuccessStatusCode)
            {
                string statsContent = await statsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var stats = JObject.Parse(statsContent);
                gameTime = (int)Math.Max(0, stats["gameTime"]?.Value<double>() ?? 0);
                gameMode = stats["gameMode"]?.Value<string>();
            }

            return new LocalLiveClientSnapshot(
                json["championStats"]?["currentGold"]?.Value<int>() ?? 0,
                items,
                gameTime,
                gameMode);
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
internal sealed record LocalLiveClientSnapshot(int CurrentGold, IReadOnlyList<string> Items, int GameTimeSeconds, string? GameMode);