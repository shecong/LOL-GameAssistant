using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

public sealed record GrowthMatch(long GameId, long CreatedAt, int ChampionId, string Mode, int Duration,
    bool? Won, double? Kda, double? CsPerMinute, double? DamagePerMinute, double? VisionPerMinute);
public sealed record ChampionGrowth(int ChampionId, int Games, int ResultSamples, int Wins, double? WinRate,
    double? Kda, double? CsPerMinute, double? DamagePerMinute, double? VisionPerMinute);
public sealed record GrowthReport(IReadOnlyList<GrowthMatch> Matches, IReadOnlyList<ChampionGrowth> Champions,
    int ExcludedGames, string Scope)
{
    public int ResultSamples => Matches.Count(x => x.Won.HasValue);
    public double? WinRate => ResultSamples == 0 ? null : Math.Round(Matches.Count(x => x.Won == true) * 100d / ResultSamples, 1);
}

/// <summary>只统计有明确身份、已完成且字段存在的样本；缺失不当作零或失败。</summary>
public static class PlayerGrowthCalculator
{
    public static GrowthReport Calculate(IEnumerable<MatchHistoryGame> source, string puuid, string mode = "全部")
    {
        var unique = source.Where(x => x.GameId > 0).DistinctBy(x => x.GameId).OrderByDescending(x => x.GameCreation).Take(100).ToArray();
        var matches = new List<GrowthMatch>(); int excluded = 0;
        foreach (var game in unique)
        {
            if (mode != "全部" && game.GetModeText() != mode) continue;
            var p = game.GetParticipant(puuid); var s = p?.stats;
            if (!game.IsCompletedGame() || p == null || s == null || p.championId <= 0) { excluded++; continue; }
            double? minutes = game.GameDuration > 0 ? game.GameDuration / 60d : null;
            matches.Add(new(game.GameId, game.GameCreation, p.championId, game.GetModeText(), game.GameDuration,
                s.HasWinResult ? s.Win : null, s.HasKdaResult ? Math.Round((s.kills + s.assists) / (double)Math.Max(1, s.deaths), 2) : null,
                s.HasFarmResult && minutes.HasValue ? Math.Round((s.totalMinionsKilled + s.neutralMinionsKilled) / minutes.Value, 2) : null,
                s.HasDamageResult && minutes.HasValue ? Math.Round(s.totalDamageDealtToChampions / minutes.Value, 1) : null,
                s.HasVisionResult && minutes.HasValue ? Math.Round(s.visionScore / minutes.Value, 2) : null));
        }
        var champions = matches.GroupBy(x => x.ChampionId).Select(g =>
        {
            int results = g.Count(x => x.Won.HasValue), wins = g.Count(x => x.Won == true);
            return new ChampionGrowth(g.Key, g.Count(), results, wins, results == 0 ? null : Math.Round(wins * 100d / results, 1),
                Average(g.Select(x => x.Kda)), Average(g.Select(x => x.CsPerMinute)), Average(g.Select(x => x.DamagePerMinute)), Average(g.Select(x => x.VisionPerMinute)));
        }).OrderByDescending(x => x.Games).ThenBy(x => x.ChampionId).ToArray();
        return new(matches, champions, excluded, $"{mode} · 最近最多 100 场可读取记录 · 有效 {matches.Count} 场 · 排除重开或身份/统计缺失 {excluded} 场");
    }
    public static double? Average(IEnumerable<double?> values)
    { var valid = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray(); return valid.Length == 0 ? null : Math.Round(valid.Average(), 2); }
}
