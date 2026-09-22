using LOL_GameAssistant.Domain.Teams;

namespace LOL_GameAssistant.Application.Teams;

/// <summary>基于近期同队记录推断开黑小组的应用端口。</summary>
public interface IPremadeDetectionService
{
    Task<PremadeDetectionResult> DetectAsync(
        IReadOnlyList<TeamMemberIdentity> teamOne,
        IReadOnlyList<TeamMemberIdentity> teamTwo,
        CancellationToken cancellationToken = default);
}