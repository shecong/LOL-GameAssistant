using LOL_GameAssistant.Application.ClientFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    // 禁用自动重定向和持久 Cookie；登录态只在本次请求内发送至固定的腾讯服务。
    private static readonly HttpClient RewardHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _rewardHttp;
    private const string DailyGoodsId = "338943";
    private const string MallApi = "https://apps.game.qq.com/daoju/igw/main";

    public async Task<IReadOnlyList<ClientRewardGrant>> GetPendingRewardsAsync(CancellationToken cancellationToken = default) =>
        (await ScanRewardsAsync(cancellationToken).ConfigureAwait(false)).Rewards;

    public async Task<ClientRewardScan> ScanRewardsAsync(CancellationToken cancellationToken = default)
    {
        var rewards = new List<ClientRewardGrant>();
        var sources = new List<ClientRewardSourceStatus>();
        await Scan("客户端奖励接口", async () =>
        {
            // 国服当前未开放 Swagger；使用真实 /help 目录识别已知协议，不遍历试写接口。
            var help = await ReadRewardJson("/help", cancellationToken) as JObject;
            var functions = help?["functions"] as JObject;
            if (functions == null) throw new InvalidOperationException("接口目录暂不可用");
            string[] names = ["GetLolRewardsV1Grants", "PostLolRewardsV1GrantsByGrantIdSelect",
                "GetLolEventHubV1Events", "GetLolEventHubV1EventsByEventIdRewardTrackItems",
                "PostLolEventHubV1EventsByEventIdRewardTrackClaimAll", "GetLolTencentCookieRelayV1TftCookies"];
            int found = names.Count(name => functions.Property(name) != null);
            return $"已从 /help 识别 {found}/{names.Length} 项奖励相关接口；各来源的可用状态见下方";
        });
        // 各源独立失败；不能把无法查询解释为“已全部领取”。
        await Scan("待选奖励", async () =>
        {
            string? raw = await _lcu.GetStringAsync("/lol-rewards/v1/grants", cancellationToken).ConfigureAwait(false);
            if (ParseRewardJson(raw) is not JArray data) throw new InvalidOperationException("奖励接口暂不可用");
            var grants = ParseSelectionRewards(data);
            rewards.AddRange(grants);
            return $"{grants.Count} 组待选奖励";
        });
        await Scan("战令", async () =>
        {
            if (await ReadRewardJson("/lol-event-hub/v1/events", cancellationToken) is not JArray events)
                throw new InvalidOperationException("活动目录暂不可用");
            foreach (var entry in events.OfType<JObject>())
            {
                string? id = entry.Value<string>("eventId");
                if (string.IsNullOrEmpty(id)) continue;
                string root = EventRewardPath(id);
                var progress = await ReadRewardJson(root + "/progress", cancellationToken) as JObject;
                var items = await ReadRewardJson(root + "/items", cancellationToken) as JArray;
                var unclaimed = await ReadRewardJson(root + "/unclaimed-rewards", cancellationToken) as JObject;
                string name = entry.SelectToken("eventInfo.eventName")?.ToString() ?? "战令";
                if (progress == null || items == null || unclaimed?["rewardsCount"] == null)
                { sources.Add(new(name, "逐级奖励接口暂不可用，未判定已领完")); continue; }
                string detail = string.Join("\n", items.OfType<JObject>().Select(item =>
                    $"第 {item["threshold"]} 级：" + string.Join("；", (item["rewardOptions"] as JArray ?? [])
                        .OfType<JObject>().Select(option => $"{option["rewardName"]}（{RewardState(option.Value<string>("state"))}）"))));
                int count = unclaimed.Value<int>("rewardsCount");
                sources.Add(new(name, $"当前 {progress["level"]}/{progress["totalLevels"]} 级，可领取 {count} 项\n{detail}"));
                if (count <= 0) continue;
                rewards.Add(new("event:" + id, name + $" · {count} 项已解锁奖励", 0, [])
                { Source = "战令", RequiresSelection = false, MinimumSelections = 0,
                    Description = "领取该战令所有已解锁奖励；需要自选的奖励会进入待选奖励。" });
            }
            return "已扫描各活动的等级进度和逐级奖励";
        });
        await Scan("商城每日精粹箱", async () =>
        {
            var session = await GetMallSession(cancellationToken).ConfigureAwait(false);
            int remaining = await GetDailyRemaining(session, cancellationToken).ConfigureAwait(false);
            if (remaining > 0)
                rewards.Add(new("mall:daily-essence", "商城每日免费精粹箱", 0, [])
                { Source = "商城每日精粹箱", RequiresSelection = false, MinimumSelections = 0,
                    Description = "领取 1 个每日免费箱，不消耗点券、精粹或魄罗币。" });
            return remaining > 0 ? "今日可领取" : "今日无可领取次数";
        });
        await Scan("好运魄罗", async () =>
        {
            var scan = await ScanPoroRewardsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var gift in scan.Gifts.Where(g => g.CanClaim))
                rewards.Add(new($"poro:{scan.Month}:{gift.Stage}", gift.Name, 0, [])
                { Source = "好运魄罗", RequiresSelection = false, MinimumSelections = 0, Description = gift.Description });
            return $"本月已签到 {scan.Days} 天，可领取 {scan.Gifts.Count(g => g.CanClaim)} 组礼包\n" +
                string.Join("\n", scan.Gifts.Select(g => $"{(g.Stage == "0" ? "掌盟礼包" : g.Stage + " 天礼包")}：{g.Name}（{g.State}）"));
        });
        return new(rewards, sources);

        async Task Scan(string source, Func<Task<string>> read)
        {
            try { sources.Add(new(source, await read().ConfigureAwait(false))); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException
                or System.Net.WebSockets.WebSocketException or FormatException or ArgumentException or OverflowException)
            { sources.Add(new(source, "扫描未完成，接口或登录态暂不可用")); }
        }
    }

    private static string RewardState(string? state) => state switch
    { "Selected" => "已领取", "Unlocked" or "Unselected" => "已解锁", "Locked" => "未解锁", _ => "状态待确认" };
    private static string EventRewardPath(string id) => $"/lol-event-hub/v1/events/{Uri.EscapeDataString(id)}/reward-track";
    private static JToken? ParseRewardJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JToken.Parse(raw); } catch (JsonException) { return null; }
    }
    private async Task<JToken?> ReadRewardJson(string route, CancellationToken token) =>
        ParseRewardJson(await _lcu.GetStringAsync(route, token).ConfigureAwait(false));

    private async Task<ClientFeatureResult> ClaimEventRewardsAsync(string eventId, CancellationToken token)
    {
        var events = await ReadRewardJson("/lol-event-hub/v1/events", token) as JArray;
        if (events?.OfType<JObject>().Any(e => e.Value<string>("eventId") == eventId) != true)
            return ClientFeatureResult.Failure("战令已变化，请重新扫描");
        string root = EventRewardPath(eventId);
        var before = await ReadRewardJson(root + "/unclaimed-rewards", token) as JObject;
        if (before?.Value<int?>("rewardsCount") is not > 0)
            return ClientFeatureResult.Failure("未确认该战令有可领取奖励，请重新扫描");
        if (!await _lcu.PostAsync(root + "/claim-all", "{}", token).ConfigureAwait(false))
            return ClientFeatureResult.Failure("客户端未接受战令领取请求");
        var after = await ReadRewardJson(root + "/unclaimed-rewards", token) as JObject;
        return ClientFeatureResult.Success(after?.Value<int?>("rewardsCount") == 0
            ? "已核对战令奖励领取完成；自选奖励请在待选奖励中选择"
            : "战令领取请求已提交，最终状态未确认，请重新扫描");
    }

    private sealed record MallSession(string Cookie, string Uin, int Area, string Platform);
    private async Task<MallSession> GetMallSession(CancellationToken token)
    {
        var authorization = await ReadRewardJson("/lol-rso-auth/v1/authorization", token) as JObject;
        string? platform = authorization?.Value<string>("currentPlatformId");
        // 腾讯官网 lol_zone_area.js 的 areaid；未知或测试区不猜测、不向其它区提交。
        int area = platform switch
        {
            "HN1" => 1, "HN2" => 3, "HN3" => 4, "HN4" => 5, "HN5" => 7, "HN6" => 8,
            "HN7" => 10, "HN8" => 11, "HN9" => 13, "HN10" => 14, "HN11" => 15,
            "HN12" => 17, "HN13" => 18, "HN14" => 19, "HN15" => 22, "HN16" => 23,
            "HN17" => 24, "HN18" => 25, "HN19" => 27, "WT1" => 2, "WT2" => 6,
            "WT3" => 9, "WT4" => 12, "WT5" => 16, "WT6" => 20, "WT7" => 26,
            "EDU1" => 21, "BGP1" => 30, "BGP2" => 31, _ => 0
        };
        var relay = await ReadRewardJson("/lol-tencent-cookie-relay/v1/tft-cookies", token) as JObject;
        var cookies = relay?["https://apps.game.qq.com"] as JObject;
        string? uin = cookies?.Value<string>("uin"), key = cookies?.Value<string>("skey");
        if (area == 0 || string.IsNullOrWhiteSpace(uin) || string.IsNullOrWhiteSpace(key)
            || uin.Any(c => !char.IsAsciiDigit(c) && c != 'o') || key.Any(c => c is '\r' or '\n' or ';'))
            throw new InvalidOperationException("当前区服或腾讯登录态未确认");
        return new($"uin={uin}; skey={key}", uin.TrimStart('o', '0'), area, platform!);
    }

    private async Task<JObject> MallRequest(MallSession session, Dictionary<string, string> fields, CancellationToken token)
    {
        fields["_app_id"] = "1006"; fields["_biz_code"] = "lol";
        fields["areaid"] = session.Area.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields["roleid"] = session.Uin;
        string query = string.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Get, MallApi + "?" + query);
        request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);
        request.Headers.Referrer = new Uri("https://lol.qq.com/act/mall/index-list.html");
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("商城接口暂不可用");
        return JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }
    private async Task<int> GetDailyRemaining(MallSession session, CancellationToken token)
    {
        var data = await MallRequest(session, new() { ["_service"] = "goods.hs.items", ["_act_id"] = "22565",
            ["propid"] = DailyGoodsId, ["fields"] = "userLeft", ["set"] = "2" }, token).ConfigureAwait(false);
        if (data["result"]?.ToString() != "0" || !int.TryParse(data.SelectToken("data.goods[0].userLeft")?.ToString(), out int count) || count < 0)
            throw new InvalidOperationException("未取得每日免费次数");
        return count;
    }
    private async Task<ClientFeatureResult> ClaimDailyEssenceAsync(CancellationToken token)
    {
        try
        {
            var session = await GetMallSession(token).ConfigureAwait(false);
            if (await GetDailyRemaining(session, token).ConfigureAwait(false) <= 0)
                return ClientFeatureResult.Failure("今日免费箱已无领取次数");
            var resource = await MallRequest(session, new() { ["_service"] = "goods.hs.res", ["_act_id"] = "22565", ["set"] = "2" }, token).ConfigureAwait(false);
            string? md5 = resource.SelectToken("data.sfileMD5")?.ToString();
            if (string.IsNullOrWhiteSpace(md5)) return ClientFeatureResult.Failure("商城道具版本未确认，请重新扫描后重试");
            if (session != await GetMallSession(token).ConfigureAwait(false))
                return ClientFeatureResult.Failure("客户端账号或区服已变化，请重新扫描");
            var result = await MallRequest(session, new() { ["_service"] = "buy.plug.svr.sysc_ext", ["paytype"] = "8",
                ["iActionId"] = "22565", ["propid"] = DailyGoodsId, ["buyNum"] = "1", ["_plug_id"] = "72007",
                ["appext"] = JsonConvert.SerializeObject(new { sfileMD5 = md5, user_price = 0 }) }, token).ConfigureAwait(false);
            if (result["ret"]?.ToString() != "0") return ClientFeatureResult.Failure("商城未确认免费箱领取成功，请在客户端核对后重试");
            int remaining = await GetDailyRemaining(session, token).ConfigureAwait(false);
            return ClientFeatureResult.Success(remaining == 0 ? "已核对商城每日精粹箱领取完成" : "免费箱请求已提交，剩余次数尚未更新，请重新扫描");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException or FormatException or ArgumentException or OverflowException)
        { return ClientFeatureResult.Failure("商城领取结果未确认，请先在客户端核对，避免重复提交"); }
    }
}
