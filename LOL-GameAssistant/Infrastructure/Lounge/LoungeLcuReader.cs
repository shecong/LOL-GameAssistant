using LOL.Lounge;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Lounge;

public sealed record LoungeLocalState(LoungePresence Presence, string Phase, long GameId, int QueueId, bool Custom,
    string[]? PartyPuuids = null, Dictionary<string, int>? RankScores = null);

/// <summary>Always uses live local reads; never the UI's stale-data fallback caches.</summary>
public sealed class LoungeLcuReader(ILcuRequestSender lcu)
{
    private string _platform = "";
    private string _platformPuuid = "";
    private DateTimeOffset _nextPlatformAttempt;
    private string _rank = "";
    private string _rankPuuid = "";
    private DateTimeOffset _nextRankAttempt;
    private string[] _party = [];
    private int _partyQueue;
    private DateTimeOffset _partySeen;
    private Dictionary<string, int> _rankScores = [];

    public async Task<LoungeLocalState> ReadAsync(bool shareActivity, CancellationToken token)
    {
        // Do not use cached current-profile data for account changes/logout.
        JObject? player = await ReadObjectAsync("/lol-summoner/v1/current-summoner", token);
        string puuid = player?.Value<string>("puuid") ?? "";
        if (string.IsNullOrWhiteSpace(puuid))
        {
            _platform = _platformPuuid = "";
            _rank = _rankPuuid = "";
            _party = [];
            return new(new("", "未登录玩家", "", "未知大区", "未登录 LOL", shareActivity), "", 0, 0, false);
        }
        if (_platformPuuid != puuid || _platform.Length == 0 ||
            (RegionName(_platform).StartsWith("未知", StringComparison.Ordinal) && DateTimeOffset.UtcNow >= _nextPlatformAttempt))
        {
            // Region/platform metadata only. Never read or send RSO access tokens.
            string? raw = await lcu.GetStringAsync("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", token);
            _platform = ParsePlatform(raw);
            if (RegionName(_platform).StartsWith("未知", StringComparison.Ordinal))
            {
                // The existing match-history response also carries the requested account's platform.
                // Only use live response metadata, never a guessed tag line or old UI cache.
                var history = await ReadObjectAsync($"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex=0&endIndex=0", token);
                string platform = history?.Value<string>("platformId") ?? "";
                if (!string.IsNullOrWhiteSpace(platform)) _platform = platform;
            }
            if (_platform.Length == 0)
            {
                var locale = await ReadObjectAsync("/riotclient/region-locale", token);
                _platform = locale?.Value<string>("region") ?? "";
            }
            _platformPuuid = puuid;
            _nextPlatformAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
        }
        JObject? me = await ReadObjectAsync("/lol-chat/v1/me", token);
        if (_rankPuuid != puuid || DateTimeOffset.UtcNow >= _nextRankAttempt)
        {
            if (_rankPuuid != puuid) { _party = []; _rank = ""; }
            var ranked = await ReadObjectAsync("/lol-ranked/v1/current-ranked-stats", token);
            _rank = ReadRank(ranked);
            _rankScores = ReadRankScores(ranked);
            _rankPuuid = puuid;
            _nextRankAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
        }
        string? phaseRaw = await lcu.GetStringAsync("/lol-gameflow/v1/gameflow-phase", token);
        string phase = ParsePlatform(phaseRaw);
        JObject? session = await ReadObjectAsync("/lol-gameflow/v1/session", token);
        long gameId = session?.SelectToken("gameData.gameId")?.Value<long?>() ?? 0;
        int queue = session?.SelectToken("gameData.queue.id")?.Value<int?>() ?? 0;
        bool custom = session?.SelectToken("gameData.isCustomGame")?.Value<bool?>() ?? false;
        if (phase is "None" or "WaitingForStats" or "PreEndOfGame" or "EndOfGame") _party = [];
        if (phase is "Lobby" or "Matchmaking" or "ReadyCheck")
        {
            var lobby = await ReadObjectAsync("/lol-lobby/v2/lobby", token);
            _party = (lobby?["members"] as JArray)?.OfType<JObject>()
                .Select(m => m.Value<string>("puuid") ?? "").Where(p => p.Length > 0).Distinct().ToArray() ?? [];
            _partyQueue = lobby?.SelectToken("gameConfig.queueId")?.Value<int?>() ?? 0;
            _partySeen = DateTimeOffset.UtcNow;
        }
        string name = player?.Value<string>("gameName") ?? player?.Value<string>("displayName") ?? "玩家";
        string tag = player?.Value<string>("tagLine") ?? "";
        if (!string.IsNullOrWhiteSpace(tag)) name += "#" + tag;
        return new(new(puuid, name, _platform, RegionName(_platform), Status(me?.Value<string>("availability"), phase, shareActivity),
            shareActivity && !string.Equals(me?.Value<string>("availability"), "offline", StringComparison.OrdinalIgnoreCase), _rank),
            phase, gameId, queue, custom, _partyQueue == queue && _party.Contains(puuid) &&
                DateTimeOffset.UtcNow - _partySeen < TimeSpan.FromMinutes(15) ? _party.ToArray() : [], new(_rankScores));
    }

