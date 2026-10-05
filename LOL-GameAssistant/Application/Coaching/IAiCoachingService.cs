using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>收集当前可见对局上下文的应用端口。</summary>
public interface IAiCoachingService
{
    /// <summary>收集当前对局所需的建议上下文。</summary>
    Task<AiGameContext> CollectContextAsync(CancellationToken cancellationToken = default);
}