namespace LOL_GameAssistant.Application.Coaching;

/// <summary>查询可维护的中文对线知识。</summary>
public interface ILaneKnowledgeService
{
    string GetAdvice(string champion, string role, string? opponent = null);
}