    public static Dictionary<string, int> ReadRankScores(JObject? data)
    {
        string[] tiers = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];
        var result = new Dictionary<string, int>();
        foreach (string queue in new[] { "RANKED_SOLO_5x5", "RANKED_FLEX_SR" })
        {
            var entry = data?["queueMap"]?[queue];
            int tier = Array.IndexOf(tiers, entry?.Value<string>("tier")?.ToUpperInvariant());
            int division = Array.IndexOf(new[] { "IV", "III", "II", "I" }, entry?.Value<string>("division"));
            if (tier >= 0 && (tier >= 7 || division >= 0)) result[queue] = tier * 4 + (tier >= 7 ? 0 : division);
        }
        return result;
    }

    public async Task<string[]> HighlightsAsync(long gameId, string puuid, CancellationToken token) =>
        ReadHighlights(await ReadObjectAsync($"/lol-match-history/v1/games/{gameId}", token), gameId, puuid);

    public static string[] ReadHighlights(JObject? detail, long gameId, string puuid)
    {
        if (detail?.Value<long?>("gameId") != gameId ||
            (detail.Value<string>("endOfGameResult") ?? "").StartsWith("Abort", StringComparison.OrdinalIgnoreCase)) return [];
        int? id = (detail["participantIdentities"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(p => p.SelectToken("player.puuid")?.Value<string>() == puuid)?.Value<int?>("participantId");
        if (id == null) return [];
        var stats = (detail["participants"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(p => p.Value<int?>("participantId") == id)?["stats"];
        var result = new List<string>();
        if (stats?.Value<int?>("pentaKills") > 0) result.Add("pentakill");
        if (stats?.Value<int?>("largestKillingSpree") >= 8) result.Add("legendary");
        return result.ToArray();
    }

    public static string ReadRank(JObject? data)
    {
        var labels = new Dictionary<string, string> { ["IRON"] = "黑铁", ["BRONZE"] = "青铜", ["SILVER"] = "白银",
            ["GOLD"] = "黄金", ["PLATINUM"] = "铂金", ["EMERALD"] = "翡翠", ["DIAMOND"] = "钻石",
            ["MASTER"] = "大师", ["GRANDMASTER"] = "宗师", ["CHALLENGER"] = "王者" };
        var result = new List<string>();
        foreach (var (queue, name) in new[] { ("RANKED_SOLO_5x5", "单双排"), ("RANKED_FLEX_SR", "灵活") })
        {
            var stats = data?["queueMap"]?[queue];
            string tier = stats?.Value<string>("tier")?.ToUpperInvariant() ?? "";
            if (labels.TryGetValue(tier, out var label))
                result.Add($"{name} {label}{stats?.Value<string>("division")} {stats?.Value<int?>("leaguePoints") ?? 0}点");
        }
        return string.Join(" / ", result);
    }

    public async Task<string[]> PartyNamesAsync(long gameId, string puuid, string[] party, CancellationToken token) =>
        ReadPartyNames(await ReadObjectAsync($"/lol-match-history/v1/games/{gameId}", token), gameId, puuid, party);

    public static string[] ReadPartyNames(JObject? detail, long gameId, string puuid, string[] party)
    {
        if (detail?.Value<long?>("gameId") != gameId || ReadVictory(detail, gameId, puuid) != true) return [];
        var identities = (detail["participantIdentities"] as JArray)?.OfType<JObject>().ToArray() ?? [];
        var participants = (detail["participants"] as JArray)?.OfType<JObject>().ToArray() ?? [];
        int? self = identities.FirstOrDefault(p => p.SelectToken("player.puuid")?.Value<string>() == puuid)?.Value<int?>("participantId");
        int? team = participants.FirstOrDefault(p => p.Value<int?>("participantId") == self)?.Value<int?>("teamId");
        if (self == null || team == null) return [];
        return identities.Where(p => p.SelectToken("player.puuid")?.Value<string>() is { } id && id != puuid && party.Contains(id) &&
                participants.Any(t => t.Value<int?>("participantId") == p.Value<int?>("participantId") && t.Value<int?>("teamId") == team))
            .Select(p =>
            {
                var player = p["player"]!;
                string name = player.Value<string>("gameName") ?? player.Value<string>("summonerName") ?? "";
                string tag = player.Value<string>("tagLine") ?? "";
                return name.Length > 0 && tag.Length > 0 ? name + "#" + tag : name;
            }).Where(n => n.Length > 0).Distinct().Take(4).ToArray();
    }

    public async Task<bool?> WonAsync(long gameId, string puuid, CancellationToken token)
    {
        JObject? detail = await ReadObjectAsync($"/lol-match-history/v1/games/{gameId}", token);
        return ReadVictory(detail, gameId, puuid);
    }

    public Task<JObject?> MatchAsync(long gameId, CancellationToken token) =>
        ReadObjectAsync($"/lol-match-history/v1/games/{gameId}", token);

    public static bool? ReadVictory(JObject? detail, long gameId, string puuid)
    {
        if (detail == null || detail.Value<long?>("gameId") != gameId) return null;
        if ((detail.Value<string>("endOfGameResult") ?? "").StartsWith("Abort", StringComparison.OrdinalIgnoreCase)) return false;
        int? participant = (detail["participantIdentities"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(p => p.SelectToken("player.puuid")?.Value<string>() == puuid)?.Value<int?>("participantId");
        if (participant is null) return null;
        var stats = (detail["participants"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(p => p.Value<int?>("participantId") == participant)?["stats"];
        return stats?["win"]?.Type == JTokenType.Boolean ? stats.Value<bool>("win") : null;
    }

    private async Task<JObject?> ReadObjectAsync(string path, CancellationToken token)
    {
        string? json = await lcu.GetStringAsync(path, token);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }

    private static string ParsePlatform(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try { return JToken.Parse(json).Type == JTokenType.String ? JToken.Parse(json).Value<string>() ?? "" : ""; }
        catch (Newtonsoft.Json.JsonException) { return ""; }
    }

    public static string Status(string? availability, string phase, bool share)
    {
        if (!share || availability == "offline") return "活动已隐藏";
        if (availability == "away") return "离开";
        if (availability == "dnd") return "忙碌";
        return phase switch
        {
            "Matchmaking" => "匹配中", "ReadyCheck" => "确认对局", "ChampSelect" => "选人中",
            "InProgress" or "GameStart" or "Reconnect" => "游戏中",
            "WaitingForStats" or "PreEndOfGame" or "EndOfGame" => "结算中",
            "Lobby" => "大厅中", "None" => "在线", _ => "状态未知"
        };
    }

    public static string RegionName(string platform) => platform.ToUpperInvariant() switch
    {
        "HN1" => "艾欧尼亚", "HN2" => "祖安", "HN3" => "诺克萨斯", "HN4" => "班德尔城",
        "HN5" => "皮尔特沃夫", "HN6" => "战争学院", "HN7" => "巨神峰", "HN8" => "雷瑟守备",
        "HN9" => "裁决之地", "HN10" => "黑色玫瑰", "HN11" => "暗影岛", "HN12" => "钢铁烈阳",
        "HN13" => "水晶之痕", "HN14" => "均衡教派", "HN15" => "影流", "HN16" => "守望之海",
        "HN17" => "征服之海", "HN18" => "卡拉曼达", "HN19" => "皮城警备",
        "WT1" => "比尔吉沃特", "WT2" => "德玛西亚", "WT3" => "弗雷尔卓德", "WT4" => "无畏先锋",
        "WT5" => "恕瑞玛", "WT6" => "扭曲丛林", "WT7" => "巨龙之巢", "EDU1" => "教育网专区",
        "BGP1" => "男爵领域", "BGP2" => "峡谷之巅", "KR" => "韩服", "NA1" => "北美",
        "EUW1" => "欧洲西部", "EUN1" => "欧洲北部", "JP1" => "日服",
        "" => "未知大区", _ => "未知大区（" + LoungeRules.Clean(platform, 20) + "）"
    };
}
