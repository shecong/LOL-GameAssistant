namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>建议的紧急程度；仅用于展示优先级，不会替玩家执行任何游戏操作。</summary>
public enum RecommendationPriority
{
    Info,
    Attention,
    Important
}

/// <summary>区分云端战术分析与本机确定性状态提醒。</summary>
public enum RecommendationSource
{
    CloudAi,
    LocalObservation
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
    DateTimeOffset ExpiresAt)
{
    public string Conditions { get; init; } = "";
    public string InvalidWhen { get; init; } = "局势或装备变化后重新评估";
    public IReadOnlyList<string> EvidenceFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferencedItems { get; init; } = Array.Empty<string>();
    public string ActionKey { get; init; } = "";
    public string ContextVersion { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
    public bool IsFormatError { get; init; }
    public string OpportunityId { get; init; } = "";
    public IReadOnlyList<string> Steps { get; init; } = [];
    public string Why { get; init; } = "";
    public string ExpectedOutcome { get; init; } = "";
    public string Fallback { get; init; } = "";
    public string SemanticKey => (string.IsNullOrWhiteSpace(ActionKey) ? Category + ":" + Title : ActionKey) +
        (OpportunityId.Length > 0 ? ":" + OpportunityId : "");
}
