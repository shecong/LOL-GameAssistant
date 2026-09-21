using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>选人静态 LCU API 的基础设施适配器。</summary>
public sealed class LegacyChampionSelectService : IChampionSelectService
{
    public async Task<ChampionSelectionSnapshot?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ChampSelectSession? legacy = await Select_Api.GetSessionAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : new ChampionSelectionSnapshot
        {
            LocalPlayerCellId = legacy.LocalPlayerCellId,
            Actions = (legacy.Actions ?? new List<List<ChampSelectAction>>())
                .Select(round => (IReadOnlyList<ChampionSelectionAction>)(round ?? new List<ChampSelectAction>())
                    .Select(action => new ChampionSelectionAction
                    {
                        ActorCellId = action.ActorCellId,
                        ChampionId = action.ChampionId,
                        IsAllyAction = action.IsAllyAction,
                        IsInProgress = action.IsInProgress,
                        Completed = action.Completed,
                        Type = action.Type ?? ""
                    })
                    .ToList())
                .ToList(),
            MyTeam = MapMembers(legacy.MyTeam),
            TheirTeam = MapMembers(legacy.TheirTeam)
        };
    }

    public Task<bool> AutoBanAsync(IReadOnlyList<int> championIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Select_Api.AutoBanAsync(championIds.ToList());
    }

    public Task<bool> AutoPickAsync(IReadOnlyList<int> championIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Select_Api.AutoPickAsync(championIds.ToList());
    }

    /// <summary>选人成员 DTO 到领域快照的单向转换。</summary>
    private static IReadOnlyList<ChampionSelectionMember> MapMembers(IEnumerable<ChampSelectTeamMember>? members)
    {
        return members == null
            ? Array.Empty<ChampionSelectionMember>()
            : members.Select(member => new ChampionSelectionMember
            {
                CellId = member.CellId,
                ChampionId = member.ChampionId,
                Puuid = member.Puuid ?? ""
            }).ToList();
    }
}
