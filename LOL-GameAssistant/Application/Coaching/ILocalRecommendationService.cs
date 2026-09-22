using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>基于玩家可见对局信息生成无需 API Key 的本地时间线建议。</summary>
public interface ILocalRecommendationService
{
    IReadOnlyList<CoachRecommendation> Create(AiGameContext context, DateTimeOffset now);
}