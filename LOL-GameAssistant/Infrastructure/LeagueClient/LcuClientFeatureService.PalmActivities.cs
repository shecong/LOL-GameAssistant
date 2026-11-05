using LOL_GameAssistant.Application.ClientFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    private sealed record PalmEvent(string Id, string Name, string Path, string Bind, string Read, string Claim,
        IReadOnlyDictionary<string, string> Flows);
    private sealed record PalmEventContext(PalmEvent Event, PoroActivity Config, MallSession Session);
    private sealed record PalmEventScan(PalmEventContext Context, IReadOnlyList<ClientRewardGrant> Rewards, string Message, JObject? State = null);
    private static readonly PalmEvent[] PalmEvents =
    [
        new("1064772", "掌盟 S16 观赛任务", "a20261015matchs16", "er7gB9", "pbYAoN", "YpL796",
            new Dictionary<string, string> { ["er7gB9"] = "1265256", ["pbYAoN"] = "1265296", ["YpL796"] = "1265299" }),
        new("1065770", "海斗音乐季表情奖励", "a20261022musicseason", "KAvQLc", "4b9da9", "ONhKHV",
            new Dictionary<string, string> { ["KAvQLc"] = "1266807", ["4b9da9"] = "1266785", ["ONhKHV"] = "1266809" }),
        new("1058041", "全球总决赛暂存奖励", "a20261014lplevent", "98Vc2D", "DcTqBD", "nt32P2",
            new Dictionary<string, string> { ["98Vc2D"] = "1258159", ["DcTqBD"] = "1262751", ["nt32P2"] = "1264506", ["Uep2mX"] = "1266321" })
    ];
    private static string PalmEventUrl(PalmEvent activity) => $"https://lol.qq.com/act/{activity.Path}/index.html";
    private static bool MatchesPalmEvent(Uri uri, PalmEvent activity) => uri.Host.Equals("lol.qq.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/act/{activity.Path}/", StringComparison.Ordinal);
    private static string PalmAccountStamp(MallSession session) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{session.Uin}:{session.Platform}:{session.Area}")))[..16];
    private static int PalmInteger(JToken? value) => int.TryParse(value?.ToString(), NumberStyles.None,
        CultureInfo.InvariantCulture, out int result) && result >= 0 ? result : throw new InvalidOperationException("掌盟活动状态字段未确认");

    private async Task<PalmEventContext> OpenPalmEventAsync(PalmEvent activity, CancellationToken token)
    {
        var session = await GetMallSession(token).ConfigureAwait(false);
        if (session.Area > 30) throw new InvalidOperationException("当前活动未确认支持该大区，请到官方页核对");
        string url = $"https://lol.qq.com/comm-htdocs/js/ams/actDesc/{activity.Id[^3..]}/{activity.Id}/act.desc.js";
        var data = await ReadPoroObjectAsync(new Uri(url), "ams_actdesc", token).ConfigureAwait(false);
        if (data["iActivityId"]?.ToString() != activity.Id || data["sServiceType"]?.ToString() != "lol"
            || data["sServiceDepartment"]?.ToString() != "k6_lol" || data["iShutdown"]?.ToString() != "0"
            || !PalmDate(data.Value<string>("dtBeginTime"), out var begin) || !PalmDate(data.Value<string>("dtEndTime"), out var end)
            || DateTimeOffset.UtcNow < begin || DateTimeOffset.UtcNow > end)
            throw new InvalidOperationException("活动未开放、已结束或配置变化，请到官方页面核对");
        if (!Uri.TryCreate("https://" + data.Value<string>("sAmePcUrl"), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "https" || !endpoint.IsDefaultPort || endpoint.UserInfo.Length > 0
            || !endpoint.Host.EndsWith(".ams.game.qq.com", StringComparison.OrdinalIgnoreCase)
            || endpoint.AbsolutePath != "/ams/ame/amesvr" || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            throw new InvalidOperationException("掌盟活动服务地址未确认");
        var flows = data["flows"] as JObject ?? throw new InvalidOperationException("掌盟活动流程目录未确认");
        foreach (var flow in activity.Flows)
            if (data.SelectToken("alias." + flow.Key)?.ToString() != flow.Value || flows["f_" + flow.Value] is not JObject)
                throw new InvalidOperationException("掌盟活动协议已更新，需要重新适配");
        string? sdid = data.Value<string>("sSDID");
        if (sdid?.Length != 32 || sdid.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidOperationException("活动路由标识未确认");
        return new(activity, new(activity.Id, "k6_lol", endpoint, flows, sdid), session);
    }

    private async Task<JObject> PalmEventRequestAsync(PalmEventContext context, string alias, Dictionary<string, string>? values, CancellationToken token)
    {
        var fields = values == null ? new Dictionary<string, string>() : new(values);
        var config = context.Config;
        fields["iActivityId"] = config.Id; fields["iFlowId"] = context.Event.Flows[alias];
        fields["sServiceType"] = "lol"; fields["sServiceDepartment"] = config.Department; fields["xhr"] = "1";
        string key = context.Session.Cookie[(context.Session.Cookie.IndexOf("skey=", StringComparison.Ordinal) + 5)..];
        uint hash = 5381; foreach (char ch in key) hash = unchecked(hash * 33 + ch);
        fields["g_tk"] = (hash & 0x7fffffff).ToString(CultureInfo.InvariantCulture);
        string query = $"?ameVersion=0.3&sServiceType=lol&iActivityId={config.Id}&sServiceDepartment={config.Department}&isXhrPost=true&sSDID={config.Sdid}";
        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint.AbsoluteUri + query);
        request.Headers.TryAddWithoutValidation("Cookie", context.Session.Cookie);
        request.Headers.Referrer = new Uri(PalmEventUrl(context.Event));
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("掌盟活动请求未确认，请重新扫描");
        var result = JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        if (result["ret"]?.ToString() != "0" || result.SelectToken("flowRet.iRet")?.ToString() != "0"
            || result["modRet"] is not JObject module || module["iRet"]?.ToString() != "0" || module["ret"]?.ToString() != "0"
            || result["failedRet"] is { Type: not JTokenType.Null } failures && (failures is not JObject || failures.HasValues))
            throw new InvalidOperationException("活动未确认请求成功；请核对登录、任务及大区，不会自动绑定或重试领取");
        var data = module["jData"] as JObject ?? throw new InvalidOperationException("活动状态格式变化，未判定领取完成");
        if (data["iRet"] != null && data["iRet"]!.ToString() != "0") throw new InvalidOperationException("活动业务状态未确认");
        return data;
    }

    private async Task<PalmEventScan> ReadPalmEventAsync(PalmEvent activity, CancellationToken token)
    {
        var context = await OpenPalmEventAsync(activity, token).ConfigureAwait(false);
        var binding = await PalmEventRequestAsync(context, activity.Bind, new() { ["query"] = "true" }, token).ConfigureAwait(false);
        if (binding.Property("bindarea") == null || binding["bindarea"]?.Type is not (JTokenType.Object or JTokenType.Null))
            throw new InvalidOperationException("活动绑定状态格式变化");
        if (binding["bindarea"] is not JObject bound)
            return new(context, [], "尚未绑定活动大区，请先到官方活动页确认；未自动更改绑定。");
        if (bound["Fuin"]?.ToString().TrimStart('o', '0') != context.Session.Uin || PalmInteger(bound["Farea"]) != context.Session.Area)
            return new(context, [], "活动绑定账号或大区与当前客户端不一致，请到官方页核对。");
        var state = await PalmEventRequestAsync(context, activity.Read, null, token).ConfigureAwait(false);
        string prefix = $"palm:{activity.Id}:{PalmAccountStamp(context.Session)}:";
        var rewards = new List<ClientRewardGrant>();
        string message;
        if (activity.Id == "1065770")
        {
            int claimed = PalmInteger(state["iClaimed"]), games = PalmInteger(state["iGameCount"]), area = PalmInteger(state["bindArea"]);
            if (claimed > 1) throw new InvalidOperationException("海斗领取状态未确认");
            if (claimed == 0 && games >= 1 && area == context.Session.Area)
                rewards.Add(PalmGrant(prefix + "emoji", "海斗音乐季对局表情奖励", activity.Name, "已完成至少 1 局海斗，奖励发往已核对的当前活动大区。"));
            message = claimed == 1 ? "对局表情奖励已领取。" : area == 0 ? "首次参与需到官方页确认领奖大区。"
                : area != context.Session.Area ? "领奖大区与当前客户端不一致。" : games < 1 ? "尚未完成海斗对局任务。" : "对局任务已达成，表情奖励可领取。";
        }
        else if (activity.Id == "1064772")
        {
            if (state["statuList"] is not JArray tasks || state["holdList"] is not JArray holds)
                throw new InvalidOperationException("掌盟观赛任务或领取状态未确认");
            if (tasks.Any(t => t is not JObject) || holds.Any(t => t is not JObject)) throw new InvalidOperationException("掌盟观赛状态格式变化");
            string[] keys = ["daily_login_reward", "daily_battle", "once_task6023", "once_replay", "once_share_reward", "once_subscribe_reward"];
            string[] names = ["查看赛事速递", "每日进行1场游戏对局", "共创掌盟电竞百科词条", "体验掌盟战绩复盘助手", "分享活动页", "一键关注社区福利官"];
            var seen = new HashSet<int>();
            int unsupported = 0;
            foreach (var task in tasks.OfType<JObject>())
            {
                int id = PalmInteger(task["taskId"]), done = PalmInteger(task["done"]);
                if (id < 1 || id > keys.Length) { unsupported++; continue; }
                if (!seen.Add(id) || done > 1) throw new InvalidOperationException("掌盟任务状态重复或变化");
                var rows = holds.OfType<JObject>().Where(h => h.Value<string>("key") == keys[id - 1]).ToArray();
                if (rows.Length > 1) throw new InvalidOperationException("掌盟领取记录重复");
                int used = rows.Length == 0 ? 0 : PalmInteger(rows[0]["iUsedNum"]);
                if (done == 1 && used == 0)
                    rewards.Add(PalmGrant(prefix + "task-" + id, names[id - 1] + " · 任务奖励", activity.Name,
                        "任务已由官方确认完成；仅领取任务对应抽奖机会，不执行抽奖、分享或关注。"));
            }
            message = $"已核对 {seen.Count} 项任务，{rewards.Count} 项达成且未领取。";
            if (unsupported > 0) message += $" 另有 {unsupported} 项新任务尚未适配。";
        }
        else
        {
            if (state["zcx"] is not JArray pending || pending.Count > 20 || pending.Any(t => t is not JObject)) throw new InvalidOperationException("总决赛暂存奖励状态未确认");
            int open = PalmInteger(state["openShop"]), unlocked = PalmInteger(state["skinRec"]);
            if (open > 1) throw new InvalidOperationException("活动开放状态未确认");
            if (open != 1 || unlocked == 0) message = "活动领取尚未开放或未解锁；未发起兑换。";
            else
            {
                var ids = new HashSet<string>(); int owned = 0;
                foreach (var item in pending.OfType<JObject>())
                {
                    string id = item["id"]?.ToString() ?? "", code = item["sExtend3"]?.ToString() ?? "";
                    if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) || number <= 0 || number.ToString(CultureInfo.InvariantCulture) != id
                        || code.Length is < 1 or > 20 || code.Any(c => !char.IsAsciiDigit(c)) || !ids.Add(id))
                        throw new InvalidOperationException("暂存奖励标识未确认");
                    var ownership = await PalmEventRequestAsync(context, "Uep2mX", new() { ["iItemCode"] = code, ["iChangeArea"] = context.Session.Area.ToString(CultureInfo.InvariantCulture) }, token).ConfigureAwait(false);
                    int has = PalmInteger(ownership["res"]);
                    if (has == 1) { owned++; continue; }
                    if (has != 0) throw new InvalidOperationException("皮肤拥有状态未确认");
                    rewards.Add(PalmGrant(prefix + "skin-" + id, $"总决赛活动暂存皮肤 · 第 {rewards.Count + 1} 份", activity.Name,
                        "奖励已在活动暂存箱，当前绑定大区尚未拥有；仅发放已获得的皮肤，不消耗活动积分。"));
                }
                message = $"暂存箱 {pending.Count} 份，{rewards.Count} 份可发放；{owned} 份当前大区已拥有，需到官方页选择其他大区。";
            }
        }
        string region = LOL_GameAssistant.Domain.LeagueClient.LeagueRegionNames.TryGetName(context.Session.Platform, out var regionName) ? regionName : context.Session.Platform;
        return new(context, rewards.Select(r => r with { Description = r.Description + $"\n领奖大区：{region}。" }).ToArray(), message, state);
    }

    private static ClientRewardGrant PalmGrant(string id, string name, string source, string description) => new(id, name, 0, [])
        { Source = source, RequiresSelection = false, MinimumSelections = 0, Description = description };

    private async Task<ClientFeatureResult> ClaimPalmRewardAsync(string grantId, CancellationToken token)
    {
        bool submitted = false;
        try
        {
            string[] parts = grantId.Split(':');
            var activity = parts.Length == 4 ? PalmEvents.SingleOrDefault(e => e.Id == parts[1]) : null;
            if (activity == null) return ClientFeatureResult.Failure("掌盟奖励标识无效，请重新扫描");
            var session = await GetMallSession(token).ConfigureAwait(false);
            if (PalmAccountStamp(session) != parts[2]) return ClientFeatureResult.Failure("账号或大区已变化，请重新扫描");
            var before = await ReadPalmEventAsync(activity, token).ConfigureAwait(false);
            if (!before.Rewards.Any(r => r.GrantId == grantId)) return ClientFeatureResult.Failure("该奖励已领取、任务未完成或状态变化，请重新扫描");
            var current = await GetMallSession(token).ConfigureAwait(false);
            if (current != before.Context.Session || PalmAccountStamp(current) != parts[2]) return ClientFeatureResult.Failure("领奖前登录态已变化，请重新扫描");
            Dictionary<string, string>? fields = activity.Id switch
            {
                "1064772" => new() { ["taskId"] = parts[3]["task-".Length..] },
                "1058041" => new() { ["id"] = parts[3]["skin-".Length..], ["iChangeArea"] = current.Area.ToString(CultureInfo.InvariantCulture) },
                _ => null
            };
            submitted = true;
            await PalmEventRequestAsync(before.Context, activity.Claim, fields, token).ConfigureAwait(false);
            var after = await ReadPalmEventAsync(activity, token).ConfigureAwait(false);
            if (after.Context.Session != current || after.Message.Contains("不一致", StringComparison.Ordinal)
                || after.Message.Contains("未绑定", StringComparison.Ordinal)) return ClientFeatureResult.Failure("领取请求已提交，但账号或大区状态变化，最终结果需在官方页核对");
            bool confirmed = after.State is { } state && activity.Id switch
            {
                "1065770" => PalmInteger(state["iClaimed"]) == 1,
                "1064772" => ConfirmWatchTask(state, parts[3]),
                _ => PalmInteger(state["openShop"]) == 1 && PalmInteger(state["skinRec"]) > 0
                    && state["zcx"] is JArray pending && !pending.OfType<JObject>().Any(p => p["id"]?.ToString() == parts[3]["skin-".Length..])
            };
            return confirmed ? ClientFeatureResult.Success("已核对掌盟活动奖励领取完成")
                : ClientFeatureResult.Success("掌盟请求已被接受，最终领取状态尚未更新，请重新扫描核对");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException or FormatException or ArgumentException or OverflowException)
        { return ClientFeatureResult.Failure(submitted ? "掌盟领取结果未确认，请先在官方页核对；未自动重试领取" : "掌盟活动资格或登录状态未确认，请重新扫描后核对官方页面"); }
    }

    private static bool ConfirmWatchTask(JObject state, string reward)
    {
        string[] keys = ["daily_login_reward", "daily_battle", "once_task6023", "once_replay", "once_share_reward", "once_subscribe_reward"];
        int id = int.Parse(reward["task-".Length..], CultureInfo.InvariantCulture);
        return state["statuList"] is JArray tasks && tasks.OfType<JObject>().Any(t => PalmInteger(t["taskId"]) == id && PalmInteger(t["done"]) == 1)
            && state["holdList"] is JArray holds && holds.OfType<JObject>().Any(h => h["key"]?.ToString() == keys[id - 1] && PalmInteger(h["iUsedNum"]) > 0);
    }
}
