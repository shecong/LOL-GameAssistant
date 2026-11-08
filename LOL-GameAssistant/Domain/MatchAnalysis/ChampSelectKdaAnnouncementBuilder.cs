using System.Globalization;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>以客户端楼层识别队友，避免异步返回顺序或玩家姓名排序改变消息中的位置。</summary>
public sealed record ChampSelectKdaPlayerSummary(int Floor, RecentModePerformanceAssessment? Assessment, string DisplayName = "");

/// <summary>组织选人阶段逐楼层的紧凑战绩汇总，保留自定义模板。</summary>
public static class ChampSelectKdaAnnouncementBuilder
{
    public const string DefaultTemplate = "【近期KDA】\n{players}";
    private const string LegacyTemplate = "【选人近期 KDA 评估】\n{allies}\n{enemies}";
    private const string PreviousTemplate = "【sc助手 · 队友近期战绩】\n近50场有效记录 · 优先同模式\n{players}";

    public static string NormalizeTemplate(string? template)
    {
        string value = (template ?? "").Replace("\r\n", "\n").Trim();
        return string.IsNullOrEmpty(value) || value == LegacyTemplate || value == PreviousTemplate ? DefaultTemplate : value;
    }

    public static string Build(IEnumerable<ChampSelectKdaPlayerSummary> players, string? template = null)
    {
        string roster = string.Join("\n", players.OrderBy(player => player.Floor).Select(FormatPlayer));
        string value = NormalizeTemplate(template)
            .Replace("\n{enemies}", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{enemies}\n", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{enemies}", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{players}", roster, StringComparison.OrdinalIgnoreCase)
            .Replace("{allies}", roster, StringComparison.OrdinalIgnoreCase).Trim();
        return value.Length <= 1200 ? value : value[..1200];
    }

    public static string FormatPlayer(ChampSelectKdaPlayerSummary player)
    {
        var assessment = player.Assessment;
        string name = string.Concat(player.DisplayName.Where(character => !char.IsControl(character))).Trim();
        int[] elements = StringInfo.ParseCombiningCharacters(name);
        if (elements.Length > 6) name = name[..elements[5]] + "…";
        string prefix = string.IsNullOrWhiteSpace(name) ? $"{player.Floor}楼" : name;
        if (assessment == null)
            return $"{prefix}：战绩暂未获取";

        bool hasKda = assessment.SampleSize > 0;
        string label = hasKda ? assessment.HasEnoughSample
            ? RecentPerformanceLabelFormatter.GetText(assessment) : $"数据不足({assessment.SampleSize}场)" : "无KDA样本";
        string winRate = assessment.HasWinRate
            ? assessment.WinRate.ToString("F0", CultureInfo.InvariantCulture) + "%" : "--";
        // 与现有 KDA 分档一致，截断而非四舍五入，避免 4.499 显示成 4.50。
        string kda = hasKda
            ? (Math.Truncate((decimal)Math.Max(0, assessment.Kda) * 100m) / 100m)
                .ToString("F2", CultureInfo.InvariantCulture) : "--";
        string score = hasKda && assessment.HasEnoughSample
            ? assessment.Score.ToString(CultureInfo.InvariantCulture) + "分" : "";
        string scope = assessment.UsesAllModes ? " [全模式参考]" : "";
        return $"{prefix}：{label}{score}｜KDA{kda}｜胜率{winRate}{scope}";
    }

}
