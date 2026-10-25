using LOL_GameAssistant.Domain.Matches;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Sgp;

internal static class SgpMatchMapper
{
    internal static (MatchHistoryGame Summary, MatchDetail Detail) Map(JToken envelope, string platform)
    {
        var json = envelope["json"] as JObject ?? throw new IOException("SGP 对局缺少 SUMMARY 数据。");
        long id = json.Value<long?>("gameId") ?? 0;
        if (id <= 0 || json["participants"] is not JArray players || players.Count == 0)
            throw new IOException("SGP 对局数据不完整。");
        string actualPlatform = json.Value<string>("platformId") ?? platform;
        if (!string.Equals(platform, actualPlatform, StringComparison.OrdinalIgnoreCase)) throw new IOException("SGP 返回了其他大区的对局。");
        var summary = new MatchHistoryGame
        {
            HasFullParticipants = true,
            PlatformId = platform,
            GameId = id, GameCreation = json.Value<long?>("gameCreation") ?? 0,
            GameDuration = json.Value<int?>("gameDuration") ?? 0,
            GameMode = json.Value<string>("gameMode") ?? "", GameType = json.Value<string>("gameType") ?? "",
            QueueId = json.Value<int?>("queueId") ?? 0, EndOfGameResult = json.Value<string>("endOfGameResult") ?? ""
        };
        var participantIds = new HashSet<int>();
        foreach (JObject p in players)
        {
            int pid = p.Value<int?>("participantId") ?? 0;
            if (pid <= 0 || !participantIds.Add(pid)) throw new IOException("SGP 参赛者标识无效或重复。");
            var stats = p.ToObject<MatchParticipantStats>()!;
            stats.HasWinResult = p["win"]?.Type == JTokenType.Boolean;
            stats.HasKdaResult = p["kills"] != null && p["deaths"] != null && p["assists"] != null;
            stats.HasFarmResult = p["totalMinionsKilled"] != null && p["neutralMinionsKilled"] != null;
            stats.HasDamageResult = p["totalDamageDealtToChampions"] != null;
            stats.HasVisionResult = p["visionScore"] != null;
            stats.totalTimeCrowdControlDealt = p.Value<int?>("totalTimeCCDealt") ?? stats.totalTimeCrowdControlDealt;
            stats.AugmentIds = Enumerable.Range(1, 6).Select(i => p.Value<int?>("playerAugment" + i) ?? 0).Where(i => i > 0).ToList();
            stats.RuneIds = p.SelectTokens("perks.styles[*].selections[*].perk").Select(v => v.Value<int>())
                .Concat((p.SelectToken("perks.statPerks") as JObject)?.Properties().Select(v => v.Value.Value<int>()) ?? []).Where(i => i > 0).ToList();
            summary.Participants.Add(new MatchParticipant { participantId = pid, championId = p.Value<int?>("championId") ?? 0,
                teamId = p.Value<int?>("teamId") ?? 0, Spell1Id = p.Value<int?>("spell1Id") ?? p.Value<int?>("summoner1Id") ?? 0,
                Spell2Id = p.Value<int?>("spell2Id") ?? p.Value<int?>("summoner2Id") ?? 0, stats = stats });
            summary.ParticipantIdentities.Add(new MatchIdentity { ParticipantId = pid, Player = new MatchPlayer
            {
                Puuid = p.Value<string>("puuid") ?? "", GameName = p.Value<string>("riotIdGameName") ?? p.Value<string>("summonerName") ?? "",
                SummonerName = p.Value<string>("summonerName") ?? "", TagLine = p.Value<string>("riotIdTagline") ?? ""
            } });
        }
        var detail = new MatchDetail
        {
            HasFullParticipants = true,
            PlatformId = platform,
            GameId = id, endOfGameResult = summary.EndOfGameResult, gameCreationDate = summary.GameCreation > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(summary.GameCreation).ToString("O") : null,
            gameDuration = summary.GameDuration, gameMode = summary.GameMode, queueId = summary.QueueId.ToString(),
            _queueId = summary.QueueId.ToString(), participants = summary.Participants,
            participantIdentities = summary.ParticipantIdentities.Select(p => new MatchParticipantIdentity { participantId = p.ParticipantId, player = p.Player }).ToList(),
            teams = (json["teams"] as JArray ?? []).Select(t => new MatchTeam { TeamId = t.Value<int>("teamId"),
                BannedChampionIds = (t["bans"] as JArray ?? []).OrderBy(b => b.Value<int?>("pickTurn") ?? 0)
                    .Select(b => b.Value<int?>("championId") ?? 0).Where(c => c > 0).ToList() }).ToList()
        };
        return (summary, detail);
    }
}
