using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>收集当前可见对局上下文并生成训练建议的应用端口。</summary>
public interface IAiCoachingService
{
    Task<AiGameContext> CollectContextAsync(CancellationToken cancellationToken = default);

    Task<AiRecommendationResult> GetRecommendationAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default);
}