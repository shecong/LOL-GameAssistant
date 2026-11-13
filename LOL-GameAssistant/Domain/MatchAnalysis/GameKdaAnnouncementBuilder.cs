namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>一名对局玩家的近期同模式 KDA 评估；没有可查结果时保留玩家并明确标注。</summary>
public sealed record GameKdaPlayerSummary(
    string Team,
    string DisplayName,
    RecentModePerformanceAssessment? Assessment);

/// <summary>游戏内共用选人的紧凑字段，每三名同队玩家合并提交，并对齐各自的显示行。</summary>
public static class GameKdaAnnouncementBuilder
{
    /// <summary>组织单名玩家的测评文案。</summary>
    public static string BuildSharpPlayer(GameKdaPlayerSummary player, int position, IReadOnlyDictionary<string, string>? customRemarks = null)
    {
        string name = ShortenToDisplayWidth(NormalizeName(player.DisplayName), 20);
        string prefix = name;
        var assessment = player.Assessment;
        if (assessment == null) return $"{prefix} 战绩暂未获取，没数据就不瞎下结论。";
        string scope = assessment.UsesAllModes ? "各模式" : "同模式";
        if (assessment.SampleSize <= 0) return $"{prefix} 近50场{scope}无样本，暂不测评。";
        string stats = $"近50场{scope}{assessment.SampleSize}场 KDA{FormatKda(assessment.Kda, 2)}";
        if (!assessment.HasEnoughSample) return $"{prefix} {stats}，样本不足，先别急着封神或判刑。";
        string remark = GameKdaRemarkLibrary.Select(assessment.Label, customRemarks);
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = NormalizeName(player.DisplayName), ["team"] = player.Team,
            ["position"] = position.ToString(), ["kda"] = FormatKda(assessment.Kda, 2),
            ["samples"] = assessment.SampleSize.ToString(), ["score"] = assessment.Score.ToString(),
            ["label"] = RecentPerformanceLabelFormatter.GetText(assessment),
            ["winrate"] = assessment.HasWinRate ? assessment.WinRate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "未知"
        };
        remark = System.Text.RegularExpressions.Regex.Replace(remark, @"\{(name|team|position|kda|samples|score|label|winrate)\}",
            match => placeholders[match.Groups[1].Value.ToLowerInvariant()], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        remark = ShortenToDisplayWidth(NormalizeName(remark), 300);
        return $"{prefix} {stats}，{RecentPerformanceLabelFormatter.GetText(assessment)}：{remark}";
    }

    /// <summary>每条最多两名同队玩家，显示阵营前缀，玩家各占一行，不插入额外空白行。</summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players,
        bool onePlayerPerLine = false, GameChatLineWidths? lineWidths = null)
    {
        return players.GroupBy(player => player.Team)
            .OrderBy(team => team.Key == "蓝方" ? 0 : team.Key == "红方" ? 1 : 2)
            .SelectMany(team => team.Chunk(2).Select(batch => AlignDisplayRows(
                batch.Select(FormatPlayer), lineWidths)))
            .ToArray();
    }

    private static string AlignDisplayRows(IEnumerable<string> rows, GameChatLineWidths? widths)
    {
        // 首行预留频道和发送者前缀；游戏聊天的实际换行会提交，使用等宽空格对齐续行。
        // OCR 校准以半角显示单位保存；无有效结果时沿用默认值。
        widths = widths is { IsValid: true } ? widths : GameChatLineWidths.Default;
        int firstLineWidth = widths.FirstLine, continuationLineWidth = widths.ContinuationLine;
        int column = 0, lineWidth = firstLineWidth;
        var body = new System.Text.StringBuilder();
        foreach (string row in rows)
        {
            if (column > 0 || body.Length == 0 && DisplayWidth(row) > firstLineWidth)
            {
                int remaining = lineWidth - column;
                body.Append('\u3000', remaining / 2);
                if (remaining % 2 != 0) body.Append(' ');
                column = 0;
                lineWidth = continuationLineWidth;
            }
            body.Append(row);
            foreach (var rune in row.EnumerateRunes())
            {
                int width = rune.Value <= 0x7f ? 1 : 2;
                if (column + width > lineWidth) { column = 0; lineWidth = continuationLineWidth; }
                column += width;
                if (column == lineWidth) { column = 0; lineWidth = continuationLineWidth; }
            }
        }
        return body.ToString();
    }

    /// <summary>将玩家评估信息格式化为显示文本。</summary>
    private static string FormatPlayer(GameKdaPlayerSummary player)
    {
        string line = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, player.Assessment, NormalizeName(player.DisplayName)));
        return $"【{NormalizeName(player.Team)} {line}】";
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
        // Normalize double precision before truncation so exact hundredths stay stable across runtimes.
        decimal nonNegative = decimal.Parse(Math.Max(0, value).ToString("G15", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        decimal truncated = Math.Truncate(nonNegative * factor) / factor;
        return truncated.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }
}
