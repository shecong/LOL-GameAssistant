using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Domain.Teams;

/// <summary>按本局 LCU 会话里的房间标识识别同队开黑，不查询历史战绩。</summary>
public static class CurrentPartyDetector
{
    /// <summary>根据当前输入执行组队关系识别。</summary>
    public static CurrentPartyDetectionResult Detect(
        IReadOnlyList<GameTeamMember> teamOne,
        IReadOnlyList<GameTeamMember> teamTwo)
    {
        var groups = new List<PremadeGroup>();
        bool[] complete = new bool[2];
        AddTeam(teamOne, 0);
        AddTeam(teamTwo, 1);
        return new CurrentPartyDetectionResult(new PremadeDetectionResult(groups), complete);

        // 将识别到的成员归入对应队伍的检测集合。
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

    /// <summary>取得玩家当前组队关系使用的标识。</summary>
    private static string? GetPartyKey(GameTeamMember member)
    {
        string partyId = member.PartyId?.Trim() ?? "";
        if (IsValidPartyId(partyId))
            return $"party:{partyId}";
        // teamParticipantId is participant metadata, not a confirmed party identifier.
        // Repeated values must never establish a current premade group.
        return null;
    }

    /// <summary>判断组队标识是否为有效值。</summary>
    internal static bool IsValidPartyId(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Trim() != "0" && !string.Equals(value.Trim(), Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>保留两队标识是否完整，避免缺失数据时把“未知”显示成“单排”。</summary>
public sealed class CurrentPartyDetectionResult
{
    private readonly bool[] _complete;

    /// <summary>初始化 CurrentPartyDetectionResult 的实例状态，并保存传入的依赖或数据。</summary>
    public CurrentPartyDetectionResult(PremadeDetectionResult groups, bool[] complete)
    {
        Groups = groups;
        _complete = complete;
    }

    public PremadeDetectionResult Groups { get; }

    /// <summary>判断当前组队信息是否不足，需要使用历史战绩补充。</summary>
    public bool NeedsHistoryFallback(int teamIndex) => !Groups.Groups.Any(group => group.TeamIndex == teamIndex);

    /// <summary>将历史推断结果补入当前组队结果。</summary>
    public CurrentPartyDetectionResult WithHistoryFallback(PremadeDetectionResult history)
    {
        var merged = Groups.Groups.Concat(history.Groups.Where(group => NeedsHistoryFallback(group.TeamIndex))
                .Select(group => group with { IsInferred = true }))
            .Select((group, index) => group with { Index = index + 1 }).ToArray();
        return new CurrentPartyDetectionResult(new PremadeDetectionResult(merged), _complete);
    }

    /// <summary>生成指定队伍的组队状态文本。</summary>
    public string GetTeamStatus(int teamIndex)
    {
        if (Groups.Groups.Any(group => group.TeamIndex == teamIndex))
            return Groups.Groups.Any(group => group.TeamIndex == teamIndex && group.IsInferred)
                ? $"疑似开黑·{Groups.GetTeamQueueStatus(teamIndex)}" : Groups.GetTeamQueueStatus(teamIndex);
        return _complete[teamIndex] ? "未见组队" : "未知";
    }

    /// <summary>生成指定队伍的组队关系详细说明。</summary>
    public string GetTeamDetail(int teamIndex)
    {
        var groups = Groups.Groups.Where(group => group.TeamIndex == teamIndex).ToArray();
        if (groups.Length == 0)
            return _complete[teamIndex]
                ? "当前对局会话中未发现同队玩家共享房间标识"
                : "当前阶段的玩家或房间标识不完整，暂无法判断";

        string detail = string.Join("；", groups.Select(group =>
            $"{group.Puuids.Count} 人{(group.IsInferred ? "近期同队" : "同组")}：{string.Join("、", group.Names)}"));
        if (groups.Any(group => group.IsInferred))
            return $"{detail}；依据近期20场至少2次同队，仅为历史推测，不能确认本局组队。";
        return _complete[teamIndex] ? detail : $"{detail}；其他玩家标识尚不完整";
    }
}
