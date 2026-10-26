using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Profiles;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Full client resource catalogs; ownership annotates choices rather than hiding them.</summary>
public sealed class LcuClientIdentityService(ILcuRequestSender lcu, Action<string>? diagnostic = null) : IClientIdentityService
{
    private const string Summary = "/lol-challenges/v1/summary-player-data/local-player";
    private const string Preferences = "/lol-challenges/v1/update-player-preferences";
    private const string Regalia = "/lol-regalia/v2/current-summoner/regalia";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HttpClient PreviewClient = new() { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private string _owner = "", _session = "";
    private Snapshot? _baseline;
    private IReadOnlyList<ClientIdentityChoice> _choices = [];
    private sealed record Snapshot(string Owner, JObject Preferences, JObject Regalia);
    private static string Text(JToken? value) => value?.Type is JTokenType.String or JTokenType.Integer ? value.ToString() : "";
    private static IEnumerable<JObject> Objects(JToken? token) => token switch {
        JArray array => array.OfType<JObject>(), JObject map => map.Properties().Select(p => p.Value).OfType<JObject>(), _ => [] };
    private async Task<JToken> JsonAsync(string path, CancellationToken token)
    {
        string? json = await lcu.GetStringAsync(path, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("客户端资源接口不可用：" + path);
        try { return JToken.Parse(json); } catch (JsonException) { throw new InvalidOperationException("客户端资源格式异常：" + path); }
    }
    private async Task<string> OwnerAsync(CancellationToken token)
    {
        var user = await JsonAsync("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
        var region = await JsonAsync("/lol-rso-auth/v1/authorization", token).ConfigureAwait(false);
        string puuid = Text(user["puuid"]), platform = Text(region["currentPlatformId"]);
        if (puuid.Length == 0 || platform.Length == 0) throw new InvalidOperationException("请先登录客户端并选择大区。");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(puuid + "\n" + platform)));
    }
    private async Task<Snapshot> SnapshotAsync(CancellationToken token)
    {
        string before = await OwnerAsync(token).ConfigureAwait(false);
        var summary = await JsonAsync(Summary, token).ConfigureAwait(false);
        var regalia = (JObject)await JsonAsync(Regalia, token).ConfigureAwait(false);
        string after = await OwnerAsync(token).ConfigureAwait(false);
        if (before != after) throw new InvalidOperationException("读取期间账号或大区发生变化，请重新读取。");
        if (summary["selectedChallengesString"]?.Type != JTokenType.String || summary["title"] is not JObject ||
            summary["prestigeCrestBorderLevel"]?.Type != JTokenType.Integer || regalia["selectedPrestigeCrest"]?.Type != JTokenType.Integer ||
            regalia["preferredCrestType"]?.Type != JTokenType.String || regalia["preferredBannerType"]?.Type != JTokenType.String)
            throw new InvalidOperationException("身份装饰字段不兼容，未执行修改。");
        string title = Text(summary["title"]?["itemId"]);
        var badges = new JArray();
        foreach (string id in Text(summary["selectedChallengesString"]).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value <= 0 || badges.Count >= 3)
                throw new InvalidOperationException("当前勋章字段不兼容，未执行修改。");
            badges.Add(value);
        }
        var prefs = new JObject { ["challengeIds"] = badges, ["title"] = title is "0" or "-1" ? "" : title,
            ["bannerAccent"] = Text(summary["bannerId"]), ["crestBorder"] = Text(summary["crestId"]),
            ["prestigeCrestBorderLevel"] = (int)summary["prestigeCrestBorderLevel"]! };
        var settings = new JObject { ["preferredCrestType"] = regalia["preferredCrestType"]!.DeepClone(),
            ["preferredBannerType"] = regalia["preferredBannerType"]!.DeepClone(), ["selectedPrestigeCrest"] = regalia["selectedPrestigeCrest"]!.DeepClone() };
        if (_owner != before || _baseline == null) {
            _owner = before; _session = Guid.NewGuid().ToString("N"); _baseline = new(before, (JObject)prefs.DeepClone(), (JObject)settings.DeepClone()); _choices = [];
        }
        return new(before, prefs, settings);
    }
    private ClientIdentityState State(Snapshot snapshot) => new(_session, Text(snapshot.Preferences["title"]),
        Text(snapshot.Preferences["bannerAccent"]), (int)snapshot.Regalia["selectedPrestigeCrest"]!,
        ((JArray)snapshot.Preferences["challengeIds"]!).Select(x => x.Value<long>()).ToArray())
        { BorderLevel = (int)snapshot.Preferences["prestigeCrestBorderLevel"]! };
    internal static int BorderLevel(int id) => id switch { 1 => 1, 2 => 30, >= 3 and <= 21 => (id - 1) * 25, _ => throw new ArgumentOutOfRangeException(nameof(id)) };
    public async Task<ClientIdentityCatalog> ReadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var current = await SnapshotAsync(token).ConfigureAwait(false);
            var choices = new List<ClientIdentityChoice>(); var notices = new List<string>();
            async Task<JToken?> Optional(string endpoint) {
                try { return await JsonAsync(endpoint, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { notices.Add("部分目录未能读取：" + endpoint);return null; }
            }
            var user = await JsonAsync("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
            int level = user.Value<int?>("summonerLevel") ?? 0;
            // IDs follow the client's 21 themed-border resources, not account level numbers.
            for (int id = 1; id <= 21; id++) {
                int required = BorderLevel(id);
                choices.Add(new(ClientIdentityCategory.Border, id.ToString(CultureInfo.InvariantCulture), $"等级边框 · {required} 级", $"主题 {id} · 需要 {required} 级",
                    $"/fe/lol-static-assets/images/uikit/themed-borders/theme-{id}-border.png", level >= required));
            }
            var inventory = await Optional("/lol-regalia/v3/inventory/REGALIA_BANNER").ConfigureAwait(false) as JObject;
            var regalia = await Optional("/lol-game-data/assets/v1/regalia.json").ConfigureAwait(false);
            var banners = Objects(regalia).Where(x => Text(x["regaliaType"]) == "kBanner").ToList();
            if (inventory != null) foreach (var group in inventory.Properties()) banners.AddRange(Objects(group.Value["items"]));
            foreach (var item in banners.DistinctBy(x => Text(x["id"]))) {
                string id = Text(item["id"]);if (!Positive(id))continue;
                choices.Add(new(ClientIdentityCategory.Banner, id, Text(item["localizedName"]) is { Length: > 0 } name ? name : "旗帜 " + id,
                    Text(item["localizedDescription"]), Text(item["assetPath"]), inventory?[id]?["isOwned"]?.Value<bool?>()));
            }
            var ownedTitles = await Optional("/lol-challenges/v2/titles/local-player").ConfigureAwait(false);
            var owned = ownedTitles == null ? null : Objects(ownedTitles).Select(x => Text(x["itemId"])).ToHashSet(StringComparer.Ordinal);
            var titles = await Optional("/lol-challenges/v2/titles/all").ConfigureAwait(false);
            foreach (var item in Objects(titles ?? ownedTitles)) {
                string id = Text(item["itemId"]);if (!Positive(id))continue;
                choices.Add(new(ClientIdentityCategory.Title, id, Text(item["name"]) is { Length: > 0 } name ? name : "头衔 " + id,
                    Text(item["titleRequirementDescription"]), Text(item["iconPath"]), owned?.Contains(id)));
            }
            var definitions = await Optional("/lol-game-data/assets/v1/challenges.json").ConfigureAwait(false);
            var progress = await Optional("/lol-challenges/v1/challenges/local-player").ConfigureAwait(false) as JObject;
            if (definitions?["challenges"] is JObject challenges) foreach (var pair in challenges.Properties()) {
                if (pair.Value is not JObject item || !Positive(pair.Name) || item["levelToIconPath"] is not JObject icons || !icons.HasValues)continue;
                string tier = Text(progress?[pair.Name]?["currentLevel"]);
                string image = Text(icons[tier]);if (image.Length == 0)image = icons.Properties().Select(p => Text(p.Value)).FirstOrDefault(x => x.Length > 0) ?? "";
                choices.Add(new(ClientIdentityCategory.Badge, pair.Name, Text(item["name"]) is { Length: > 0 } name ? name : "勋章 " + pair.Name,
                    Text(item["descriptionShort"]) + (tier.Length > 0 ? " · 当前等级 " + tier : ""), image, progress == null ? null : tier.Length > 0 && tier != "NONE"));
            }
            choices.Add(new(ClientIdentityCategory.Title, "", "不展示头衔", "清空当前头衔", "", true));
            choices.Add(new(ClientIdentityCategory.Badge, "", "清空此勋章槽", "移除当前槽位的勋章", "", true));
            if (await OwnerAsync(token).ConfigureAwait(false) != current.Owner)throw new InvalidOperationException("账号或大区变化，请重新读取目录。");
            _choices = choices.DistinctBy(x => (x.Category, x.Id)).ToArray();
            return new(State(current), _choices, notices);
        } finally { _gate.Release(); }
    }
    private static bool Positive(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0;
    public async Task<byte[]?> GetImageAsync(ClientIdentityChoice choice, CancellationToken token = default)
    {
        string path = choice.ImagePath;
        if (path.Length == 0 || !(path.StartsWith("/lol-game-data/assets/", StringComparison.Ordinal) || path.StartsWith("/fe/lol-static-assets/", StringComparison.Ordinal)) ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#'))return null;
        var bytes = await lcu.GetBytesAsync(path, token).ConfigureAwait(false);
        if (bytes is { Length: > 0 } || choice.Category != ClientIdentityCategory.Border)return bytes;
        // CEF static resources are not served by every LCU build. This public
        // asset request uses a separate client and never sends local credentials.
        if (!int.TryParse(choice.Id, out int theme) || theme is < 1 or > 21)return null;
        try { return await PreviewClient.GetByteArrayAsync($"https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/uikit/themed-borders/theme-{theme}-border.png", token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }
    private async Task<(bool Accepted, string Code)> WriteAsync(string method, string path, JObject body, CancellationToken token)
    {
        if (lcu is ILcuResultRequestSender classified) {
            var reply = await classified.SendAsync(method, path, body.ToString(Formatting.None), token).ConfigureAwait(false);
            if (reply.Succeeded)return (true, "accepted");
            // Error bodies may include account identifiers. Classify without recording raw text.
            string error = Encoding.UTF8.GetString(reply.Data ?? []);
            if (reply.ServerErrorCode == "not_owned" || error.Contains("does not own", StringComparison.OrdinalIgnoreCase))return (false, "not_owned");
            if (reply.Failure is LcuFailure.Timeout or LcuFailure.Network or LcuFailure.Unexpected)throw new InvalidOperationException("身份装饰请求结果未知，请重新读取；未自动重试。");
            return (false, reply.StatusCode is { } status ? "http_" + (int)status : reply.Failure.ToString());
        }
        bool ok = method == "PUT" ? await lcu.PutAsync(path, body.ToString(Formatting.None), token).ConfigureAwait(false)
            : await lcu.PostAsync(path, body.ToString(Formatting.None), token).ConfigureAwait(false);
        return (ok, ok ? "accepted" : "rejected");
    }
    public async Task<ClientIdentityResult> ApplyAsync(string session, ClientIdentityCategory category, string id, int badgeSlot = 0, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            // Retain selection before a session change clears the old catalog.
            var selected = _choices.FirstOrDefault(x => x.Category == category && x.Id == id);
            var current = await SnapshotAsync(token).ConfigureAwait(false);
            if (session != _session)throw new InvalidOperationException("账号或大区已变化，请重新读取。");
            if (selected == null)throw new ArgumentException("请从当前客户端完整目录中选择装饰。");
            if (badgeSlot is < 0 or > 2)throw new ArgumentOutOfRangeException(nameof(badgeSlot));
            bool border = category == ClientIdentityCategory.Border;
            var desired = (JObject)current.Preferences.DeepClone();
            var desiredRegalia = (JObject)current.Regalia.DeepClone();
            switch (category) {
                // The legacy selectedPrestigeCrest setter clamps unearned themes.
                // The current identity preferences accept the actual level field.
                case ClientIdentityCategory.Border: desiredRegalia["preferredCrestType"] = "prestige";desired["prestigeCrestBorderLevel"] = BorderLevel(int.Parse(id, CultureInfo.InvariantCulture));break;
                case ClientIdentityCategory.Title: desired["title"] = id;break;
                case ClientIdentityCategory.Banner: desired["bannerAccent"] = id;break;
                case ClientIdentityCategory.Badge:
                    var ids = desired["challengeIds"]!.Values<long>().ToList();
                    while (ids.Count <= badgeSlot)ids.Add(0);
                    ids[badgeSlot] = id.Length == 0 ? 0 : long.Parse(id, CultureInfo.InvariantCulture);
                    desired["challengeIds"] = new JArray(ids.Where(value => value > 0));break;
                default:throw new ArgumentOutOfRangeException(nameof(category));
            }
            token.ThrowIfCancellationRequested();
            var crest = (Accepted: true, Code: "accepted");
            if (border && !JToken.DeepEquals(current.Regalia, desiredRegalia))crest = await WriteAsync("PUT", Regalia, desiredRegalia, token).ConfigureAwait(false);
            var write = crest.Accepted ? await WriteAsync("POST", Preferences, desired, token).ConfigureAwait(false) : crest;
            var after = await SnapshotAsync(token).ConfigureAwait(false);
            bool verified = write.Accepted && current.Owner == after.Owner && session == _session && JToken.DeepEquals(desired, after.Preferences) && JToken.DeepEquals(desiredRegalia, after.Regalia);
            string code = verified ? "verified" : current.Owner != after.Owner || session != _session ? "session_changed" : write.Accepted ? "readback_reverted" : write.Code;
            string message = verified ? "装饰显示偏好已更新，回读一致；请在客户端核对画面。" : code == "session_changed" ? "账号或大区已变化，请重新读取；未对新账号恢复旧设置。"
                : code == "not_owned" ? "客户端服务端拒绝：账号未拥有该装饰；原有设置以重新读取结果为准。"
                : code == "readback_reverted" ? "请求已受理，但客户端过滤或回滚了所选装饰，未视为应用成功。" : "装饰未应用：" + code;
            if (!verified && current.Owner == after.Owner && session == _session &&
                (!JToken.DeepEquals(current.Preferences, after.Preferences) || !JToken.DeepEquals(current.Regalia, after.Regalia))) {
                // A known filtered result can partially clear badge slots. Restore
                // the preceding snapshot, but never replay the failed selection.
                var undo = await WriteAsync("POST", Preferences, current.Preferences, token).ConfigureAwait(false);
                if (await OwnerAsync(token).ConfigureAwait(false) != current.Owner)throw new InvalidOperationException("账号变化，已停止恢复；请重新读取。");
                var undoRegalia = JToken.DeepEquals(current.Regalia, after.Regalia) ? (Accepted: true, Code: "unchanged")
                    : await WriteAsync("PUT", Regalia, current.Regalia, token).ConfigureAwait(false);
                after = await SnapshotAsync(token).ConfigureAwait(false);
                bool rolledBack = undo.Accepted && undoRegalia.Accepted && current.Owner == after.Owner &&
                    JToken.DeepEquals(current.Preferences, after.Preferences) && JToken.DeepEquals(current.Regalia, after.Regalia);
                message += rolledBack ? " 已恢复操作前设置。" : " 操作前设置未能全部恢复，请点击恢复原样并检查日志。";
            }
            diagnostic?.Invoke($"类别={category}; ID={id}; 拥有={selected.Owned?.ToString() ?? "未知"}; 结果={code}; 核验={verified}");
            return new(State(after), verified, code, message);
        } finally { _gate.Release(); }
    }
    public async Task<ClientIdentityResult> RestoreAsync(string session, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var current = await SnapshotAsync(token).ConfigureAwait(false);
            if (_baseline == null || session != _session)throw new InvalidOperationException("账号或大区已变化，不能恢复旧账号设置。");
            var baseline = _baseline;
            var first = await WriteAsync("POST", Preferences, baseline.Preferences, token).ConfigureAwait(false);
            if (await OwnerAsync(token).ConfigureAwait(false) != current.Owner)throw new InvalidOperationException("恢复期间账号发生变化，已停止后续写入。");
            var second = await WriteAsync("PUT", Regalia, baseline.Regalia, token).ConfigureAwait(false);
            var after = await SnapshotAsync(token).ConfigureAwait(false);
            bool verified = first.Accepted && second.Accepted && session == _session && baseline.Owner == after.Owner &&
                JToken.DeepEquals(baseline.Preferences, after.Preferences) && JToken.DeepEquals(baseline.Regalia, after.Regalia);
            diagnostic?.Invoke("恢复身份装饰; 核验=" + verified);
            return new(State(after), verified, verified ? "restored" : "restore_not_verified", verified ? "本次首次读取的身份装饰已恢复，回读一致。" : "恢复未全部通过核验，请重新读取当前设置。");
        } finally { _gate.Release(); }
    }
}
