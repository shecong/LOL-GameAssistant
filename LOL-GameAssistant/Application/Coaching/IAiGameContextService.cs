using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>收集 AI 建议所需、且玩家正常可见的最小对局上下文。</summary>
public interface IAiGameContextService
{
    /// <summary>收集当前游戏阶段可用的 AI 建议上下文。</summary>
    Task<AiGameContext> CollectAsync(CancellationToken cancellationToken = default);
}