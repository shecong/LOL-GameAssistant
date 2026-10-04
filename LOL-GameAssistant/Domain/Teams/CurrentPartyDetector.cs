using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Domain.Teams;

/// <summary>按本局 LCU 会话里的房间标识识别同队开黑，不查询历史战绩。</summary>
public static class CurrentPartyDetector
{
    public static CurrentPartyDetectionResult Detect(
        IReadOnlyList<GameTeamMember> teamOne,
        IReadOnlyList<GameTeamMember> teamTwo)
    {
        var groups = new List<PremadeGroup>();
        bool[] complete = new bool[2];
        AddTeam(teamOne, 0);
        AddTeam(teamTwo, 1);
        return new CurrentPartyDetectionResult(new PremadeDetectionResult(groups), complete);

        void AddTeam(IReadOnlyList<GameTeamMember> team, int teamIndex)
        {
            GameTeamMember[] players = team.Where(member => !member.IsBot)
                .DistinctBy(member => member.Puuid, StringComparer.Ordinal)
                .ToArray();
            complete[teamIndex] = players.Length >= 2 && players.All(member =>
                !string.IsNullOrWhiteSpace(member.Puuid) && GetPartyKey(member) != null);

            foreach (var party in players
                .Where(member => !string.IsNullOrWhiteSpace(member.Puuid))
                .Select(member => (Member: member, Key: GetPartyKey(member)))
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!, StringComparer.Ordinal)
                .Where(group => group.Count() >= 2)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                groups.Add(new PremadeGroup(
                    groups.Count + 1,
                    party.Select(item => item.Member.Puuid).ToArray(),
                    party.Select(item => string.IsNullOrWhiteSpace(item.Member.SummonerName)
                        ? "未知玩家" : item.Member.SummonerName).ToArray(),
                    teamIndex));
            }
        }
    }

    private static string? GetPartyKey(GameTeamMember member)
    {
        string partyId = member.PartyId?.Trim() ?? "";
        if (partyId.Length > 0 && partyId != "0" &&
            !string.Equals(partyId, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase))
            return $"party:{partyId}";
        // teamParticipantId is participant metadata, not a confirmed party identifier.
        // Repeated values must never establish a current premade group.
        return null;
    }
}

/// <summary>保留两队标识是否完整，避免缺失数据时把“未知”显示成“单排”。</summary>
public sealed class CurrentPartyDetectionResult
{
    private readonly bool[] _complete;

    public CurrentPartyDetectionResult(PremadeDetectionResult groups, bool[] complete)
    {
        Groups = groups;
        _complete = complete;
    }

    public PremadeDetectionResult Groups { get; }

    public string GetTeamStatus(int teamIndex)
    {
        if (Groups.Groups.Any(group => group.TeamIndex == teamIndex))
            return Groups.GetTeamQueueStatus(teamIndex);
        return _complete[teamIndex] ? "未见组队" : "未知";
    }

    public string GetTeamDetail(int teamIndex)
    {
        var groups = Groups.Groups.Where(group => group.TeamIndex == teamIndex).ToArray();
        if (groups.Length == 0)
            return _complete[teamIndex]
                ? "当前对局会话中未发现同队玩家共享房间标识"
                : "当前阶段的玩家或房间标识不完整，暂无法判断";

        string detail = string.Join("；", groups.Select(group =>
            $"{group.Puuids.Count} 人同组：{string.Join("、", group.Names)}"));
        return _complete[teamIndex] ? detail : $"{detail}；其他玩家标识尚不完整";
    }
}
