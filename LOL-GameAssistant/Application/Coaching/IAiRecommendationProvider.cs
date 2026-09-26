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

    /// <summary>
    /// 用一次最小请求验证服务商、模型与密钥是否可用；不发送任何对局数据。
    /// 失败时抛出带原因说明的异常，成功时返回模型回复的文本。
    /// </summary>
    Task<string> TestAsync(CloudAiSettings settings, CancellationToken cancellationToken = default);

    /// <summary>读取该服务商当前可用的模型名称，供设置页直接选择。</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CloudAiSettings settings, CancellationToken cancellationToken = default);
}