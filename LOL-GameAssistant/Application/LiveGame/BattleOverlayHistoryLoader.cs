using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>兼容浮窗加载入口，委托所有窗口共享的战绩仓库。</summary>
internal sealed class BattleOverlayHistoryLoader(IMatchHistoryService history)
{
    private readonly PlayerHistoryRepository _repository = PlayerHistoryRepository.For(history);
    internal (int Entries, long Bytes) MeasureCache() => _repository.Measure();
    internal void ClearCache() => _repository.Clear();
    internal async Task<(IReadOnlyList<MatchHistoryGame> Games, bool Loaded, bool Complete, int WindowGames,
        DateTimeOffset AcquiredAt, bool FromCache)> LoadAsync(GameTeamMember member, ActiveGameSnapshot session, CancellationToken token)
    {
        var snapshot = await _repository.GetBatchAsync(member.Puuid, token);
        var games = LOL_GameAssistant.Domain.LiveGame.BattleHistoryStatistics.SelectWindow(snapshot.Data?.Games?.Games ?? []);
        return (games, snapshot.Available, snapshot.Available && !snapshot.IsStale,
            games.Count, snapshot.AcquiredAt, snapshot.IsStale);
    }
}
