using LOL_GameAssistant.Application.ClientFeatures;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    private const string PalmActivityDirectory = "https://mlol.qt.qq.com/go/recommend/v2/platactivity?type=process&game_id=lol";
    private const string PalmActivityCenter = "https://jcc.qq.com/act/activityCenter2308/";

    private async Task<ClientRewardSourceStatus> ScanPalmDirectoryAsync(CancellationToken token)
    {
        // Reward scans require a connected account. Its credentials are never
        // attached to the public Palm directory request.
        _ = await GetMallSession(token).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, PalmActivityDirectory);
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("掌盟活动目录暂不可用");
        string raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (raw.Length > 2_097_152) throw new InvalidOperationException("掌盟活动目录超出预期大小");
        return ReadPalmDirectory(JObject.Parse(raw), DateTimeOffset.UtcNow);
    }

    internal static ClientRewardSourceStatus ReadPalmDirectory(JObject root, DateTimeOffset now)
    {
        if (root.Value<int?>("code") != 0 || root.SelectToken("data.result")?.Value<int?>() != 0
            || root.SelectToken("data.feedsInfo") is not JArray feeds)
            throw new InvalidOperationException("掌盟活动目录返回异常，未确认活动或领取状态");
        var activities = new Dictionary<string, string>(StringComparer.Ordinal);
        int unconfirmed = 0;
        foreach (var feed in feeds.OfType<JObject>())
        {
            if (feed.SelectToken("feedNews.body") is not JArray rows)
                throw new InvalidOperationException("掌盟活动目录格式变化");
            foreach (var row in rows.OfType<JObject>())
            {
                if (row.Value<string>("gameid") != "lol") continue;
                string? id = row["taskId"]?.ToString(), title = row.Value<string>("title");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)
                    || !PalmDate(row.Value<string>("activityStartTime"), out var start)
                    || !PalmDate(row.Value<string>("activityEndTime"), out var end) || end < start)
                { unconfirmed++; continue; }
                if (now < start || now > end) continue;
                string? intent = row.Value<string>("intent");
                string entry = Uri.TryCreate(intent, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                    && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host.EndsWith(".qq.com", StringComparison.OrdinalIgnoreCase)
                    ? uri.AbsoluteUri : "需要在掌盟内打开";
                activities.TryAdd(id, $"{title}（截止 {end:MM-dd HH:mm}）\n{entry}");
            }
        }
        string? next = root.SelectToken("data.next")?.ToString();
        string coverage = next == "0" ? "当前目录读取完成" : "目录分页状态未确认，不能判定已扫描全部活动";
        string message = $"发现 {activities.Count} 项当期 LOL 活动；{coverage}。活动清单不代表账号可领取奖励，尚未查询各活动资格。";
        if (unconfirmed > 0) message += $"另有 {unconfirmed} 项活动资料未确认。";
        message += "\n" + (activities.Count == 0 ? "当前目录没有匹配的有效活动；未判定掌盟奖励已领完。" : string.Join("\n\n", activities.Values));
        return new("掌盟活动目录", message) { OfficialUrl = PalmActivityCenter };
    }

    private static bool PalmDate(string? text, out DateTimeOffset value)
    {
        value = default;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        value = new DateTimeOffset(date, TimeSpan.FromHours(8));
        return true;
    }
}
