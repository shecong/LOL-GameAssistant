namespace LOL_GameAssistant.Domain.Matches;

public enum LiveHistoryFilter
{
    Latest,
    CurrentMode
}

public static class LiveHistorySelection
{
    public static MatchHistoryGame[] Select(IEnumerable<MatchHistoryGame> games,
        LiveHistoryFilter filter, int queueId, string? mode) => games
        .Where(game => game.GameId > 0)
        .OrderByDescending(game => game.GameCreation)
        .DistinctBy(game => game.GameId)
        .Where(game => filter != LiveHistoryFilter.CurrentMode || MatchModeComparer.IsSameMode(queueId, mode, game))
        .Take(10).ToArray();
}
