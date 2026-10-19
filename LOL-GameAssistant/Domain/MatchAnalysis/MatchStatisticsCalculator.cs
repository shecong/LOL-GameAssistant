using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

public sealed record MatchAnalysisPolicy(int GameLimit = 50, int MinimumRatingGames = 10, bool AllowAllModesFallback = true);

public sealed record PlayerMatchStatistics(RecentModePerformanceAssessment Assessment,
    IReadOnlyList<MatchHistoryGame> Games, IReadOnlyList<bool> RecentResults,
    int UnknownResults, int ChampionGames, int ChampionWins, int RecentStreak,
    double? AverageKills, double? AverageDeaths, double? AverageAssists);

/// <summary>统一的纯计算器。KDA、胜率各自统计有效字段，不将未知当作零或失败。</summary>
public static class MatchStatisticsCalculator
{
    public static PlayerMatchStatistics Calculate(IEnumerable<MatchHistoryGame> history, string puuid,
        int queueId, string? mode, string modeText, int championId, DateTimeOffset now,
        MatchAnalysisPolicy? policy = null)
    {
        policy ??= new();
        int limit = Math.Clamp(policy.GameLimit, 1, 500);
        var window = history.OrderByDescending(game => game.GameCreation)
            .Where(game => game.GameId > 0).DistinctBy(game => game.GameId).Take(500).ToArray();
        var available = window.Where(game => game.GameCreation <= now.ToUnixTimeMilliseconds() &&
            game.IsCompletedGame() && !game.IsCustomGame()).ToArray();
        var games = available.Where(game => MatchModeComparer.IsSameMode(queueId, mode, game)).Take(limit).ToArray();
        bool allModes = !games.Any(game => ValidKda(game.GetParticipant(puuid)?.stats)) &&
            policy.AllowAllModesFallback && available.Length > 0;
        if (allModes) games = available.Take(limit).ToArray();
        var stats = games.Select(game => game.GetParticipant(puuid)?.stats).ToArray();
        var kda = stats.Where(ValidKda).Cast<MatchParticipantStats>().ToArray();
        var results = stats.Where(value => value?.HasWinResult == true).Cast<MatchParticipantStats>().Select(value => value.Win).ToArray();
        int required = Math.Clamp(policy.MinimumRatingGames, 1, 500);
        string scope = allModes ? "各模式参考（排除自定义）" : queueId == 3110 ? $"{modeText}参考（峡谷匹配/单双排/灵活）" : modeText;
        var assessment = RecentModePerformanceEvaluator.Evaluate(scope,
            kda.Select(value => new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 0, "", value.kills, value.deaths, value.assists)),
            Enumerable.Repeat(false, kda.Length), required) with
        {
            WinRate = results.Length == 0 ? 0 : results.Count(value => value) * 100d / results.Length,
            WinSampleSize = results.Length, RequiredSampleSize = required, UsesAllModes = allModes,
            NoKdaSampleReason = kda.Length > 0 ? null : window.Length == 0 ? "暂无战绩摘要" :
                available.Length == 0 ? "无非自定义有效对局" :
                games.Length == 0 ? "无同模式对局" : "KDA字段缺失"
        };
        string rate = results.Length == 0 ? "未知" : $"{assessment.WinRate:F1}%";
        assessment = assessment with { Detail = $"已获取摘要中最近{limit}场{scope}（不足按实际场数，不限日期）：KDA样本 {kda.Length} 场" +
            $" · KDA {(kda.Length == 0 ? "—" : assessment.Kda.ToString("F2"))} · 胜负样本 {results.Length} 场 · 胜率 {rate}" +
            $" · {RecentPerformanceLabelFormatter.GetText(assessment)}（评级至少 {required} 场）。" +
            $"摘要 {window.Length} 场 → 非自定义有效对局 {available.Length} 场 → 最近{limit}场{(allModes ? "全模式参考" : "同模式")} {games.Length} 场。" +
            (assessment.NoKdaSampleReason == null ? "" : $"原因：{assessment.NoKdaSampleReason}。") };
        var recent = games.Take(10).ToArray();
        var recentStats = recent.Select(game => game.GetParticipant(puuid)?.stats).ToArray();
        var recentKda = recentStats.Where(ValidKda).Cast<MatchParticipantStats>().ToArray();
        var champion = games.Select(game => game.GetParticipant(puuid))
            .Where(player => championId > 0 && player?.championId == championId && player.stats?.HasWinResult == true).ToArray();
        var latest = recentStats.FirstOrDefault();
        int streak = latest?.HasWinResult != true ? 0 : recentStats.TakeWhile(value =>
            value?.HasWinResult == true && value.Win == latest.Win).Count() * (latest.Win ? 1 : -1);
        return new(assessment, Array.AsReadOnly(games),
            Array.AsReadOnly(recentStats.Where(value => value?.HasWinResult == true).Select(value => value!.Win).ToArray()),
            stats.Count(value => value?.HasWinResult != true), champion.Length, champion.Count(player => player!.stats!.Win), streak,
            recentKda.Length == 0 ? null : recentKda.Average(value => value.kills),
            recentKda.Length == 0 ? null : recentKda.Average(value => value.deaths),
            recentKda.Length == 0 ? null : recentKda.Average(value => value.assists));
    }
    public static bool ValidKda(MatchParticipantStats? stats) => stats is
        { HasKdaResult: true, kills: >= 0, deaths: >= 0, assists: >= 0 };
}
