using System.Collections.Concurrent;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>Loads and verifies history without mutating the shared match-history cache.</summary>
internal sealed class BattleOverlayHistoryLoader(IMatchHistoryService history)
{
    private readonly ConcurrentDictionary<long, Lazy<Task<MatchDetail?>>> _details = new();
    private readonly ConcurrentQueue<long> _completed = new();

    internal async Task<(IReadOnlyList<MatchHistoryGame> Games, bool Loaded, bool Complete, int WindowGames)> LoadAsync(
        GameTeamMember member, ActiveGameSnapshot session, CancellationToken token)
    {
        var source = await ReadWindowAsync(member.Puuid, token);
        if (!source.Loaded) return ([], false, false, 0);
        var window = BattleHistoryStatistics.SelectWindow(source.Games);
        var games = window.Where(game => game.IsCompletedGame() &&
            MatchModeComparer.IsSameMode(session.QueueId, session.GameMode, game))
            .Select(game => CopySample(game, game.GetParticipant(member.Puuid))).ToArray();
        var needsDetail = games.Where((game, index) =>
        {
            var player = game.GetParticipant(member.Puuid);
            return index < BattleHistoryStatistics.RecentLimit || player?.championId == member.ChampionId ||
                player?.stats == null || !player.stats.HasWinResult || !player.stats.HasKdaResult || player.championId <= 0;
        }).ToArray();
        int next = 0, failures = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        async Task WorkerAsync()
        {
            while (true)
            {
                int index = Interlocked.Increment(ref next) - 1;
                if (index >= needsDetail.Length) return;
                var game = needsDetail[index];
                var detail = await GetDetailAsync(game.GameId, timeout.Token);
                var participant = detail?.GetParticipant(member.Puuid);
                if (participant?.stats == null)
                {
                    _details.TryRemove(game.GameId, out _);
                    Interlocked.Increment(ref failures);
                    continue;
                }
                var summary = game.GetParticipant(member.Puuid);
                var stats = participant.stats;
                var merged = new MatchParticipant
                {
                    participantId = participant.participantId,
                    championId = participant.championId > 0 ? participant.championId : summary?.championId ?? 0,
                    stats = new()
                    {
                        kills = stats.HasKdaResult ? stats.kills : summary?.stats?.kills ?? 0,
                        deaths = stats.HasKdaResult ? stats.deaths : summary?.stats?.deaths ?? 0,
                        assists = stats.HasKdaResult ? stats.assists : summary?.stats?.assists ?? 0,
                        Win = stats.HasWinResult ? stats.Win : summary?.stats?.Win ?? false,
                        HasWinResult = stats.HasWinResult || summary?.stats?.HasWinResult == true,
                        HasKdaResult = stats.HasKdaResult || summary?.stats?.HasKdaResult == true
                    }
                };
                if (merged.stats.kills < 0 || merged.stats.deaths < 0 || merged.stats.assists < 0)
                    merged.stats.HasKdaResult = false;
                if (!merged.stats.HasWinResult || !merged.stats.HasKdaResult || merged.championId <= 0)
                    Interlocked.Increment(ref failures);
                game.ParticipantIdentities = [new() { ParticipantId = merged.participantId, Player = new() { Puuid = member.Puuid } }];
                game.Participants = [merged];
            }
        }
        try { await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => WorkerAsync())); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Interlocked.Increment(ref failures); }
        token.ThrowIfCancellationRequested();
        return (games, true, source.Complete && failures == 0, window.Count);
    }

    private async Task<(IReadOnlyList<MatchHistoryGame> Games, bool Loaded, bool Complete)> ReadWindowAsync(
        string puuid, CancellationToken token)
    {
        const int pageSize = 50;
        List<MatchHistoryGame> games = [];
        bool loaded = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            for (int offset = 0; offset < BattleHistoryStatistics.WindowLimit;)
            {
                int end = Math.Min(offset + pageSize, BattleHistoryStatistics.WindowLimit) - 1;
                int requested = end - offset + 1;
                var page = await BattleOverlayService.ReadAsync(ct => history.GetPageAsync(puuid, offset, end, ct), deadline.Token);
                if (page?.Games == null) return (games, loaded, false);
                loaded = true;
                var rows = page.Games.Games.Take(requested).ToArray();
                if (rows.Length == 0) return (games, true, page.Games.GameCount <= offset);
                int previous = BattleHistoryStatistics.SelectWindow(games).Count;
                games.AddRange(rows);
                // A server ignoring the offset must not produce a complete-looking duplicate window.
                if (BattleHistoryStatistics.SelectWindow(games).Count == previous) return (games, true, false);
                offset += rows.Length;
                if (page.Games.GameCount > 0 && offset >= page.Games.GameCount) return (games, true, true);
                if (page.Games.GameCount <= 0 && rows.Length < requested)
                    return (games, true, true);
            }
            return (games, true, true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return (games, loaded, false); }
    }

    private async Task<MatchDetail?> GetDetailAsync(long id, CancellationToken token)
    {
        if (id <= 0) return null;
        // Shared requests have their own deadline. One cancelled player must not cancel other readers.
        var request = _details.GetOrAdd(id, key => new Lazy<Task<MatchDetail?>>(() => FetchDetailAsync(key)));
        var detail = await request.Value.WaitAsync(token);
        // An empty/incomplete response may recover on retry; don't cache it indefinitely.
        if (detail == null || detail.participants.Count == 0 ||
            detail.participants.Any(player => player.stats is not { HasWinResult: true, HasKdaResult: true }) ||
            detail.participantIdentities.Count == 0)
            _details.TryRemove(new KeyValuePair<long, Lazy<Task<MatchDetail?>>>(id, request));
        return detail;
    }

    private async Task<MatchDetail?> FetchDetailAsync(long id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await MatchDetailConcurrency.Gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                return await BattleOverlayService.ReadAsync(ct => history.GetDetailAsync(id, cancellationToken: ct),
                    deadline.Token, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally { MatchDetailConcurrency.Gate.Release(); }
        }
        catch { return null; }
        finally
        {
            _completed.Enqueue(id);
            while (_completed.Count > 256 && _completed.TryDequeue(out long oldest)) _details.TryRemove(oldest, out _);
        }
    }

    private static MatchHistoryGame CopySample(MatchHistoryGame game, MatchParticipant? participant)
    {
        var stats = participant?.stats;
        return new()
        {
            GameId = game.GameId, GameCreation = game.GameCreation, QueueId = game.QueueId,
            GameMode = game.GameMode, GameDuration = game.GameDuration, EndOfGameResult = game.EndOfGameResult,
            ParticipantIdentities = game.ParticipantIdentities,
            Participants = participant == null ? [] : [new()
            {
                participantId = participant.participantId, championId = participant.championId,
                stats = stats == null ? null : new()
                {
                    kills = stats.kills, deaths = stats.deaths, assists = stats.assists, Win = stats.Win,
                    HasWinResult = stats.HasWinResult, HasKdaResult = !RecentKdaStatsResolver.NeedsDetail(stats)
                }
            }]
        };
    }
}
