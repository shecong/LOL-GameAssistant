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

    /// <summary>组织单名玩家的测评文案。</summary>
    public static string BuildSharpPlayer(GameKdaPlayerSummary player, int position, IReadOnlyDictionary<string, string>? customRemarks = null)
    {
        string name = ShortenToDisplayWidth(NormalizeName(player.DisplayName), 20);
        string prefix = $"【{(player.Team == "蓝方" ? "蓝" : "红")}{position}】{name}";
        var assessment = player.Assessment;
        if (assessment == null) return $"{prefix} 战绩暂未获取，没数据就不瞎下结论。";
        if (assessment.SampleSize <= 0) return $"{prefix} 近30天同模式无样本，暂不测评。";
        string stats = $"近30天同模式{assessment.SampleSize}场 KDA{FormatKda(assessment.Kda, 2)}";
        if (!assessment.HasEnoughSample) return $"{prefix} {stats}，样本不足，先别急着封神或判刑。";
        string remark = GameKdaRemarkLibrary.Select(assessment.Label, customRemarks);
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = NormalizeName(player.DisplayName), ["team"] = player.Team,
            ["position"] = position.ToString(), ["kda"] = FormatKda(assessment.Kda, 2),
            ["samples"] = assessment.SampleSize.ToString(), ["score"] = assessment.Score.ToString(),
            ["label"] = RecentPerformanceLabelFormatter.GetText(assessment),
            ["winrate"] = assessment.WinRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
        };
        remark = System.Text.RegularExpressions.Regex.Replace(remark, @"\{(name|team|position|kda|samples|score|label|winrate)\}",
            match => placeholders[match.Groups[1].Value.ToLowerInvariant()], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        remark = ShortenToDisplayWidth(NormalizeName(remark), 300);
        return $"{prefix} {stats}，{RecentPerformanceLabelFormatter.GetText(assessment)}：{remark}";
    }

    /// <summary>按队伍组织测评消息，支持逐人独立发送或按队伍合并。</summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players,
        bool onePlayerPerLine = false)
    {
        return players.GroupBy(player => player.Team)
            .SelectMany(team => onePlayerPerLine
                ? team.Select(player => FormatPlayer(team.Key, player))
                : BuildTeamMessages(team.Key, team.ToArray()))
            .ToArray();
    }

    /// <summary>按队伍分别组织可发送的测评消息。</summary>
    private static IEnumerable<string> BuildTeamMessages(string team, IReadOnlyList<GameKdaPlayerSummary> players)
    {
        // 五人队伍必须一次生成完整汇总，不能按逐个加入时的长度提前拆开，
        // 否则会出现“红方四人”之后又发送“红方最后一人”的额外消息。
        foreach (GameKdaPlayerSummary[] batch in players.Chunk(5))
            yield return BuildTeamMessage(team, batch);
    }

    /// <summary>组织一支队伍的测评消息。</summary>
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

    /// <summary>将队伍中的玩家信息格式化为测评文本。</summary>
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

    /// <summary>将玩家评估信息格式化为显示文本。</summary>
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

    /// <summary>规范化玩家名称，供测评文案使用。</summary>
    private static string NormalizeName(string displayName) => string.IsNullOrWhiteSpace(displayName)
        ? "未知玩家"
        : string.Join(" ", displayName.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

    /// <summary>按显示宽度截断文本，避免中文姓名挤占消息长度。</summary>
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

    /// <summary>估算文本的显示宽度，区分宽字符和窄字符。</summary>
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
