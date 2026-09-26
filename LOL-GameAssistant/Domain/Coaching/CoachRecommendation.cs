namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>建议的紧急程度；仅用于展示优先级，不会替玩家执行任何游戏操作。</summary>
public enum RecommendationPriority
{
    Info,
    Attention,
    Important
}

/// <summary>建议来源；时间线建议仅由 AI 服务生成。</summary>
public enum RecommendationSource
{
    CloudAi
}

/// <summary>
/// 一条可展示、可去重的局内建议。Id 由触发条件构成，相同局势不会重复弹出。
/// </summary>
public sealed record CoachRecommendation(
    string Id,
    string Category,
    RecommendationPriority Priority,
    string Title,
    string Body,
    string Evidence,
    RecommendationSource Source,
    DateTimeOffset ExpiresAt);