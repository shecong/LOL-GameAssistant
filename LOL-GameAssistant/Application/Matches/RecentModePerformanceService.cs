using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>兼容原调用点的适配器；摘要获取和统计分别由仓库和纯计算器负责。</summary>
public sealed class RecentModePerformanceService(IMatchHistoryService matches)
{
    public const int SummaryLimit = MatchDataPolicy.SummaryLimit;
    public async Task<RecentModePerformanceAssessment> EvaluateAsync(
        string puuid, int queueId, string? gameMode, string modeText,
        MatchHistoryResponse? firstPage = null, CancellationToken cancellationToken = default,
        bool fallbackToAllModes = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlayerHistorySnapshot? snapshot = null;
        if (firstPage == null)
        {
            snapshot = await PlayerHistoryRepository.For(matches).GetBatchAsync(puuid, cancellationToken).ConfigureAwait(false);
            firstPage = snapshot.Data;
        }
        if (firstPage?.Games == null) throw new InvalidOperationException(snapshot?.Error ?? "战绩摘要暂不可用");
        var policy = MatchAnalysisSettings.Policy();
        var assessment = PlayerAnalysisCoordinator.Analyze(firstPage, puuid, queueId, gameMode, modeText,
            policy: policy with { AllowAllModesFallback = fallbackToAllModes || policy.AllowAllModesFallback }).Assessment;
        return assessment with { Detail = $"{(snapshot?.ScopeText ?? $"已获取的 {firstPage.Games.Games.Count} 条摘要 · 有限样本")}。{assessment.Detail}" };
    }
}
