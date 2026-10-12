using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Domain.LiveGame;

/// <summary>Immutable statistics for one player's recent history in the current mode.</summary>
public sealed record BattleHistoryStatistics(
    IReadOnlyList<bool> RecentResults, double? RecentKda,
    int ChampionGames, int ChampionWins, int WindowGames, int ModeGames, int UnknownResults,
    double? AverageKills, double? AverageDeaths, double? AverageAssists, int RecentStreak)
{
    public const int WindowLimit = 100;
    public const int RecentLimit = 10;
    public int RecentGames => RecentResults.Count;
    public int RecentWins => RecentResults.Count(win => win);
    public int RecentLosses => RecentGames - RecentWins;
    public double? RecentWinRate => RecentGames == 0 ? null : 100d * RecentWins / RecentGames;
    public int ChampionLosses => ChampionGames - ChampionWins;
    public double? ChampionWinRate => ChampionGames == 0 ? null : 100d * ChampionWins / ChampionGames;

    public static IReadOnlyList<MatchHistoryGame> SelectWindow(IEnumerable<MatchHistoryGame> history) =>
        history.OrderByDescending(game => game.GameCreation)
            .DistinctBy(game => game.GameId > 0 ? (object)game.GameId : game)
            .Take(WindowLimit).ToArray();

    public static BattleHistoryStatistics Analyze(IEnumerable<MatchHistoryGame> history,
        string puuid, int championId, int queueId, string mode)
    {
        var window = SelectWindow(history);
        var matching = window.Where(game => game.IsCompletedGame() &&
            MatchModeComparer.IsSameMode(queueId, mode, game)).ToArray();
        var samples = matching.Select(game => game.GetParticipant(puuid))
            .Where(player => player?.stats is { HasWinResult: true }).Cast<MatchParticipant>().ToArray();
        var recent = samples.Take(RecentLimit).ToArray();
        bool completeKda = recent.Length > 0 && recent.All(player => player.stats is
            { HasKdaResult: true, kills: >= 0, deaths: >= 0, assists: >= 0 });
        long kills = completeKda ? recent.Sum(player => (long)player.stats!.kills) : 0;
        long deaths = completeKda ? recent.Sum(player => (long)player.stats!.deaths) : 0;
        long assists = completeKda ? recent.Sum(player => (long)player.stats!.assists) : 0;
        var champion = samples.Where(player => championId > 0 && player.championId == championId).ToArray();
        // Unknown results interrupt a streak; never join wins across an unverified match.
        var latest = matching.FirstOrDefault()?.GetParticipant(puuid)?.stats;
        int streak = latest is not { HasWinResult: true } ? 0 : matching.Take(RecentLimit)
            .TakeWhile(game => game.GetParticipant(puuid)?.stats is { HasWinResult: true } result && result.Win == latest.Win)
            .Count() * (latest.Win ? 1 : -1);
        return new(Array.AsReadOnly(recent.Select(player => player.stats!.Win).ToArray()),
            completeKda ? (double)(kills + assists) / Math.Max(1, deaths) : null,
            champion.Length, champion.Count(player => player.stats!.Win), window.Count, matching.Length,
            matching.Length - samples.Length,
            completeKda ? (double)kills / recent.Length : null,
            completeKda ? (double)deaths / recent.Length : null,
            completeKda ? (double)assists / recent.Length : null, streak);
    }
}
