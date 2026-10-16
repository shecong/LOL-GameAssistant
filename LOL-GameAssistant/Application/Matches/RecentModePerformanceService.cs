using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>读取最近 30 天的全部战绩摘要，并用同模式有效对局计算近期表现。</summary>
public sealed class RecentModePerformanceService(IMatchHistoryService matches)
{
    private const int PageSize = 100;

    /// <summary>收集近期同模式战绩并计算玩家表现评估。</summary>
    public async Task<RecentModePerformanceAssessment> EvaluateAsync(
        string puuid, int queueId, string? gameMode, string modeText,
        MatchHistoryResponse? firstPage = null, CancellationToken cancellationToken = default,
        bool fallbackToAllModes = false, IReadOnlyDictionary<long, MatchDetail>? knownDetails = null,
        int firstPageSize = PageSize)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        var seen = new HashSet<long>();
        var assessments = new List<MatchPerformanceAssessment>();
        var wins = new List<bool>();
        var otherModes = new List<MatchHistoryGame>();
        int offset = 0;
        using var playerDetails = new SemaphoreSlim(2, 2);

        async Task ReadSamplesAsync(IEnumerable<MatchHistoryGame> eligible)
        {
            var samples = await Task.WhenAll(eligible.Select(async game =>
            {
                MatchParticipantStats? stats = game.GetParticipant(puuid)?.stats;
                bool confirmed = false;
                if (RecentKdaStatsResolver.NeedsDetail(stats) && knownDetails != null &&
                    knownDetails.TryGetValue(game.GameId, out var known))
                {
                    stats = RecentKdaStatsResolver.Resolve(stats, known.GetParticipant(puuid)?.stats);
                    confirmed = stats != null;
                }
                if (!confirmed && RecentKdaStatsResolver.NeedsDetail(stats))
                {
                    // 仅为不可信摘要补查详情；跨玩家共用限流器，避免详情页同时加载时压满 LCU。
                    await playerDetails.WaitAsync(cancellationToken);
                    try
                    {
                        await MatchDetailConcurrency.BackgroundGate.WaitAsync(cancellationToken);
                        try
                        {
                            await MatchDetailConcurrency.Gate.WaitAsync(cancellationToken);
                            try
                            {
                                using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                request.CancelAfter(TimeSpan.FromSeconds(5));
                                MatchDetail? detail = await matches.GetDetailAsync(game.GameId, cancellationToken: request.Token)
                                    .WaitAsync(request.Token);
                                stats = RecentKdaStatsResolver.Resolve(stats, detail?.GetParticipant(puuid)?.stats);
                            }
                            finally { MatchDetailConcurrency.Gate.Release(); }
                        }
                        finally { MatchDetailConcurrency.BackgroundGate.Release(); }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch { stats = null; }
                    finally { playerDetails.Release(); }
                }
                return stats;
            }));

            foreach (MatchParticipantStats? stats in samples)
            {
                if (stats == null) continue;
                assessments.Add(new MatchPerformanceAssessment(
                    MatchPerformanceTier.Medium, 0, "", stats.kills, stats.deaths, stats.assists));
                wins.Add(stats.Win);
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool usingFirstPage = offset == 0 && firstPage != null;
            MatchHistoryResponse? page = usingFirstPage
                ? firstPage
                : await matches.GetPageAsync(puuid, offset, offset + PageSize - 1, cancellationToken);
            List<MatchHistoryGame>? games = page?.Games?.Games;
            if (games == null || games.Count == 0) break;
            int previousCount = seen.Count;
            var recent = games.Where(game => seen.Add(game.GameId))
                .Where(game => game.GameCreation >= cutoff && game.IsCompletedGame())
                .OrderByDescending(game => game.GameCreation).ToArray();
            if (seen.Count == previousCount) break;
            await ReadSamplesAsync(recent.Where(game => MatchModeComparer.IsSameMode(queueId, gameMode, game)));
            if (fallbackToAllModes)
                otherModes.AddRange(recent.Where(game => !MatchModeComparer.IsSameMode(queueId, gameMode, game)));

            offset += games.Count;
            // 战绩由新到旧分页，遇到时间截止点或服务端总数后即可结束，无须继续翻旧记录。
            if (games.Count < (usingFirstPage ? firstPageSize : PageSize) ||
                (page?.Games?.GameCount is > 0 && offset >= page.Games.GameCount) ||
                games.Any(game => game.GameCreation > 0 && game.GameCreation < cutoff)) break;
        }

        if (assessments.Count == 0 && fallbackToAllModes)
        {
            await ReadSamplesAsync(otherModes);
            return RecentModePerformanceEvaluator.Evaluate("近期各模式", assessments, wins) with { UsesAllModes = true };
        }
        return RecentModePerformanceEvaluator.Evaluate(modeText, assessments, wins);
    }
}
