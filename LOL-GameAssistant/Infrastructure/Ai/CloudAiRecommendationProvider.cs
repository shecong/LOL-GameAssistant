using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>当前 OpenAI 兼容 / Claude HTTP 实现的可替换适配器。</summary>
public sealed class CloudAiRecommendationProvider : IAiRecommendationProvider
{
    private readonly CloudAiRecommendationService _service;

    /// <summary>初始化 CloudAiRecommendationProvider 的实例状态，并保存传入的依赖或数据。</summary>
    public CloudAiRecommendationProvider(CloudAiRecommendationService service) => _service = service;

    /// <summary>根据对局上下文及服务设置生成 AI 推荐结果。</summary>
    public Task<AiRecommendationResult> CreateAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default) =>
        _service.GetRecommendationAsync(settings, context, cancellationToken);

    /// <summary>向当前 AI 服务发起测试请求并返回连接结果。</summary>
    public Task<string> TestAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) =>
        _service.TestConnectionAsync(settings, cancellationToken);

    /// <summary>读取当前 AI 服务提供的可用模型列表。</summary>
    public Task<IReadOnlyList<string>> ListModelsAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) =>
        _service.ListModelsAsync(settings, cancellationToken);
}