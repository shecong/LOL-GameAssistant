using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;

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
        var profileTask = ReadAsync(() => profiles.GetByPuuidAsync(member.Puuid, cancellationToken), cancellationToken);
        var rankTask = ReadAsync(() => ranks.GetAsync(member.Puuid, cancellationToken: cancellationToken), cancellationToken);
        var historyTask = ReadAsync(() => history.GetPageAsync(member.Puuid, 0, 100, cancellationToken), cancellationToken);
        await Task.WhenAll(profileTask, rankTask, historyTask);
        PlayerProfile? profile = await profileTask;
        RankedOverview? ranked = await rankTask;
        string queue = session.QueueId == 440 ? RankedQueues.Flex5x5 : RankedQueues.Solo5x5;
        return BattleOverlayPlayer.Create(member, profile?.RiotId ?? member.SummonerName, ranked?.GetQueue(queue),
            (await historyTask)?.Games?.Games ?? [], session.QueueId, session.GameMode);
    }

    private static async Task<T?> ReadAsync<T>(Func<Task<T?>> read, CancellationToken token) where T : class
    {
        try { return await read().WaitAsync(TimeSpan.FromSeconds(15), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }
}
