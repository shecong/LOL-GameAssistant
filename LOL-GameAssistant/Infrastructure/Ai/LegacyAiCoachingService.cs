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

    public LegacyAiCoachingService(IAiGameContextService contextService)
    {
        _contextService = contextService;
    }

    public Task<AiGameContext> CollectContextAsync(CancellationToken cancellationToken = default) =>
        _contextService.CollectAsync(cancellationToken);
}