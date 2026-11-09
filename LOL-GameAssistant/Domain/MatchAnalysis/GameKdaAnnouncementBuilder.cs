namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>一名对局玩家的近期同模式 KDA 评估；没有可查结果时保留玩家并明确标注。</summary>
public sealed record GameKdaPlayerSummary(
    string Team,
    string DisplayName,
    RecentModePerformanceAssessment? Assessment);

/// <summary>游戏内共用选人的紧凑格式，合并提交，用空格将玩家对齐到下一显示行。</summary>
public static class GameKdaAnnouncementBuilder
{
    // 首行需为游戏聊天的频道和发言者前缀留空间；续行使用完整正文宽度。
    internal const int FirstLineWidth = 30, ContinuationLineWidth = 60;
    private const int MaximumMessageLength = 480;
    /// <summary>组织单名玩家的测评文案。</summary>
    public static string BuildSharpPlayer(GameKdaPlayerSummary player, int position, IReadOnlyDictionary<string, string>? customRemarks = null)
    {
        string name = ShortenToDisplayWidth(NormalizeName(player.DisplayName), 20);
        string prefix = $"【{(player.Team == "蓝方" ? "蓝" : "红")}{position}】{name}";
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

    /// <summary>整批合并发送；旧排版参数不再拆分玩家，仅在超过输入长度时按玩家边界分批。</summary>
    public static IReadOnlyList<string> Build(IReadOnlyList<GameKdaPlayerSummary> players,
        bool onePlayerPerLine = false)
    {
        var rows = players.GroupBy(player => player.Team)
            .SelectMany(team => team.Select(player => FormatPlayer(team.Key, player)));
        var messages = new List<string>();
        var body = new System.Text.StringBuilder();
        int column = 0, lineWidth = FirstLineWidth;
        foreach (string row in rows)
        {
            string spacing = WrapSpacing(body.Length > 0, row);
            if (body.Length + spacing.Length + row.Length > MaximumMessageLength && body.Length > 0)
            {
                messages.Add(body.ToString()); body.Clear();
                column = 0; lineWidth = FirstLineWidth;
                spacing = WrapSpacing(false, row);
            }
            body.Append(spacing).Append(row);
            if (spacing.Length > 0) { column = 0; lineWidth = ContinuationLineWidth; }
            foreach (var rune in row.EnumerateRunes())
            {
                int width = rune.Value <= 0x7f ? 1 : 2;
                if (column + width > lineWidth) { column = 0; lineWidth = ContinuationLineWidth; }
                column += width;
                if (column == lineWidth) { column = 0; lineWidth = ContinuationLineWidth; }
            }
        }
        if (body.Length > 0) messages.Add(body.ToString());
        return messages;

        string WrapSpacing(bool hasPrevious, string row)
        {
            int remaining = hasPrevious && column > 0 ? lineWidth - column
                : !hasPrevious && DisplayWidth(row) > lineWidth ? lineWidth : 0;
            // 全角空格与汉字等宽，降低补齐时占用的输入字符数；奇数余量再补一个普通空格。
            return new string('\u3000', remaining / 2) + (remaining % 2 == 0 ? "" : " ");
        }
    }

    /// <summary>将玩家评估信息格式化为显示文本。</summary>
    private static string FormatPlayer(string team, GameKdaPlayerSummary player)
    {
        string teamLabel = team == "蓝方" ? "蓝" : team == "红方" ? "红" : team;
        string line = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, player.Assessment, NormalizeName(player.DisplayName)));
        return $"{NormalizeName(teamLabel)} {line}";
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
