using System.Globalization;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

public enum PracticeMetric { Cs, Damage, Vision, Deaths }
public sealed record PracticeGoal(PracticeMetric Metric, double Target, string Mode, int? ChampionId, DateTimeOffset StartedAt, int Games = 5);
public sealed record PracticeProgress(int Completed, int Passed, int Missing, int Required);
public sealed record MinuteReview(string Highlight, string Improvement, string NextGoal, int BaselineGames);

/// <summary>只比较同英雄、同模式的已知数据，不从结算统计推断对局过程或失误原因。</summary>
public static class CompanionExperience
{
    public static string MetricName(PracticeMetric metric) => metric switch
    { PracticeMetric.Cs => "补刀/分", PracticeMetric.Damage => "伤害/分", PracticeMetric.Vision => "视野/分", _ => "死亡次数" };
    public static double? Value(GrowthMatch match, PracticeMetric metric) => metric switch
    { PracticeMetric.Cs => match.CsPerMinute, PracticeMetric.Damage => match.DamagePerMinute, PracticeMetric.Vision => match.VisionPerMinute, _ => match.Deaths };
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static PracticeProgress Progress(IEnumerable<GrowthMatch> source, PracticeGoal goal)
    {
        var games = source.DistinctBy(m => m.GameId).Where(m => m.CreatedAt >= goal.StartedAt.ToUnixTimeMilliseconds() &&
            m.Mode == goal.Mode && (!goal.ChampionId.HasValue || m.ChampionId == goal.ChampionId))
            .OrderBy(m => m.CreatedAt).ToArray();
        var valid = games.Where(m => Value(m, goal.Metric) is double v && double.IsFinite(v)).Take(goal.Games).ToArray();
        int known = valid.Length;
        int passed = valid.Count(m => Value(m, goal.Metric) is double v &&
            (goal.Metric == PracticeMetric.Deaths ? v <= goal.Target : v >= goal.Target));
        int missing = games.Count(m => (!valid.Any() || known < goal.Games || m.CreatedAt <= valid[^1].CreatedAt) &&
            !(Value(m, goal.Metric) is double v && double.IsFinite(v)));
        return new(known, passed, missing, goal.Games);
    }
    public static MinuteReview Review(GrowthMatch current, IEnumerable<GrowthMatch> source)
    {
        var baseline = source.Where(m => m.GameId != current.GameId && m.CreatedAt <= current.CreatedAt &&
            m.ChampionId == current.ChampionId && m.Mode == current.Mode).DistinctBy(m => m.GameId)
            .OrderByDescending(m => m.CreatedAt).Take(10).ToArray();
        var comparisons = new List<(PracticeMetric Metric, double Current, double Mean, double Change, int Samples)>();
        foreach (var metric in Enum.GetValues<PracticeMetric>())
        {
            var values = baseline.Select(m => Value(m, metric)).OfType<double>().Where(double.IsFinite).ToArray();
            if (values.Length < 3 || Value(current, metric) is not double value || !double.IsFinite(value)) continue;
            double mean = values.Average(), direction = metric == PracticeMetric.Deaths ? -1 : 1;
            comparisons.Add((metric, value, mean, direction * (value - mean) / Math.Max(1, Math.Abs(mean)), values.Length));
        }
        string Evidence((PracticeMetric Metric, double Current, double Mean, double Change, int Samples) c) =>
            $"{MetricName(c.Metric)}：本局 {N(c.Current)}，此前同英雄同模式 {c.Samples} 场均值 {N(c.Mean)}";
        var strengths = comparisons.Where(c => c.Change > .01).OrderByDescending(c => c.Change).ToArray();
        var weaknesses = comparisons.Where(c => c.Change < -.01).OrderBy(c => c.Change).ToArray();
        return new(strengths.Length > 0 ? Evidence(strengths[0]) : "暂未发现有足够样本支持的明显提升项。",
            weaknesses.Length > 0 ? Evidence(weaknesses[0]) : "暂未发现有足够样本支持的明显下降项；不据此认定本局没有问题。",
            weaknesses.Length > 0 ? $"下一局可关注{MetricName(weaknesses[0].Metric)}；先以个人历史均值 {N(weaknesses[0].Mean)} 为参考，自行选择练习目标。" :
                "可选择一个自己想改善的指标，连续练习五局；单局胜负不作为训练是否达标的依据。", baseline.Length);
    }
}
