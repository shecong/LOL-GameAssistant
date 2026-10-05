using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>
/// 云端模型与本机上下文采集的过渡适配器。
/// 窗体只依赖应用端口，后续可在不改 UI 的前提下将旧静态实现替换为独立 provider。
/// </summary>
public sealed class LegacyAiCoachingService : IAiCoachingService
{
    private readonly IAiGameContextService _contextService;

    /// <summary>初始化 LegacyAiCoachingService 的实例状态，并保存传入的依赖或数据。</summary>
    public LegacyAiCoachingService(IAiGameContextService contextService)
    {
        _contextService = contextService;
    }

    /// <summary>收集当前对局所需的建议上下文。</summary>
    public Task<AiGameContext> CollectContextAsync(CancellationToken cancellationToken = default) =>
        _contextService.CollectAsync(cancellationToken);
}