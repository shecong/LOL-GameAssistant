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
        ChampSelectSession? legacy = await Select_Api.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : ChampionSelectionSnapshotMapper.Map(legacy);
    }

    public Task<bool> AutoBanAsync(IReadOnlyList<int> championIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Select_Api.AutoBanAsync(championIds.ToList(), cancellationToken);
    }

    public Task<bool> AutoPickAsync(
        IReadOnlyList<int> championIds,
        bool lockIn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Select_Api.AutoPickAsync(championIds.ToList(), lockIn, cancellationToken);
    }

}
