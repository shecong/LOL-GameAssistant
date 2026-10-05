using LOL_GameAssistant.Application.Coaching;

namespace LOL_GameAssistant.Infrastructure.Coaching;

/// <summary>将本地可维护的对线知识文件暴露为应用端口。</summary>
public sealed class FileLaneKnowledgeService : ILaneKnowledgeService
{
    /// <summary>根据对局上下文读取适用的本地对线建议。</summary>
    public string GetAdvice(string champion, string role, string? opponent = null) =>
        LocalLaneKnowledgeReader.GetAdvice(champion, role, opponent);
}