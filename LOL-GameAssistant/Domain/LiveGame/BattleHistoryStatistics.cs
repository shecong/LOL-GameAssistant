using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Domain.LiveGame;

/// <summary>浮窗采用同一近50场同模式评级；近期十场胜负另行展示。</summary>
public sealed record BattleHistoryStatistics(
    IReadOnlyList<bool> RecentResults, double? RecentKda,
    int ChampionGames, int ChampionWins, int WindowGames, int ModeGames, int UnknownResults,
    double? AverageKills, double? AverageDeaths, double? AverageAssists, int RecentStreak)
{
    public const int WindowLimit = 500;
    public const int RecentLimit = 10;
    public RecentModePerformanceAssessment? Assessment { get; init; }
    public int RecentGames => RecentResults.Count;
    public int RecentWins => RecentResults.Count(win => win);
    public int RecentLosses => RecentGames - RecentWins;
    public double? RecentWinRate => RecentGames == 0 ? null : 100d * RecentWins / RecentGames;
    public int ChampionLosses => ChampionGames - ChampionWins;
    public double? ChampionWinRate => ChampionGames == 0 ? null : 100d * ChampionWins / ChampionGames;
    public static IReadOnlyList<MatchHistoryGame> SelectWindow(IEnumerable<MatchHistoryGame> history) =>
        history.OrderByDescending(game => game.GameCreation).DistinctBy(game => game.GameId > 0 ? (object)game.GameId : game)
            .Take(WindowLimit).ToArray();
    public static BattleHistoryStatistics Analyze(IEnumerable<MatchHistoryGame> history,
        string puuid, int championId, int queueId, string mode, MatchAnalysisPolicy? policy = null)
    {
        var window = SelectWindow(history);
        var result = MatchStatisticsCalculator.Calculate(window, puuid, queueId, mode,
            LolGameModeNames.GetModeText(queueId.ToString(), mode), championId, DateTimeOffset.UtcNow, policy);
        return new(result.RecentResults, result.Assessment.SampleSize == 0 ? null : result.Assessment.Kda,
            result.ChampionGames, result.ChampionWins, window.Count, result.Games.Count, result.UnknownResults,
            result.AverageKills, result.AverageDeaths, result.AverageAssists, result.RecentStreak)
            { Assessment = result.Assessment };
    }
}
