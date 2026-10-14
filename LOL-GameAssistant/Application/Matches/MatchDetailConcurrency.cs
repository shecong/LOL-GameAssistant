namespace LOL_GameAssistant.Application.Matches;

/// <summary>Shared budget for recent rows, overlay samples and performance detail requests.</summary>
internal static class MatchDetailConcurrency
{
    internal static readonly SemaphoreSlim Gate = new(20, 20);
    // 后台评分最多占四个详情槽，为可见战绩和对局浮窗保留容量。
    internal static readonly SemaphoreSlim BackgroundGate = new(4, 4);
}
