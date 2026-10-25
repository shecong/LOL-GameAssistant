namespace LOL_GameAssistant.Domain.Matches;

public sealed record MatchHonors(string? Mvp, IReadOnlyList<string> Feats, bool MvpEvaluated)
{
    public IReadOnlyList<string> Labels => Mvp is null ? Feats : new[] { Mvp }.Concat(Feats).ToArray();
    public string Text => string.Join(" · ", Labels);
}

/// <summary>Only compares confirmed full rosters. Individual feats do not require other players' data.</summary>
public static class MatchHonorCalculator
{
    public const string Explanation = "MVP 为助手综合评选，非官方称号：同队比较 KDA、参团率，并在全队均有数据时参考伤害、视野和补刀；双方各一人。超神为本局最高连续击杀 ≥ 8，多杀展示最高档记录。";

    public static MatchHonors ForPlayer(MatchHistoryGame game, MatchParticipant? player) => Calculate(
        game.Participants, player, game.HasFullParticipants, game.IsCompletedGame());

    public static MatchHonors ForPlayer(MatchDetail game, MatchParticipant? player) => Calculate(
        game.participants, player, game.HasFullParticipants,
        !game.endOfGameResult.StartsWith("Abort", StringComparison.OrdinalIgnoreCase) && !(game.gameDuration is > 0 and < 300));

    private static MatchHonors Calculate(IReadOnlyList<MatchParticipant> players, MatchParticipant? player, bool full, bool completed)
    {
        if (!completed || player?.stats is not { } stats) return new(null, [], false);
        var feats = new List<string>();
        if (stats.largestKillingSpree >= 8) feats.Add("超神");
        if (stats.pentaKills > 0) feats.Add(Count("五杀", stats.pentaKills));
        else if (stats.quadraKills > 0) feats.Add(Count("四杀", stats.quadraKills));
        else if (stats.tripleKills > 0) feats.Add(Count("三杀", stats.tripleKills));
        else if (stats.doubleKills > 0) feats.Add(Count("双杀", stats.doubleKills));

        var teams = players.GroupBy(p => p.teamId).ToArray();
        bool eligible = full && teams.Length == 2 && teams.All(t => t.Key > 0 && t.Count() >= 2) &&
            teams[0].Count() == teams[1].Count() && players.All(p => p.stats is { HasKdaResult: true, HasWinResult: true }
                && p.stats.kills >= 0 && p.stats.deaths >= 0 && p.stats.assists >= 0) &&
            players.Select(p => p.participantId).Distinct().Count() == players.Count &&
            teams.All(t => t.Select(p => p.stats!.Win).Distinct().Count() == 1) &&
            teams[0].First().stats!.Win != teams[1].First().stats!.Win;
        if (!eligible) return new(null, feats, false);
        var team = players.Where(p => p.teamId == player.teamId).ToArray();
        if (team.Length == 0 || !team.Any(p => p.participantId == player.participantId) ||
            !team.Any(p => p.stats!.kills + p.stats.assists > 0)) return new(null, feats, false);
        double teamKills = Math.Max(1, team.Sum(p => (double)p.stats!.kills));
        bool damage = team.All(p => p.stats!.HasDamageResult);
        bool vision = team.All(p => p.stats!.HasVisionResult);
        bool farm = team.All(p => p.stats!.HasFarmResult);
        double maxDamage = Math.Max(1, team.Max(p => (double)p.stats!.totalDamageDealtToChampions));
        double maxVision = Math.Max(1, team.Max(p => (double)p.stats!.visionScore));
        double maxFarm = Math.Max(1, team.Max(p => (double)p.stats!.totalMinionsKilled + p.stats.neutralMinionsKilled));
        double Score(MatchParticipant p)
        {
            var s = p.stats!;
            double kda = ((double)s.kills + s.assists) / Math.Max(1, s.deaths);
            return 40 * Math.Min(1, kda / 10) + 25 * Math.Clamp((s.kills + (double)s.assists) / teamKills, 0, 1) +
                (damage ? 20 * Math.Clamp(s.totalDamageDealtToChampions / maxDamage, 0, 1) : 0) +
                (vision ? 10 * Math.Clamp(s.visionScore / maxVision, 0, 1) : 0) +
                (farm ? 5 * Math.Clamp((s.totalMinionsKilled + (double)s.neutralMinionsKilled) / maxFarm, 0, 1) : 0);
        }
        var best = team.OrderByDescending(Score).ThenBy(p => p.stats!.deaths)
            .ThenByDescending(p => p.stats!.assists).ThenBy(p => p.participantId).First();
        return new(best.participantId == player.participantId ? stats.Win ? "胜方 MVP" : "败方 MVP" : null, feats, true);
    }

    private static string Count(string text, int count) => count > 1 ? $"{text}×{count}" : text;
}
