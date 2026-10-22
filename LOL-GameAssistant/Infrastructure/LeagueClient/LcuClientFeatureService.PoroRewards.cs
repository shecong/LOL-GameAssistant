using LOL_GameAssistant.Application.ClientFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    private readonly ITencentClientRpc _poroRpc;
    private sealed record PoroGift(string Stage, string Name, string Description, string State, bool CanClaim);
    private sealed record PoroScan(string Month, int Days, IReadOnlyList<PoroGift> Gifts,
        MallSession Session, TencentClientIdentity Identity);
    private sealed record PoroActivity(string Id, string Department, Uri Endpoint, JObject Flows, string? Sdid);

    private async Task<PoroScan> ScanPoroRewardsAsync(CancellationToken token)
    {
        var session = await GetMallSession(token).ConfigureAwait(false);
        var identity = await _poroRpc.GetIdentityAsync(token).ConfigureAwait(false);
        ValidatePoroIdentity(session, identity);
        string month = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMM", CultureInfo.InvariantCulture);
        var config = await GetPoroActivityAsync("510196", token).ConfigureAwait(false);
        var gifts = await ReadPoroObjectAsync(new Uri($"https://lol.qq.com/act/a20170405signin/publish/LOL_SignInReward_{month}.js"),
            "LOLSignInReward", token).ConfigureAwait(false);
        if (gifts["date"]?.ToString() != month) throw new InvalidOperationException("魄罗礼包月份未确认");
        // 与官方礼包页面打开时一致：同步当日签到后查询可领阶段，不消耗任何货币。
        var signIn = await PoroAmsAsync(session, config, "901066", new() { ["iArea"] = session.Area.ToString(CultureInfo.InvariantCulture),
            ["action"] = "SignIn" }, token).ConfigureAwait(false);
        string? dates = signIn.SelectToken("SignIn.msg")?.Value<string>();
        if (dates == null) throw new InvalidOperationException("未取得魄罗签到日期");
        string[] days = dates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (days.Any(d => !int.TryParse(d, out int day) || day is < 1 or > 31) || days.Distinct().Count() != days.Length)
            throw new InvalidOperationException("魄罗签到日期格式变化");
        var status = await ReadPoroClaimStatusAsync(session, config, token).ConfigureAwait(false);
        var result = new List<PoroGift>();
        foreach (var gift in (gifts["stage-reward"] as JArray ?? throw new InvalidOperationException("魄罗阶段奖励目录不可用")).OfType<JObject>())
        {
            string stage = gift["stage"]?.ToString() ?? "";
            if (!int.TryParse(stage, out int threshold) || threshold is < 1 or > 31
                || threshold.ToString(CultureInfo.InvariantCulture) != stage) throw new InvalidOperationException("魄罗礼包阶段无效");
            result.Add(ToPoroGift(gift, stage, days.Length >= threshold, status[stage]));
        }
        if (gifts["initial-reward"] is JObject initial)
            result.Add(ToPoroGift(initial, "0", status.Value<bool?>("initial_finished") == true, status["initial"]));
        if (result.Select(g => g.Stage).Distinct().Count() != result.Count) throw new InvalidOperationException("魄罗奖励阶段重复");
        return new(month, days.Length, result, session, identity);
    }

    private static PoroGift ToPoroGift(JObject gift, string stage, bool eligible, JToken? rawStatus)
    {
        string description = string.Join("；", (gift["rewards"] as JArray ?? []).OfType<JObject>()
            .Select(r => $"{r["name"]} × {r["amount"]}"));
        string state = "尚未达成";
        bool canClaim = false;
        if (rawStatus is JObject status)
        {
            if (status.Value<bool?>("expire") == null) state = "状态待确认";
            else if (status.Value<bool?>("expire") == true) state = "已过期";
            else if (int.TryParse(status["used"]?.ToString(), out int used)
                && int.TryParse(status["total"]?.ToString(), out int total)
                && int.TryParse(status["remain"]?.ToString(), out int remain) && total > 0 && used >= 0 && remain >= 0)
            {
                if (used == total && remain == 0) state = "已领取";
                else if (eligible && remain > 0) { state = "可领取"; canClaim = true; }
                else state = eligible ? "状态待确认" : "尚未达成";
            }
            else state = "状态待确认";
        }
        else if (rawStatus == null && eligible) { state = "可领取"; canClaim = true; }
        return new(stage, gift.Value<string>("name") ?? $"好运魄罗 {stage} 天礼包", description, state, canClaim);
    }

    private static void ValidatePoroIdentity(MallSession session, TencentClientIdentity identity)
    {
        int zone = session.Platform switch { "EDU1" => 65539, "BGP1" => 261, "BGP2" => 517, _ => 0 };
        if (zone == 0 && session.Platform.Length > 2 && int.TryParse(session.Platform[2..], out int number))
            zone = session.Platform[..2] switch { "HN" => number * 256 + 1, "WT" => number * 256 + 2, _ => 0 };
        if (identity.AccountType != "1" || identity.Uin != session.Uin || zone == 0 || identity.ZoneId != zone)
            throw new InvalidOperationException("魄罗签名账号与当前商城账号或区服不一致");
    }

    private async Task<ClientFeatureResult> ClaimPoroRewardAsync(string grantId, CancellationToken token)
    {
        try
        {
            string[] id = grantId.Split(':');
            if (id.Length != 3) return ClientFeatureResult.Failure("魄罗奖励标识无效，请重新扫描");
            var scan = await ScanPoroRewardsAsync(token).ConfigureAwait(false);
            var gift = scan.Gifts.SingleOrDefault(g => g.Stage == id[2]);
            if (scan.Month != id[1] || gift?.CanClaim != true)
                return ClientFeatureResult.Failure("该魄罗礼包已领取、尚未达成或月份已变化，请重新扫描");
            var config = await GetPoroActivityAsync("109144", token).ConfigureAwait(false);
            // 新鲜签名由当前客户端生成；签名前后都核对账号，签名不缓存、不落盘。
            var signature = await _poroRpc.SignRewardAsync(gift.Stage, scan.Identity, token).ConfigureAwait(false);
            if (scan.Session != await GetMallSession(token).ConfigureAwait(false))
                return ClientFeatureResult.Failure("客户端账号或区服已变化，请重新扫描");
            string flow = gift.Stage == "0" ? "358524" : "356172";
            var fields = new Dictionary<string, string>
            {
                ["sArea"] = scan.Session.Area.ToString(CultureInfo.InvariantCulture),
                ["nonce"] = signature.Nonce, ["time"] = signature.Time.ToString(CultureInfo.InvariantCulture), ["token"] = signature.Token,
                ["ams_appname"] = "LOL", ["ams_targetappid"] = "wx35a4657522d31151"
            };
            if (gift.Stage != "0") fields["iStage"] = gift.Stage;
            await PoroAmsAsync(scan.Session, config, flow, fields, token).ConfigureAwait(false);
            var queryConfig = await GetPoroActivityAsync("510196", token).ConfigureAwait(false);
            var status = await ReadPoroClaimStatusAsync(scan.Session, queryConfig, token).ConfigureAwait(false);
            var latest = status[gift.Stage == "0" ? "initial" : gift.Stage] as JObject;
            bool confirmed = latest?["used"]?.ToString() == latest?["total"]?.ToString()
                && latest?["total"]?.ToString() is not (null or "0") && latest?["remain"]?.ToString() == "0"
                && latest?.Value<bool?>("expire") == false;
            return ClientFeatureResult.Success(confirmed ? $"已核对好运魄罗礼包领取完成：{gift.Name}"
                : "魄罗领取请求已被接受，最终状态尚未更新，请重新扫描核对");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException
            or WebSocketException or FormatException or ArgumentException or OverflowException)
        { return ClientFeatureResult.Failure("魄罗领取结果未确认，请先在客户端核对后重新扫描；未自动重试领取"); }
    }

    private async Task<JObject> ReadPoroClaimStatusAsync(MallSession session, PoroActivity config, CancellationToken token)
    {
        var result = await PoroAmsAsync(session, config, "901066", new() { ["iArea"] = session.Area.ToString(CultureInfo.InvariantCulture),
            ["action"] = "GetClaimStatus" }, token).ConfigureAwait(false);
        return result.SelectToken("GetClaimStatus.msg") as JObject ?? throw new InvalidOperationException("魄罗领取状态暂不可用");
    }

    private async Task<PoroActivity> GetPoroActivityAsync(string id, CancellationToken token)
    {
        string path = id switch
        {
            "510196" => "https://lol.qq.com/comm-htdocs/js/ams/actDesc/196/510196/act.desc.js",
            "109144" => "https://lol.qq.com/comm-htdocs/js/ams/v0.2R02/act/109144/act.desc.js",
            _ => throw new InvalidOperationException("未知魄罗活动")
        };
        var data = await ReadPoroObjectAsync(new Uri(path), "ams_actdesc", token).ConfigureAwait(false);
        string endpoint = data.Value<string>("sAmePcUrl") ?? "";
        if (!endpoint.StartsWith("https://", StringComparison.Ordinal)) endpoint = "https://" + endpoint;
        if (data["iActivityId"]?.ToString() != id || data["sServiceType"]?.ToString() != "lol"
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443
            || !uri.Host.EndsWith(".ams.game.qq.com", StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length > 0
            || uri.AbsolutePath is not ("/ams/ame/amesvr" or "/ams/ame/ame.php") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("魄罗活动服务地址未确认");
        string department = data.Value<string>("sServiceDepartment") ?? "";
        if (!Regex.IsMatch(department, "^group_[a-z0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new InvalidOperationException("魄罗活动路由未确认");
        return new(id, department, uri, data["flows"] as JObject ?? throw new InvalidOperationException("魄罗活动流程目录不可用"),
            data.Value<string>("sSDID"));
    }

    internal static JObject ParsePoroScript(string script, string variable)
    {
        // 只解析官方配置中的 JSON 对象，不执行下载的 JavaScript。
        var match = Regex.Match(script, @"\bvar\s+" + Regex.Escape(variable) + @"\s*=\s*(?=\{)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success || script.Length > 1_048_576) throw new InvalidOperationException("魄罗配置格式变化");
        using var reader = new JsonTextReader(new StringReader(script[(match.Index + match.Length)..]));
        return JObject.Load(reader);
    }

    private async Task<JObject> ReadPoroObjectAsync(Uri uri, string variable, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("魄罗官方配置暂不可用");
        return ParsePoroScript(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), variable);
    }

    private async Task<JObject> PoroAmsAsync(MallSession session, PoroActivity config, string flow,
        Dictionary<string, string> fields, CancellationToken token)
    {
        if (config.Flows.Property("f_" + flow) == null) throw new InvalidOperationException("当前活动未开放该领取流程");
        fields["iActivityId"] = config.Id; fields["iFlowId"] = flow;
        fields["sServiceType"] = "lol"; fields["sServiceDepartment"] = config.Department; fields["xhr"] = "1";
        // 官方 AMS CSRF 算法，来自当前客户端 Cookie relay；不使用过期或人工填写的登录凭据。
        string key = session.Cookie[(session.Cookie.IndexOf("skey=", StringComparison.Ordinal) + 5)..];
        uint hash = 5381;
        foreach (char ch in key) hash = unchecked(hash * 33 + ch);
        fields["g_tk"] = (hash & 0x7fffffff).ToString(CultureInfo.InvariantCulture);
        string query = "?ameVersion=0.3&sServiceType=lol&iActivityId=" + config.Id
            + "&sServiceDepartment=" + Uri.EscapeDataString(config.Department) + "&isXhrPost=true";
        if (!string.IsNullOrEmpty(config.Sdid)) query += "&sSDID=" + Uri.EscapeDataString(config.Sdid);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint.AbsoluteUri + query);
        request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);
        request.Headers.Referrer = new Uri("https://lol.qq.com/taskcenter/view/dailytask/new_dailysign.html");
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("魄罗活动接口暂不可用");
        var result = JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        if (result.SelectToken("flowRet.iRet")?.ToString() != "0" || result["ret"]?.ToString() != "0"
            || result["failedRet"] is JObject failures && failures.Count > 0
            || result["modRet"] is not JObject module || module["iRet"]?.ToString() != "0")
            throw new InvalidOperationException("魄罗活动未确认本次请求成功");
        return module;
    }
}
