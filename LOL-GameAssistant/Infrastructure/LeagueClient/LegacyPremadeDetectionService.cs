using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>将既有开黑算法与 LCU 战绩采集收口为应用服务。</summary>
public sealed class LegacyPremadeDetectionService : IPremadeDetectionService
{
    public async Task<PremadeDetectionResult> DetectAsync(
        IReadOnlyList<TeamMemberIdentity> teamOne,
        IReadOnlyList<TeamMemberIdentity> teamTwo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await PremadeDetector.DetectAsync(
            teamOne.Select(player => (player.Puuid, player.Name)).ToList(),
            teamTwo.Select(player => (player.Puuid, player.Name)).ToList()).ConfigureAwait(false);

        var groups = result.Groups.Select(group => new PremadeGroup(
            group.Index,
            group.Puuids.ToArray(),
            group.Names.ToArray(),
            group.TeamIndex)).ToArray();
        return new PremadeDetectionResult(groups);
    }
}
