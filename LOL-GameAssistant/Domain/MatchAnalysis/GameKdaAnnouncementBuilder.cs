namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>一名对局玩家的近期同模式 KDA 评估；没有可查结果时保留玩家并明确标注。</summary>
public sealed record GameKdaPlayerSummary(
    string Team,
    string DisplayName,
    RecentModePerformanceAssessment? Assessment);

/// <summary>将双方玩家压缩为适合游戏聊天框的短消息，保留全部玩家。</summary>
public static class GameKdaAnnouncementBuilder
{
    private const int MaximumMessageLength = 300;

    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players)
    {
        var messages = new List<string>();
        foreach (var team in players.GroupBy(player => player.Team))
        {
            string heading = $"【本局近期KDA·{team.Key}】";
            string current = heading;
            foreach (GameKdaPlayerSummary player in team)
            {
                string name = string.IsNullOrWhiteSpace(player.DisplayName) ? "未知玩家" : player.DisplayName.Trim();
                if (name.Length > 18) name = name[..18] + "…";
                string detail = player.Assessment is { } assessment
                    ? $"{name} {RecentPerformanceLabelFormatter.GetText(assessment)}{assessment.Score}分 KDA{assessment.Kda:F2}"
                    : $"{name} 近期KDA暂无可查";
                string separator = current == heading ? " " : "；";
                if (current.Length + separator.Length + detail.Length > MaximumMessageLength)
                {
                    messages.Add(current);
                    current = heading + "（续）";
                    separator = " ";
                }
                current += separator + detail;
            }
            if (current != heading) messages.Add(current);
        }
        return messages;
    }
}
