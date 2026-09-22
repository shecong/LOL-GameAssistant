using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>根据当前可见对局上下文生成 AI 时间线建议的端口。</summary>
public interface IAiRecommendationProvider
{
    Task<AiRecommendationResult> CreateAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default);
}
