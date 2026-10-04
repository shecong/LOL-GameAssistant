namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>一名对局玩家的近期同模式 KDA 评估；没有可查结果时保留玩家并明确标注。</summary>
public sealed record GameKdaPlayerSummary(
    string Team,
    string DisplayName,
    RecentModePerformanceAssessment? Assessment);

/// <summary>默认按队伍汇总为短消息；仅逐人模式为每名玩家发送一条。</summary>
public static class GameKdaAnnouncementBuilder
{
    private const int MaximumTeamMessageLength = 120;
    private const string PlayerSeparator = "      ";

    // 2560×1440 默认聊天栏实测：所有人频道前缀后，正文约 30 个半角宽度仍可单行显示。
    private const int MaximumBodyWidth = 30;

    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players,
        bool onePlayerPerLine = false)
    {
        return players.GroupBy(player => player.Team)
            .SelectMany(team => onePlayerPerLine
                ? team.Select(player => FormatPlayer(team.Key, player))
                : BuildTeamMessages(team.Key, team.ToArray()))
            .ToArray();
    }

    private static IEnumerable<string> BuildTeamMessages(string team, IReadOnlyList<GameKdaPlayerSummary> players)
    {
        // 五人队伍必须一次生成完整汇总，不能按逐个加入时的长度提前拆开，
        // 否则会出现“红方四人”之后又发送“红方最后一人”的额外消息。
        foreach (GameKdaPlayerSummary[] batch in players.Chunk(5))
            yield return BuildTeamMessage(team, batch);
    }

    private static string BuildTeamMessage(string team, IReadOnlyList<GameKdaPlayerSummary> players)
    {
        string heading = $"【本局近期KDA·{team}】 ";
        foreach ((int nameLimit, bool compact, string separator, bool terse) in new[]
                 { (18, false, PlayerSeparator, false), (12, false, PlayerSeparator, false),
                   (10, true, PlayerSeparator, false), (8, true, PlayerSeparator, false),
                   (6, true, PlayerSeparator, false), (6, true, "  ", true), (4, true, "  ", true) })
        {
            string message = heading + string.Join(separator,
                players.Select(player => FormatTeamPlayer(player, nameLimit, compact, terse)));
            if (message.Length <= MaximumTeamMessageLength) return message;
        }

        return heading + string.Join(" ",
            players.Select(player => FormatTeamPlayer(player, 2, true, true)));
    }

    private static string FormatTeamPlayer(GameKdaPlayerSummary player, int nameLimit, bool compact, bool terse = false)
    {
        string name = NormalizeName(player.DisplayName);
        if (name.Length > nameLimit) name = name[..nameLimit] + "…";
        if (player.Assessment == null) return $"{name} 战绩暂未获取";
        if (player.Assessment is not { SampleSize: > 0 } assessment)
            return compact ? $"{name} 同模式无样本" : $"{name} 近30天同模式无样本";
        if (!assessment.HasEnoughSample)
            return compact
                ? $"{name} 样本不足{assessment.SampleSize}/{RecentModePerformanceEvaluator.RequiredSampleSize} K{FormatKda(assessment.Kda, 1)}"
                : $"{name} 样本不足{assessment.SampleSize}/{RecentModePerformanceEvaluator.RequiredSampleSize} KDA{FormatKda(assessment.Kda, 2)}";
        string label = RecentPerformanceLabelFormatter.GetText(assessment);
        return compact
            ? $"{name} {label}{assessment.Score} {(terse ? "K" : "KDA")}{FormatKda(assessment.Kda, 1)}"
            : $"{name} {label}{assessment.Score}分 KDA{FormatKda(assessment.Kda, 2)}";
    }

    private static string FormatPlayer(string team, GameKdaPlayerSummary player)
    {
        // 游戏聊天以 Enter 提交消息；名字中的换行不能传入按键模拟器。
        string name = NormalizeName(player.DisplayName);
        string teamLabel = team == "蓝方" ? "蓝" : team == "红方" ? "红" : team;
        string detail = player.Assessment switch
        {
            null => "战绩暂未获取",
            not { SampleSize: > 0 } => "同模式无样本",
            { HasEnoughSample: false } assessment =>
                $"样本不足{assessment.SampleSize}/{RecentModePerformanceEvaluator.RequiredSampleSize} K{FormatKda(assessment.Kda, 1)}",
            { } assessment =>
                $"{RecentPerformanceLabelFormatter.GetText(assessment)}{assessment.Score} K{FormatKda(assessment.Kda, 1)}"
        };
        int nameWidth = Math.Max(2, MaximumBodyWidth - DisplayWidth(teamLabel) - 2 - DisplayWidth(detail));
        return $"{teamLabel} {ShortenToDisplayWidth(name, nameWidth)} {detail}";
    }

    private static string NormalizeName(string displayName) => string.IsNullOrWhiteSpace(displayName)
        ? "未知玩家"
        : string.Join(" ", displayName.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    private static string ShortenToDisplayWidth(string value, int maximumWidth)
    {
        if (DisplayWidth(value) <= maximumWidth) return value;
        int width = 0;
        int index = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            int runeWidth = rune.Value <= 0x7f ? 1 : 2;
            if (width + runeWidth > maximumWidth - 2)
                return value[..index] + "…";
            width += runeWidth;
            index += rune.Utf16SequenceLength;
        }
        return value;
    }

    private static int DisplayWidth(string value) => value.EnumerateRunes()
        .Sum(rune => rune.Value <= 0x7f ? 1 : 2);

    // 发送文案截断小数，避免 2.19→2.2 或 4.49→4.5 的四舍五入让数值看起来跨过分档线。
    private static string FormatKda(double value, int decimals)
    {
        decimal factor = decimals == 1 ? 10m : 100m;
        decimal nonNegative = (decimal)Math.Max(0, value);
        decimal truncated = Math.Truncate(nonNegative * factor) / factor;
        return truncated.ToString($"F{decimals}");
    }
}
