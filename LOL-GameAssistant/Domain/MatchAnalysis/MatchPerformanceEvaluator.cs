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
    string Detail,
    int Kills = 0,
    int Deaths = 0,
    int Assists = 0);

/// <summary>
/// 同一模式最近对局的表现汇总。它不是段位或胜负预测，而是用于当前对局中提示玩家近况。
/// </summary>
public sealed record RecentModePerformanceAssessment(
    MatchPerformanceTier Tier,
    int Score,
    int SampleSize,
    double WinRate,
    string Detail,
    double Kda = 0,
    bool HasEnoughSample = false);

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
            return new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 50, "本局数据不足，暂不参与近期 KDA 判定。");

        var team = allParticipants.Where(item => item.TeamId == player.TeamId).ToList();
        if (team.Count == 0)
            return new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 50, "未找到可比较的本局队伍数据。", player.Kills, player.Deaths, player.Assists);

        double playerKda = Kda(player);
        double avgKda = team.Average(Kda);
        double avgDamage = Math.Max(1, team.Average(item => item.ChampionDamage));
        double avgGold = Math.Max(1, team.Average(item => item.GoldEarned));
        double avgVision = Math.Max(1, team.Average(item => item.VisionScore));

        // This score is explanatory only.  The visible "上/中/下等马" label is
        // determined from aggregate recent KDA below, not damage/gold/vision or one game.
        double score = 50 + (Relative(playerKda, Math.Max(.5, avgKda)) - 1) * 45;
        int rounded = Math.Clamp((int)Math.Round(score), 0, 100);
        MatchPerformanceTier tier = playerKda >= avgKda * 1.2
            ? MatchPerformanceTier.Upper
            : playerKda < avgKda * .75 ? MatchPerformanceTier.Lower : MatchPerformanceTier.Medium;
        var reasons = new List<string>
        {
            $"KDA {playerKda:F1}（队内均值 {avgKda:F1}）",
            $"对英雄伤害 {player.ChampionDamage:N0}（队内均值 {avgDamage:N0}）",
            $"金币 {player.GoldEarned:N0}（队内均值 {avgGold:N0}）"
        };
        if (player.VisionScore > 0 || avgVision > 1)
            reasons.Add($"视野 {player.VisionScore}（队内均值 {avgVision:F0}）");

        return new MatchPerformanceAssessment(tier, rounded, string.Join("；", reasons), player.Kills, player.Deaths, player.Assists);
    }

    private static double Kda(MatchPerformanceSnapshot participant) =>
        (participant.Kills + participant.Assists) / (double)Math.Max(1, participant.Deaths);

    private static double Relative(double value, double average) => Math.Clamp(value / average, 0, 1.5);
}

/// <summary>
/// 用同一模式的近期已结束对局判定“上/中/下等马”。
/// 标签只由汇总 KDA 决定；伤害、金币、视野和胜率只保留在详情中，避免它们改变 KDA 标签。
/// </summary>
public static class RecentModePerformanceEvaluator
{
    public static RecentModePerformanceAssessment Evaluate(
        string mode,
        IEnumerable<MatchPerformanceAssessment> performances,
        IEnumerable<bool> wins)
    {
        var performanceList = performances.Take(10).ToList();
        var resultList = wins.Take(10).ToList();
        int sampleSize = Math.Min(performanceList.Count, resultList.Count);
        if (sampleSize == 0)
        {
            return new RecentModePerformanceAssessment(
                MatchPerformanceTier.Medium, 50, 0, 0,
                $"最近没有可用于分析的{mode}已结束对局，暂不贴表现标签。", 0, false);
        }

        performanceList = performanceList.Take(sampleSize).ToList();
        resultList = resultList.Take(sampleSize).ToList();
        double winRate = resultList.Count(item => item) * 100D / sampleSize;
        int totalKills = performanceList.Sum(item => item.Kills);
        int totalDeaths = performanceList.Sum(item => item.Deaths);
        int totalAssists = performanceList.Sum(item => item.Assists);
        double kda = (totalKills + totalAssists) / (double)Math.Max(1, totalDeaths);
        bool hasEnoughSample = sampleSize >= 5;
        int score = Math.Clamp((int)Math.Round(kda / 4D * 100D), 0, 100);

        if (!hasEnoughSample)
        {
            return new RecentModePerformanceAssessment(
                MatchPerformanceTier.Medium, score, sampleSize, winRate,
                $"最近仅 {sampleSize} 场{mode}，样本不足 5 场，暂不贴表现标签。累计 KDA {kda:F2}。",
                kda,
                false);
        }

        // Strict, deterministic thresholds requested by the product: a player must
        // maintain a 3.5+ KDA across at least five comparable games to be upper tier.
        MatchPerformanceTier tier = kda >= 3.5
            ? MatchPerformanceTier.Upper
            : kda < 1.5 ? MatchPerformanceTier.Lower : MatchPerformanceTier.Medium;
        string detail = $"最近 {sampleSize} 场{mode}：累计 {totalKills}/{totalDeaths}/{totalAssists} · KDA {kda:F2} · 胜率 {winRate:F0}%";
        return new RecentModePerformanceAssessment(tier, score, sampleSize, winRate, detail, kda, true);
    }
}