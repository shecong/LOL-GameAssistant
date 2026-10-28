using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Application.Coaching;

public interface IMatchTimelineService
{
    Task<JungleTimeline> GetAsync(long gameId, CancellationToken cancellationToken = default);
}
