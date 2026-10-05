namespace LOL_GameAssistant.Application.Coaching;

/// <summary>查询可维护的中文对线知识。</summary>
public interface ILaneKnowledgeService
{
    /// <summary>根据对局上下文读取适用的本地对线建议。</summary>
    string GetAdvice(string champion, string role, string? opponent = null);
}