using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>可选的云端建议增强端口；本地规则引擎不依赖其可用性。</summary>
public interface IAiRecommendationProvider
{
    Task<AiRecommendationResult> CreateAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default);
}