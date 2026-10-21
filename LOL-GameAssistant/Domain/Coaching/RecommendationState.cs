namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>建议协调器的可诊断状态。</summary>
public enum RecommendationStatus
{
    Disabled,
    Collecting,
    NoActiveGame,
    AiReady,
    NoActionNeeded,
    ConfigurationRequired,
    DataUnavailable,
    Failed,
    BudgetPaused,
    FormatError,
    Stale
}

/// <summary>
/// 供页面和浮窗订阅的单一建议状态。Diagnostic 是面向用户的诊断信息，不包含密钥或原始响应。
/// </summary>
public sealed record RecommendationState(
    RecommendationStatus Status,
    AiGameContext Context,
    IReadOnlyList<CoachRecommendation> Recommendations,
    string Diagnostic,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? NextRefreshAt)
{
    public DateTimeOffset? AnalyzedAt { get; init; }
    public int RequestCount { get; init; }
    public int RequestBudget { get; init; }
    public int SessionVersion { get; init; }
    public string Trigger { get; init; } = "";
    public IReadOnlyList<RecommendationHistoryEntry> History { get; init; } = Array.Empty<RecommendationHistoryEntry>();
    public static RecommendationState Initial { get; } = new(
        RecommendationStatus.NoActiveGame,
        new AiGameContext(),
        Array.Empty<CoachRecommendation>(),
        "等待进入英雄选择或对局。",
        DateTimeOffset.MinValue,
        null);
}

public sealed record RecommendationHistoryEntry(DateTimeOffset At, string GameTime, CoachRecommendation Recommendation, string Feedback = "");
