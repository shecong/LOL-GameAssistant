using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>兼容浮窗加载入口，委托所有窗口共享的战绩仓库。</summary>
internal sealed class BattleOverlayHistoryLoader(IMatchHistoryService history)
{
    private readonly PlayerHistoryRepository _repository = PlayerHistoryRepository.For(history);
    private int _generation;
    internal (int Entries, long Bytes) MeasureCache() => _repository.Measure();
    internal void ClearCache()
    {
        Interlocked.Increment(ref _generation);
        _repository.Clear();
    }
    internal async Task<(IReadOnlyList<MatchHistoryGame> Games, bool Loaded, bool Complete, int WindowGames,
        DateTimeOffset AcquiredAt, bool FromCache)> LoadAsync(GameTeamMember member, ActiveGameSnapshot session, CancellationToken token)
    {
        int generation = Volatile.Read(ref _generation);
        var snapshot = await _repository.GetBatchAsync(member.Puuid, token);
        // Retry unavailable or stale data; an empty but successful history is valid.
        for (int retry = 1; retry <= 2 && (!snapshot.Available || snapshot.IsStale); retry++)
        {
            if (generation != Volatile.Read(ref _generation)) break;
            await Task.Delay(TimeSpan.FromSeconds(retry), token);
            if (generation != Volatile.Read(ref _generation)) break;
            snapshot = await _repository.GetBatchAsync(member.Puuid, token);
        }
        var games = LOL_GameAssistant.Domain.LiveGame.BattleHistoryStatistics.SelectWindow(snapshot.Data?.Games?.Games ?? []);
        return (games, snapshot.Available, snapshot.Available && !snapshot.IsStale,
            games.Count, snapshot.AcquiredAt, snapshot.IsStale);
    }
}
