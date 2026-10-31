using System.Text;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>Evidence-based descriptions; comparisons never mix champions or modes.</summary>
public static class GrowthInsights
{
    public static string Number(double? value) => value?.ToString("0.##") ?? "—";
    public static string Metrics(IEnumerable<GrowthMatch> source)
    {
        var a = source.ToArray();
        double? Mean(Func<GrowthMatch, double?> metric) => PlayerGrowthCalculator.Average(a.Select(metric));
        int results = a.Count(m => m.Won.HasValue);
        return $"{a.Length} 场 · {a.Count(m => m.Won == true)} 胜 {a.Count(m => m.Won == false)} 负 · 胜率 {Number(results == 0 ? null : a.Count(m => m.Won == true) * 100d / results)}%\r\n" +
            $"KDA {Number(Mean(m => m.Kda))} · 死亡 {Number(Mean(m => m.Deaths))}/局 · 补刀 {Number(Mean(m => m.CsPerMinute))}/分 · 伤害 {Number(Mean(m => m.DamagePerMinute))}/分 · 视野 {Number(Mean(m => m.VisionPerMinute))}/分";
    }

    public static string Overview(GrowthReport report)
    {
        var a = report.Matches.OrderByDescending(m => m.CreatedAt).ToArray();
        var text = new StringBuilder("样本总览\r\n").AppendLine(Metrics(a));
        text.AppendLine($"使用英雄 {a.Select(m => m.ChampionId).Distinct().Count()} 位 · 总时长 {a.Sum(m => (long)Math.Max(0, m.Duration)) / 3600d:0.0} 小时 · 重开/无效记录 {report.ExcludedGames} 场");
        text.AppendLine($"指标覆盖：KDA {a.Count(m => m.Kda.HasValue)}/{a.Length} · 补刀 {a.Count(m => m.CsPerMinute.HasValue)}/{a.Length} · 伤害 {a.Count(m => m.DamagePerMinute.HasValue)}/{a.Length} · 视野 {a.Count(m => m.VisionPerMinute.HasValue)}/{a.Length}");
        text.AppendLine($"高死亡对局（≥8） {a.Count(m => m.Deaths >= 8)} 场 / 已知死亡 {a.Count(m => m.Deaths.HasValue)} 场 · KDA≥3 {a.Count(m => m.Kda >= 3)} 场 / 已知KDA {a.Count(m => m.Kda.HasValue)} 场");
        var top = a.GroupBy(m => m.ChampionId).OrderByDescending(g => g.Count()).Take(3).Sum(g => g.Count());
        text.AppendLine($"前三常用英雄占比 {Number(a.Length == 0 ? null : top * 100d / a.Length)}% · 低于5场的英雄 {a.GroupBy(m => m.ChampionId).Count(g => g.Count() < 5)} 位（尚待积累）");
        foreach (var mode in a.GroupBy(m => m.Mode).OrderByDescending(g => g.Count()))
        {
            text.AppendLine().AppendLine($"【{mode.Key}】").AppendLine(Metrics(mode));
            var recent = mode.Take(10).ToArray();var previous = mode.Skip(10).Take(10).ToArray();
            text.AppendLine($"最近 {recent.Length} 场：{Metrics(recent)}");
            if (previous.Length > 0) text.AppendLine($"此前 {previous.Length} 场：{Metrics(previous)}");
            text.AppendLine($"胜局表现：{Metrics(mode.Where(m => m.Won == true))}");
            text.AppendLine($"负局表现：{Metrics(mode.Where(m => m.Won == false))}");
            foreach (var duration in mode.Where(m => m.Duration > 0).GroupBy(m => m.Duration < 1200 ? "20分钟内" : m.Duration < 1800 ? "20–30分钟" : "30分钟以上"))
                text.AppendLine($"时长分组 · {duration.Key}：{Metrics(duration)}");
        }
        return text.AppendLine().Append("统计描述样本差异，不据此推断对线、团战或胜负原因。— 表示未取得字段；小样本不代表长期实力。").ToString();
    }

    public static string ChampionStatus(ChampionGrowth? champion) => champion == null ? "暂无近期样本" :
        champion.ResultSamples < 5 ? "观察期 · 至少积累 5 场" : champion.WinRate >= 55 ? "近期优势 · 可作为主练候选" :
        champion.WinRate < 45 ? "重点复盘 · 先收敛练习目标" : "持续积累 · 观察稳定性";

    public static string Review(GrowthMatch match, IEnumerable<GrowthMatch> history)
    {
        var baseline = history.Where(m => m.GameId != match.GameId && m.CreatedAt < match.CreatedAt &&
            m.ChampionId == match.ChampionId && m.Mode == match.Mode).DistinctBy(m => m.GameId).OrderByDescending(m => m.CreatedAt).Take(10).ToArray();
        var text = new StringBuilder().AppendLine($"本局 {match.GameId} · {match.Mode} · 时长 {match.Duration / 60}:{match.Duration % 60:00}")
            .AppendLine(Metrics([match])).AppendLine().AppendLine($"个人基线 · 此前同英雄同模式 {baseline.Length} 场").AppendLine(Metrics(baseline));
        var review = CompanionExperience.Review(match, baseline);
        text.AppendLine().AppendLine("值得保持").AppendLine(review.Highlight)
            .AppendLine().AppendLine("优先复盘").AppendLine(review.Improvement)
            .AppendLine().AppendLine("下一局练习").AppendLine(review.NextGoal);
        text.AppendLine().Append("回看检查：记录一次关键死亡前的选择、一次资源交换和一个可重复的改进动作。可填写上方笔记并保存，或设置练习目标追踪后续达标情况。");
        return text.ToString();
    }
}
