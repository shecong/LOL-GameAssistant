namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>AI 服务生成建议的结果；失败时不提供本地替代建议。</summary>
public sealed record AiRecommendationResult(
    bool FromAi,
    string ContextSummary,
    string Recommendation,
    AiGameContext Context,
    string? Error)
{
    public static AiRecommendationResult Unavailable(
        string contextSummary,
        AiGameContext context,
        string? error = null) =>
        new(false, contextSummary, "", context, error);
}
