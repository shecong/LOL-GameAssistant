using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>当前 OpenAI 兼容 / Claude HTTP 实现的可替换适配器。</summary>
public sealed class CloudAiRecommendationProvider : IAiRecommendationProvider
{
    private readonly CloudAiRecommendationService _service;

    public CloudAiRecommendationProvider(CloudAiRecommendationService service) => _service = service;

    public Task<AiRecommendationResult> CreateAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default) =>
        _service.GetRecommendationAsync(settings, context, cancellationToken);

    public Task<string> TestAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) =>
        _service.TestConnectionAsync(settings, cancellationToken);

    public Task<IReadOnlyList<string>> ListModelsAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) =>
        _service.ListModelsAsync(settings, cancellationToken);
}