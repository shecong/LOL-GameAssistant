namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>云端或本地校验生成的训练建议结果。</summary>
public sealed record AiRecommendationResult(
    bool FromAi,
    string LocalValidation,
    string Recommendation,
    AiGameContext Context,
    string? Error)
{
    public static AiRecommendationResult LocalOnly(
        string validation,
        string recommendation,
        AiGameContext context,
        string? error = null) =>
        new(false, validation, recommendation, context, error);
}