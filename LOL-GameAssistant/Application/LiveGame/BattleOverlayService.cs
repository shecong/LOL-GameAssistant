using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>Loads each player's independent sources without making partial failures hide the roster.</summary>
public sealed class BattleOverlayService(IPlayerProfileService profiles, IRankedStatsService ranks,
    IMatchHistoryService history)
{
    public async Task<BattleOverlayPlayer> GetPlayerAsync(GameTeamMember member, ActiveGameSnapshot session,
        CancellationToken cancellationToken)
    {
        if (member.IsBot || string.IsNullOrWhiteSpace(member.Puuid))
            return BattleOverlayPlayer.Create(member, member.SummonerName, null, [], session.QueueId, session.GameMode);
        var profileTask = ReadAsync(token => profiles.GetByPuuidAsync(member.Puuid, token), cancellationToken);
        var rankTask = ReadAsync(token => ranks.GetAsync(member.Puuid, cancellationToken: token), cancellationToken);
        var historyTask = ReadHistoryAsync(member, session, cancellationToken);
        await Task.WhenAll(profileTask, rankTask, historyTask);
        PlayerProfile? profile = await profileTask;
        RankedOverview? ranked = await rankTask;
        string queue = session.QueueId == 440 ? RankedQueues.Flex5x5 : RankedQueues.Solo5x5;
        bool validProfile = profile?.Puuid == member.Puuid && !string.IsNullOrWhiteSpace(profile.GameName);
        var recent = await historyTask;
        return BattleOverlayPlayer.Create(member, validProfile ? profile!.RiotId : member.SummonerName, ranked?.GetQueue(queue),
            recent.Games, session.QueueId, session.GameMode) with
        {
            ProfileLoaded = validProfile, RankLoaded = ranked != null,
            HistoryLoaded = recent.Loaded, HistoryComplete = recent.Complete
        };
    }

    private async Task<(IReadOnlyList<MatchHistoryGame> Games, bool Loaded, bool Complete)> ReadHistoryAsync(
        GameTeamMember member, ActiveGameSnapshot session, CancellationToken token)
    {
        var page = await ReadAsync(ct => history.GetPageAsync(member.Puuid, 0, 99, ct), token);
        if (page?.Games == null) return ([], false, false);
        var games = page.Games.Games.OrderByDescending(game => game.GameCreation)
            .DistinctBy(game => game.GameId > 0 ? (object)game.GameId : game).Take(100)
            .Where(game => game.IsCompletedGame() && MatchModeComparer.IsSameMode(session.QueueId, session.GameMode, game))
            .Select(game => CopySample(game, game.GetParticipant(member.Puuid))).ToArray();
        // Win/hero samples remain available even if their KDA cannot be confirmed.
        var needsDetail = games.Where(game =>
        {
            var participant = game.GetParticipant(member.Puuid);
            return participant?.stats == null || !participant.stats.HasWinResult ||
                !participant.stats.HasKdaResult || participant.championId <= 0;
        }).ToArray();
        int next = 0;
        bool complete = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        async Task WorkerAsync()
        {
            while (true)
            {
                int index = Interlocked.Increment(ref next) - 1;
                if (index >= needsDetail.Length) return;
                var game = needsDetail[index];
                await MatchDetailConcurrency.Gate.WaitAsync(timeout.Token);
                MatchDetail? detail;
                try
                {
                    detail = await ReadAsync(ct => history.GetDetailAsync(game.GameId, cancellationToken: ct),
                        timeout.Token, TimeSpan.FromSeconds(5));
                }
                finally { MatchDetailConcurrency.Gate.Release(); }
                var participant = detail?.GetParticipant(member.Puuid);
                if (participant?.stats == null)
                {
                    complete = false;
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
                if (!merged.stats.HasWinResult || !merged.stats.HasKdaResult || merged.championId <= 0) complete = false;
                game.ParticipantIdentities = [new() { ParticipantId = merged.participantId, Player = new() { Puuid = member.Puuid } }];
                game.Participants = [merged];
            }
        }
        try { await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => WorkerAsync())); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { complete = false; }
        token.ThrowIfCancellationRequested();
        return (games, true, complete);
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

    private static async Task<T?> ReadAsync<T>(Func<CancellationToken, Task<T?>> read, CancellationToken token,
        TimeSpan? duration = null) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(duration ?? TimeSpan.FromSeconds(15));
        try { return await read(timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }
}
