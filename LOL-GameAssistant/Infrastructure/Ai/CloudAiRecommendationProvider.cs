using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>当前 OpenAI 兼容 / Claude HTTP 实现的可替换适配器。</summary>
public sealed class CloudAiRecommendationProvider : IAiRecommendationProvider
{
    public Task<AiRecommendationResult> CreateAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default) =>
        CloudAiRecommendationService.GetRecommendationAsync(settings, context, cancellationToken);
}