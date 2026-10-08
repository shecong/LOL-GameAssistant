using Newtonsoft.Json.Linq;
using System.Net.Security;

namespace LOL_GameAssistant.Infrastructure.LiveGame;

/// <summary>
/// 读取游戏客户端本机 Live Client Data API 中与当前玩家有关的数据。
/// 仅在游戏进程已运行时可用，且不会请求位置、敌方冷却等信息。
/// </summary>
internal static class LocalLiveClientDataReader
{
    public static async Task<IReadOnlyList<LOL_GameAssistant.Domain.LiveGame.LiveScoreboardPlayer>?> GetScoreboardAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync("https://127.0.0.1:2999/liveclientdata/playerlist", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseScoreboard(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    internal static IReadOnlyList<LOL_GameAssistant.Domain.LiveGame.LiveScoreboardPlayer> ParseScoreboard(string content) =>
        JArray.Parse(content).OfType<JObject>().Select(player =>
            new LOL_GameAssistant.Domain.LiveGame.LiveScoreboardPlayer(
                player["riotId"]?.Value<string>() ??
                    (player["riotIdGameName"]?.Value<string>() is { Length: > 0 } name
                        ? name + (player["riotIdTagLine"]?.Value<string>() is { Length: > 0 } tag ? "#" + tag : "") : ""),
                player["summonerName"]?.Value<string>() ?? "", player["team"]?.Value<string>() ?? "",
                player["scores"]?["kills"]?.Value<int>() ?? 0,
                player["scores"]?["deaths"]?.Value<int>() ?? 0,
                player["scores"]?["assists"]?.Value<int>() ?? 0,
                player["scores"]?["creepScore"]?.Value<int>() ?? 0,
                player["scores"]?["wardScore"]?.Value<double>())).ToArray();
    private static readonly HttpClient Client;

    /// <summary>初始化 LocalLiveClientDataReader 使用的共享状态。</summary>
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

    /// <summary>读取当前玩家的金币、装备及游戏时间状态。</summary>
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

    /// <summary>读取游戏客户端返回的当前玩法模式。</summary>
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

    /// <summary>读取当前对局已进行的秒数。</summary>
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

/// <summary>从 Live Client API 提取的当前玩家金币、装备和游戏时间。</summary>
internal sealed record LocalLiveClientOwnState(int CurrentGold, IReadOnlyList<string> Items, int GameTimeSeconds);
/// <summary>一次读取得到的玩家状态、游戏时间和模式快照。</summary>
internal sealed record LocalLiveClientSnapshot(int CurrentGold, IReadOnlyList<string> Items, int GameTimeSeconds, string? GameMode);
