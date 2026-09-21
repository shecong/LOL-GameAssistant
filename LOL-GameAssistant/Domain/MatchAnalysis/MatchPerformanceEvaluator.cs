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
