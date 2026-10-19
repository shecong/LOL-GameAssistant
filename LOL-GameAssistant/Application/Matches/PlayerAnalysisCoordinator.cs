using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

public static class MatchAnalysisSettings
{
    public static Func<MatchAnalysisPolicy> Policy { get; set; } = () => new();
}

/// <summary>应用层协调共享快照和纯统计，任何页面都不为评级额外读取详情。</summary>
public sealed class PlayerAnalysisCoordinator(IMatchHistoryService matches)
{
    public async Task<(PlayerHistorySnapshot Snapshot, PlayerMatchStatistics Statistics)> GetAsync(
        string puuid, int queueId, string? mode, string modeText, int championId = 0,
        CancellationToken token = default, bool refresh = false)
    {
        var snapshot = await PlayerHistoryRepository.For(matches).GetBatchAsync(puuid, token, refresh).ConfigureAwait(false);
        if (!snapshot.Available) throw new IOException(snapshot.Error ?? "战绩摘要暂不可用");
        return (snapshot, Analyze(snapshot.Data!, puuid, queueId, mode, modeText, championId));
    }
    public static PlayerMatchStatistics Analyze(MatchHistoryResponse history, string puuid,
        int queueId, string? mode, string modeText, int championId = 0, MatchAnalysisPolicy? policy = null) =>
        MatchStatisticsCalculator.Calculate(history.Games?.Games ?? [], puuid, queueId, mode, modeText,
            championId, DateTimeOffset.UtcNow, policy ?? MatchAnalysisSettings.Policy());
}
