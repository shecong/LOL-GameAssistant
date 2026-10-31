using LOL_GameAssistant.Helper;
using System.Collections.Concurrent;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>比较官方资源快照，不把资源差异宣称为国服补丁说明或强度结论。</summary>
public sealed class DataDragonPatchWatchService(HttpClient? client = null) : IPatchWatchService
{
    private static readonly HttpClient Shared = new(NetworkTrafficService.Handler(NetworkModule.Patch)) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient _http = client ?? Shared;
    private readonly ConcurrentDictionary<string, JToken> _cache = new();
    private readonly SemaphoreSlim _gate = new(3, 3);
    private async Task<JToken> Get(string path, CancellationToken token)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(path, out cached)) return cached;
            var value = JToken.Parse(await _http.GetStringAsync("https://ddragon.leagueoflegends.com/" + path, token).ConfigureAwait(false));
            if (_cache.Count > 80) _cache.Clear();
            _cache[path] = value; return value;
        }
        finally { _gate.Release(); }
    }
    public async Task<PatchWatchResult> CheckAsync(PlayerExperience previous, IEnumerable<int> champions, IEnumerable<int> items, CancellationToken token = default, IEnumerable<int>? runes = null)
    {
        var versions = await Get("api/versions.json", token).ConfigureAwait(false);
        // Version discovery is refreshed for the next explicit check; versioned assets remain cached.
        _cache.TryRemove("api/versions.json", out _);
        string version = versions[0]?.Value<string>() ?? throw new InvalidDataException("资源版本暂不可用");
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new InvalidDataException("资源版本格式异常");
        string prefix = $"cdn/{version}/data/zh_CN/";
        var index = await Get(prefix + "champion.json", token).ConfigureAwait(false);
        var ids = champions.Where(id => id > 0).Distinct().Take(30).ToHashSet();
        var assets = new ConcurrentDictionary<string, TrackedPatchAsset>();
        await Task.WhenAll((index["data"] as JObject ?? new()).Properties().Where(p => ids.Contains(p.Value.Value<int?>("key") ?? 0)).Select(async entry =>
        {
            string id = entry.Value.Value<string>("id")!;
            var details = await Get(prefix + "champion/" + Uri.EscapeDataString(id) + ".json", token).ConfigureAwait(false);
            var data = details["data"]?[id] ?? throw new InvalidDataException("英雄资源不完整");
            var fields = new Dictionary<string, string>();
            foreach (var stat in (data["stats"] as JObject ?? new()).Properties()) fields["基础属性." + StatName(stat.Name)] = stat.Value.ToString(Newtonsoft.Json.Formatting.None);
            fields["被动说明"] = data.SelectToken("passive.description")?.ToString() ?? "";
            int order = 0;
            foreach (var spell in (data["spells"] ?? new JArray()).Take(4))
            {
                string key = "QWER"[order++].ToString();
                foreach (string field in new[] { "description", "cooldownBurn", "costBurn", "rangeBurn", "effectBurn" })
                    fields[key + "." + (field switch { "description" => "说明", "cooldownBurn" => "冷却", "costBurn" => "消耗", "rangeBurn" => "范围", _ => "等级数值" })] = spell[field]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            }
            assets["champion:" + entry.Value.Value<int>("key")] = new(data.Value<string>("name") ?? id, fields);
        })).ConfigureAwait(false);
        foreach (int id in ids)
            assets.TryAdd("champion:" + id, new($"英雄 #{id}", new() { ["可用性"] = "此资源版本未收录" }));
        var itemIds = items.Where(id => id > 0).Distinct().Take(100).ToArray();
        if (itemIds.Length > 0)
        {
            var catalog = await Get(prefix + "item.json", token).ConfigureAwait(false);
            foreach (int id in itemIds)
            {
                var data = catalog["data"]?[id.ToString()];
                assets["item:" + id] = data == null ? new($"装备 #{id}", new() { ["可用性"] = "此资源版本未收录" }) :
                    new(data.Value<string>("name") ?? $"装备 #{id}", new() { ["说明"] = data["description"]?.ToString() ?? "", ["价格"] = data["gold"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "" });
            }
        }
        var runeIds = (runes ?? []).Where(id => id > 0).Distinct().Take(100).ToHashSet();
        if (runeIds.Count > 0)
        {
            var catalog = await Get(prefix + "runesReforged.json", token).ConfigureAwait(false);
            foreach (var style in catalog)
            {
                foreach (var rune in new[] { style }.Concat((style["slots"] ?? new JArray()).SelectMany(slot => slot["runes"] ?? new JArray())))
                {
                    int id = rune.Value<int?>("id") ?? 0;
                    if (!runeIds.Contains(id)) continue;
                    assets["rune:" + id] = new(rune.Value<string>("name") ?? $"符文 #{id}", new() { ["说明"] = rune["longDesc"]?.ToString() ?? rune["shortDesc"]?.ToString() ?? "" });
                }
            }
            foreach (int id in runeIds) assets.TryAdd("rune:" + id, new($"符文 #{id}", new() { ["可用性"] = "此资源版本未收录，可能为属性碎片" }));
        }
        var changes = Compare(previous.PatchAssets, assets);
        return new(previous.PatchVersion, version, changes, new Dictionary<string, TrackedPatchAsset>(assets),
            "Data Dragon 中文资源差异；可能含文案修正，数值和上线时间以当前国服客户端为准。最多检查 30 位主练英雄、100 件个人方案装备和100项符文。");
    }
    public static IReadOnlyList<string> Compare(IReadOnlyDictionary<string, TrackedPatchAsset> before, IEnumerable<KeyValuePair<string, TrackedPatchAsset>> after)
    {
        var changes = new List<string>();
        foreach (var (key, asset) in after.OrderBy(p => p.Key))
        {
            if (!before.TryGetValue(key, out var old)) { changes.Add(asset.Name + "：新加入关注，确认后建立比较基准。"); continue; }
            var fields = asset.Values.Keys.Union(old.Values.Keys).Where(f => asset.Values.GetValueOrDefault(f) != old.Values.GetValueOrDefault(f)).ToArray();
            static string Brief(string? text)
            {
                string clean = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text ?? "未收录", "<[^>]+>", " ")).Trim();
                return clean.Length > 130 ? clean[..130] + "…" : clean;
            }
            if (fields.Length > 0) changes.Add(asset.Name + "：\n" + string.Join("\n", fields.Select(field =>
                $"  {field}：{Brief(old.Values.GetValueOrDefault(field))} → {Brief(asset.Values.GetValueOrDefault(field))}")));
        }
        return changes;
    }
    private static string StatName(string key) => key switch
    {
        "hp" => "生命值", "hpperlevel" => "生命成长", "mp" => "法力值", "mpperlevel" => "法力成长",
        "movespeed" => "移动速度", "armor" => "护甲", "armorperlevel" => "护甲成长", "spellblock" => "魔法抗性", "spellblockperlevel" => "魔抗成长",
        "attackrange" => "攻击距离", "hpregen" => "生命回复", "hpregenperlevel" => "生命回复成长", "mpregen" => "法力回复", "mpregenperlevel" => "法力回复成长",
        "crit" => "暴击率", "critperlevel" => "暴击成长", "attackdamage" => "攻击力", "attackdamageperlevel" => "攻击成长", "attackspeed" => "攻击速度", "attackspeedperlevel" => "攻速成长", _ => key
    };
}
