using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>Loads each player's independent sources without making partial failures hide the roster.</summary>
public sealed class BattleOverlayService(IPlayerProfileService profiles, IRankedStatsService ranks,
    IMatchHistoryService history)
{
    private readonly BattleOverlayHistoryLoader _history = new(history);

    public async Task<BattleOverlayPlayer> GetPlayerAsync(GameTeamMember member, ActiveGameSnapshot session,
        CancellationToken cancellationToken, Action<BattleOverlayPlayer>? onUpdated = null)
    {
        if (member.IsBot || string.IsNullOrWhiteSpace(member.Puuid))
            return BattleOverlayPlayer.Create(member, member.SummonerName, null, [], session.QueueId, session.GameMode);
        string queue = session.QueueId == 440 ? RankedQueues.Flex5x5 : RankedQueues.Solo5x5;
        object sync = new();
        var current = BattleOverlayPlayer.Create(member, member.SummonerName, null, [], session.QueueId, session.GameMode) with
        {
            ProfileLoaded = false, RankLoaded = false, HistoryLoaded = false, HistoryComplete = false
        };
        void Publish() { if (!cancellationToken.IsCancellationRequested) onUpdated?.Invoke(current); }
        async Task LoadProfileAsync()
        {
            PlayerProfile? profile = await ReadAsync(token => profiles.GetByPuuidAsync(member.Puuid, token), cancellationToken);
            bool valid = profile?.Puuid == member.Puuid && !string.IsNullOrWhiteSpace(profile.GameName);
            lock (sync)
            {
                current = current with { Name = valid ? profile!.RiotId : member.SummonerName, ProfileLoaded = valid };
                Publish();
            }
        }
        async Task LoadRankAsync()
        {
            RankedOverview? ranked = await ReadAsync(token => ranks.GetAsync(member.Puuid, cancellationToken: token), cancellationToken);
            lock (sync)
            {
                current = current with { Rank = ranked?.GetQueue(queue), RankLoaded = ranked != null };
                Publish();
            }
        }
        async Task LoadHistoryAsync()
        {
            var recent = await _history.LoadAsync(member, session, cancellationToken);
            var stats = BattleHistoryStatistics.Analyze(recent.Games, member.Puuid, member.ChampionId, session.QueueId, session.GameMode,
                MatchAnalysisSettings.Policy())
                with { WindowGames = recent.WindowGames };
            lock (sync)
            {
                current = current with { History = stats, HistoryLoaded = recent.Loaded,
                    HistoryComplete = recent.Complete && stats.UnknownResults == 0,
                    HistoryAcquiredAt = recent.AcquiredAt, HistoryFromCache = recent.FromCache };
                Publish();
            }
        }
        await Task.WhenAll(LoadProfileAsync(), LoadRankAsync(), LoadHistoryAsync());
        cancellationToken.ThrowIfCancellationRequested();
        return current;
    }

    internal static async Task<T?> ReadAsync<T>(Func<CancellationToken, Task<T?>> read, CancellationToken token,
        TimeSpan? duration = null) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(duration ?? TimeSpan.FromSeconds(15));
        try { return await read(timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }
}
