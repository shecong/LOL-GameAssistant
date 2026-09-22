namespace LOL_GameAssistant.Domain.Teams;

/// <summary>近期同队关系推断出的开黑小组。</summary>
public sealed record PremadeGroup(
    int Index,
    IReadOnlyList<string> Puuids,
    IReadOnlyList<string> Names,
    int TeamIndex);

/// <summary>开黑检测结果及面向界面的摘要规则。</summary>
public sealed class PremadeDetectionResult
{
    public PremadeDetectionResult(IReadOnlyList<PremadeGroup> groups)
    {
        Groups = groups;
        GroupByPuuid = groups
            .SelectMany(group => group.Puuids.Select(puuid => (puuid, group)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
    }

    public IReadOnlyList<PremadeGroup> Groups { get; }

    public IReadOnlyDictionary<string, PremadeGroup> GroupByPuuid { get; }

    public string GetTeamSummary(int teamIndex)
    {
        var sizes = Groups.Where(group => group.TeamIndex == teamIndex)
            .Select(group => group.Puuids.Count)
            .OrderByDescending(size => size)
            .ToList();
        return sizes.Count == 0 ? "" : string.Join("+", sizes);
    }

    public string GetTeamQueueStatus(int teamIndex)
    {
        var sizes = Groups.Where(group => group.TeamIndex == teamIndex)
            .Select(group => group.Puuids.Count)
            .OrderByDescending(size => size)
            .ToList();
        if (sizes.Count == 0) return "单排";
        if (sizes.Count > 1) return $"多排 {string.Join("+", sizes)}";

        return sizes[0] switch
        {
            2 => "双排",
            3 => "三排",
            4 => "四排",
            5 => "五排",
            _ => "多排"
        };
    }

    public string GetTeamQueueDetail(int teamIndex)
    {
        var groups = Groups.Where(group => group.TeamIndex == teamIndex).ToList();
        return groups.Count == 0
            ? "近期战绩中未检测到固定同队关系"
            : string.Join("；", groups.Select(group =>
                $"{group.Puuids.Count} 人组队：{string.Join("、", group.Names)}"));
    }
}