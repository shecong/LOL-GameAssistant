using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>收集 AI 建议所需、且玩家正常可见的最小对局上下文。</summary>
public interface IAiGameContextService
{
    Task<AiGameContext> CollectAsync(CancellationToken cancellationToken = default);
}