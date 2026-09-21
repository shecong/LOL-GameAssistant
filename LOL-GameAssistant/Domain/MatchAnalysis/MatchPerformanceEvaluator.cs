namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>
/// 已结束对局中单个玩家的可比较数据快照。
/// 领域对象不依赖 LCU DTO 或 WinForms，数据来源可以是本地缓存或远端接口。
/// </summary>
public sealed record MatchPerformanceSnapshot(
    string PlayerId,
    int TeamId,
    bool IsWin,
    int Kills,
    int Deaths,
    int Assists,
    int ChampionDamage,
    int GoldEarned,
    int VisionScore);

/// <summary>表现分层仅有三档，表现层负责映射到“上等马 / 中等马 / 下等马”文本。</summary>
public enum MatchPerformanceTier
{
    Upper,
    Medium,
    Lower
}

/// <summary>领域评估结果，可供战绩列表、详情和导出功能复用。</summary>
public sealed record MatchPerformanceAssessment(
    MatchPerformanceTier Tier,
    int Score,
    string Detail);

/// <summary>
/// 同一模式最近对局的表现汇总。它不是段位或胜负预测，而是用于当前对局中提示玩家近况。
/// </summary>
public sealed record RecentModePerformanceAssessment(
    MatchPerformanceTier Tier,
    int Score,
    int SampleSize,
    double WinRate,
    string Detail);

/// <summary>
/// 赛后表现领域服务。
/// 只比较同一已结束对局内的同队可见数据，不读取选人或进行中的历史战绩。
/// </summary>
public static class MatchPerformanceEvaluator
{
    /// <summary>基于 KDA、伤害、金币、视野和胜负计算相对队内表现。</summary>
    public static MatchPerformanceAssessment Evaluate(
        MatchPerformanceSnapshot? player,
        IEnumerable<MatchPerformanceSnapshot> allParticipants)
    {
        if (player == null)
            return new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 50, "本局数据不足，暂按中等表现记录。");

        var team = allParticipants.Where(item => item.TeamId == player.TeamId).ToList();
        if (team.Count == 0)
            return new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 50, "未找到可比较的本局队伍数据。");

        double playerKda = Kda(player);
        double avgKda = team.Average(Kda);
        double avgDamage = Math.Max(1, team.Average(item => item.ChampionDamage));
        double avgGold = Math.Max(1, team.Average(item => item.GoldEarned));
        double avgVision = Math.Max(1, team.Average(item => item.VisionScore));

        double score =
            Relative(playerKda, Math.Max(0.5, avgKda)) * 34 +
            Relative(player.ChampionDamage, avgDamage) * 28 +
            Relative(player.GoldEarned, avgGold) * 22 +
            Relative(player.VisionScore, avgVision) * 10 +
            (player.IsWin ? 6 : 0);
        int rounded = Math.Clamp((int)Math.Round(score), 0, 100);

        MatchPerformanceTier tier = rounded >= 68
            ? MatchPerformanceTier.Upper
            : rounded <= 38 ? MatchPerformanceTier.Lower : MatchPerformanceTier.Medium;
        var reasons = new List<string>
        {
            $"KDA {playerKda:F1}（队内均值 {avgKda:F1}）",
            $"对英雄伤害 {player.ChampionDamage:N0}（队内均值 {avgDamage:N0}）",
            $"金币 {player.GoldEarned:N0}（队内均值 {avgGold:N0}）"
        };
        if (player.VisionScore > 0 || avgVision > 1)
            reasons.Add($"视野 {player.VisionScore}（队内均值 {avgVision:F0}）");

        return new MatchPerformanceAssessment(tier, rounded, string.Join("；", reasons));
    }

    private static double Kda(MatchPerformanceSnapshot participant) =>
        (participant.Kills + participant.Assists) / (double)Math.Max(1, participant.Deaths);

    private static double Relative(double value, double average) => Math.Clamp(value / average, 0, 1.5);
}

/// <summary>
/// 用同一模式的近期已结束对局判定“上/中/下等马”。
/// 权重由单局队内相对贡献、近期胜率和最近三局状态组成，避免只凭某一局的偶然数据贴标签。
/// </summary>
public static class RecentModePerformanceEvaluator
{
    public static RecentModePerformanceAssessment Evaluate(
        string mode,
        IEnumerable<MatchPerformanceAssessment> performances,
        IEnumerable<bool> wins)
    {
        var performanceList = performances.Take(12).ToList();
        var resultList = wins.Take(12).ToList();
        int sampleSize = Math.Min(performanceList.Count, resultList.Count);
        if (sampleSize == 0)
        {
            return new RecentModePerformanceAssessment(
                MatchPerformanceTier.Medium, 50, 0, 0,
                $"最近没有可用于分析的{mode}已结束对局，暂不贴表现标签。");
        }

        performanceList = performanceList.Take(sampleSize).ToList();
        resultList = resultList.Take(sampleSize).ToList();
        double impact = performanceList.Average(item => item.Score);
        double recentImpact = performanceList.Take(3).Average(item => item.Score);
        double winRate = resultList.Count(item => item) * 100D / sampleSize;
        // 队内影响 55%，胜率 30%，最近三局状态 15%。样本不足 5 场时向中间分收缩，
        // 防止一两局极端数据变成“上等马/下等马”。
        double rawScore = impact * .55 + winRate * .30 + recentImpact * .15;
        double confidence = Math.Min(1D, sampleSize / 5D);
        int score = (int)Math.Round(50 + (rawScore - 50) * confidence);
        score = Math.Clamp(score, 0, 100);
        MatchPerformanceTier tier = score >= 64
            ? MatchPerformanceTier.Upper
            : score <= 42 ? MatchPerformanceTier.Lower : MatchPerformanceTier.Medium;

        string detail = $"最近 {sampleSize} 场{mode}：胜率 {winRate:F0}% · 平均队内影响 {impact:F0} · 最近 3 场 {recentImpact:F0}";
        if (sampleSize < 5) detail += " · 样本较少，结论已收敛";
        return new RecentModePerformanceAssessment(tier, score, sampleSize, winRate, detail);
    }
}
