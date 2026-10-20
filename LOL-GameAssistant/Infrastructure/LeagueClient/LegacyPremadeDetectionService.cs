using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>保留既有开黑算法，战绩统一从注入的数据源读取，不再旁路调用 LCU。</summary>
public sealed class LegacyPremadeDetectionService(LOL_GameAssistant.Application.Matches.IMatchHistoryService history) : IPremadeDetectionService
{
    /// <summary>根据给定队伍成员及战绩数据判断可能的组队关系。</summary>
    public async Task<PremadeDetectionResult> DetectAsync(
        IReadOnlyList<TeamMemberIdentity> teamOne,
        IReadOnlyList<TeamMemberIdentity> teamTwo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var gate = new SemaphoreSlim(6);
        var histories = await Task.WhenAll(teamOne.Concat(teamTwo).Where(p => !string.IsNullOrWhiteSpace(p.Puuid)).DistinctBy(p => p.Puuid).Select(async player =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var page = await history.GetPageAsync(player.Puuid, 0, 19, cancellationToken).ConfigureAwait(false);
                var data = page == null ? null : Newtonsoft.Json.Linq.JObject.FromObject(page).ToObject<LOL_GameAssistant.Entity.GameHeadModel.MatchHistoryResponse>();
                return (player.Puuid, Data: data);
            }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
        var result = PremadeDetector.DetectCore(
            teamOne.Select(player => (player.Puuid, player.Name)).ToList(),
            teamTwo.Select(player => (player.Puuid, player.Name)).ToList(), histories);

        var groups = result.Groups.Select(group => new PremadeGroup(
            group.Index,
            group.Puuids.ToArray(),
            group.Names.ToArray(),
            group.TeamIndex, IsInferred: true)).ToArray();
        return new PremadeDetectionResult(groups);
    }
}
