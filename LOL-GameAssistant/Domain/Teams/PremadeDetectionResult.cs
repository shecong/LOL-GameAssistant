namespace LOL_GameAssistant.Domain.Teams;

/// <summary>同队分组；来源可为本局房间标识或近期同队推断。</summary>
public sealed record PremadeGroup(
    int Index,
    IReadOnlyList<string> Puuids,
    IReadOnlyList<string> Names,
    int TeamIndex,
    bool IsInferred = false);

/// <summary>开黑检测结果及面向界面的摘要规则。</summary>
public sealed class PremadeDetectionResult
{
    /// <summary>初始化 PremadeDetectionResult 的实例状态，并保存传入的依赖或数据。</summary>
    public PremadeDetectionResult(IReadOnlyList<PremadeGroup> groups)
    {
        Groups = groups;
        GroupByPuuid = groups
            .SelectMany(group => group.Puuids.Select(puuid => (puuid, group)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
    }

    public IReadOnlyList<PremadeGroup> Groups { get; }

    public IReadOnlyDictionary<string, PremadeGroup> GroupByPuuid { get; }

    /// <summary>生成队伍组队情况的简要说明。</summary>
    public string GetTeamSummary(int teamIndex)
    {
        var sizes = Groups.Where(group => group.TeamIndex == teamIndex)
            .Select(group => group.Puuids.Count)
            .OrderByDescending(size => size)
            .ToList();
        return sizes.Count == 0 ? "" : string.Join("+", sizes);
    }

    /// <summary>生成队伍中单排、双排等组队状态文本。</summary>
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

    /// <summary>历史战绩只能推断一起排队的可能性，不能断定当局的房间关系。</summary>
    public string GetInferredTeamStatus(int teamIndex)
    {
        string status = GetTeamQueueStatus(teamIndex);
        return status == "单排" ? "未发现" : $"疑似{status}";
    }

    /// <summary>生成队伍组队情况的详细提示。</summary>
    public string GetTeamQueueDetail(int teamIndex)
    {
        var groups = Groups.Where(group => group.TeamIndex == teamIndex).ToList();
        return groups.Count == 0
            ? "近期战绩中未检测到固定同队关系"
            : string.Join("；", groups.Select(group =>
                $"{group.Puuids.Count} 人组队：{string.Join("、", group.Names)}"));
    }
}
