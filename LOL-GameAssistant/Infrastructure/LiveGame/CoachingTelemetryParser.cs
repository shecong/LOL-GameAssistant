using LOL_GameAssistant.Domain.Coaching;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LiveGame;

internal static class CoachingTelemetryParser
{
    internal static LocalLiveClientSnapshot? Parse(string content)
    {
        var root = JObject.Parse(content);
        if (root["activePlayer"] is not JObject active || root["gameData"] is not JObject game ||
            root["allPlayers"] is not JArray roster || Number(game["gameTime"]) is not { } time) return null;
        var players = roster.OfType<JObject>().Take(16).ToArray();
        string Identity(JObject p) => Text(p["riotId"]) is { Length: > 0 } riot ? riot :
            Text(p["riotIdGameName"]) is { Length: > 0 } name && Text(p["riotIdTagLine"]) is { Length: > 0 } tag
                ? name + "#" + tag : Text(p["summonerName"]);
        string identity = Identity(active);
        var matching = players.Where(p => identity.Length > 0 && Identity(p) == identity).ToArray();
        if (matching.Length != 1 && Text(active["summonerName"]) is { Length: > 0 } summoner)
            matching = players.Where(p => Text(p["summonerName"]) == summoner).ToArray();
        if (matching.Length != 1) return null; // 不猜玩家，避免把队友装备当作自己的。
        JObject me = matching[0];
        string team = Text(me["team"]);
        if (team.Length == 0) return null;
        var observed = players.Select((p, index) => new ObservedPlayer
        {
            Key = "p" + index, Champion = Text(p["championName"]), Position = Text(p["position"]), IsSelf = ReferenceEquals(p, me), IsAlly = Text(p["team"]) == team,
            Level = Integer(p["level"]), IsDead = p["isDead"]?.Type == JTokenType.Boolean ? p["isDead"]!.Value<bool>() : null,
            RespawnSeconds = Integer(p["respawnTimer"]), Kills = Integer(p["scores"]?["kills"]),
            Deaths = Integer(p["scores"]?["deaths"]), Assists = Integer(p["scores"]?["assists"]),
            CreepScore = Integer(p["scores"]?["creepScore"]), VisionScore = Number(p["scores"]?["wardScore"]),
            HasItems = p["items"] is JArray,
            Items = (p["items"] as JArray)?.OfType<JObject>().Take(8).Select(i =>
                new ObservedItem(Integer(i["itemID"]) ?? 0, Text(i["displayName"]), Integer(i["count"]) ?? 1)).ToArray() ?? [],
            SummonerSpells = Names(p["summonerSpells"] as JObject), Runes = Names(p["runes"] as JObject)
        }).ToArray();
        // 将事件中的玩家名映射为席位；野怪或无法确认的来源统一置空。
        string Seat(JToken? value)
        {
            string name = Text(value);
            var matches = players.Select((p, i) => (p, i)).Where(x => name.Length > 0 &&
                (Identity(x.p) == name || Text(x.p["summonerName"]) == name)).ToArray();
            return matches.Length == 1 ? "p" + matches[0].i : "";
        }
        string[] kinds = ["ChampionKill", "DragonKill", "BaronKill", "HeraldKill", "HordeKill", "TurretKilled", "InhibKilled", "InhibRespawned", "Ace"];
        var events = (root["events"]?["Events"] as JArray)?.OfType<JObject>()
            .Where(e => kinds.Contains(Text(e["EventName"]), StringComparer.Ordinal) &&
                Number(e["EventTime"]) is { } at && at >= Math.Max(0, time - 180) && at <= time)
            .TakeLast(24).Select(e => new ObservedGameEvent(Integer(e["EventID"]) ?? 0, Text(e["EventName"]),
                Integer(e["EventTime"]) ?? 0, Seat(e["KillerName"]), Seat(e["VictimName"]))).ToArray() ?? [];
        JObject? stats = active["championStats"] as JObject;
        var telemetry = new CoachingTelemetry
        {
            Gold = Integer(active["currentGold"]), Level = Integer(active["level"]),
            Health = Number(stats?["currentHealth"]), MaxHealth = Number(stats?["maxHealth"]),
            ResourceType = Text(stats?["resourceType"]), Resource = Number(stats?["resourceValue"]), MaxResource = Number(stats?["resourceMax"]),
            Abilities = (active["abilities"] as JObject)?.Properties().Where(p => p.Value is JObject && p.Name is "Q" or "W" or "E" or "R")
                .Where(p => Integer(p.Value["abilityLevel"]) != null)
                .Select(p => new ObservedAbility(p.Name, Text(p.Value["displayName"]), Integer(p.Value["abilityLevel"])!.Value)).ToArray() ?? [],
            Players = observed, Events = events, HasEvents = root["events"]?["Events"] is JArray,
            HasCompleteRoster = players.Length == roster.Count && players.All(p => Text(p["team"]).Length > 0 && Text(p["championName"]).Length > 0)
        };
        return new LocalLiveClientSnapshot(telemetry.Gold ?? 0,
            telemetry.Self!.Items.Select(i => i.Name.Length > 0 ? i.Name : "#" + i.Id).ToArray(),
            (int)Math.Clamp(time, 0, int.MaxValue), Text(game["gameMode"])) { Telemetry = telemetry };
    }

    private static IReadOnlyList<string> Names(JObject? obj) => obj?.Properties().Where(p => p.Value is JObject)
        .Select(p => Text(p.Value["displayName"])).Where(n => n.Length > 0).Take(6).ToArray() ?? [];
    private static string Text(JToken? t) => t?.Type == JTokenType.String ? (t.Value<string>() ?? "")[..Math.Min(t.Value<string>()?.Length ?? 0, 100)] : "";
    private static double? Number(JToken? t) => t?.Type is JTokenType.Float or JTokenType.Integer &&
        double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= 0 ? n : null;
    private static int? Integer(JToken? t) => Number(t) is { } n && n <= int.MaxValue ? (int)n : null;
}
