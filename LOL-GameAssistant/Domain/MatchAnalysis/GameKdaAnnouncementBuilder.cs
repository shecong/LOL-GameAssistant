namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>一名对局玩家的近期同模式 KDA 评估；没有可查结果时保留玩家并明确标注。</summary>
public sealed record GameKdaPlayerSummary(
    string Team,
    string DisplayName,
    RecentModePerformanceAssessment? Assessment);

/// <summary>按蓝方、红方顺序为每名玩家生成一条游戏聊天消息。</summary>
public static class GameKdaAnnouncementBuilder
{
    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players)
    {
        return players.GroupBy(player => player.Team)
            .SelectMany(team => team.Select(player => FormatPlayer(team.Key, player)))
            .ToArray();
    }

    private static string FormatPlayer(string team, GameKdaPlayerSummary player)
    {
        // 游戏聊天以 Enter 提交消息；先清掉名字中的换行，保证一名玩家只发送一条。
        string name = string.IsNullOrWhiteSpace(player.DisplayName)
            ? "未知玩家"
            : string.Join(" ", player.DisplayName.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 32) name = name[..32] + "…";
        string prefix = $"【{team}近期KDA】{name} ";
        if (player.Assessment is not { } assessment)
            return prefix + "近期KDA暂无可查";
        string label = RecentPerformanceLabelFormatter.GetText(assessment);
        return $"{prefix}{label}{assessment.Score}分 KDA{assessment.Kda:F2}";
    }
}
