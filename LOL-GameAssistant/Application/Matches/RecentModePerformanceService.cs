using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>读取最近 30 天的全部战绩摘要，并用同模式有效对局计算近期表现。</summary>
public sealed class RecentModePerformanceService(IMatchHistoryService matches)
{
    private const int PageSize = 100;
    private static readonly SemaphoreSlim DetailGate = new(8, 8);

    /// <summary>收集近期同模式战绩并计算玩家表现评估。</summary>
    public async Task<RecentModePerformanceAssessment> EvaluateAsync(
        string puuid, int queueId, string? gameMode, string modeText,
        MatchHistoryResponse? firstPage = null, CancellationToken cancellationToken = default)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        var seen = new HashSet<long>();
        var assessments = new List<MatchPerformanceAssessment>();
        var wins = new List<bool>();
        int offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MatchHistoryResponse? page = offset == 0 && firstPage != null
                ? firstPage
                : await matches.GetPageAsync(puuid, offset, offset + PageSize - 1, cancellationToken);
            List<MatchHistoryGame>? games = page?.Games?.Games;
            if (games == null || games.Count == 0) break;

            // 分页可能包含重复对局；先按 ID 去重，再排除超过时间范围、重开或不同模式的记录。
            var eligible = games
                .Where(game => game.GameCreation >= cutoff && seen.Add(game.GameId))
                .Where(game => game.IsCompletedGame() && MatchModeComparer.IsSameMode(queueId, gameMode, game))
                .OrderByDescending(game => game.GameCreation)
                .ToArray();

            var samples = await Task.WhenAll(eligible.Select(async game =>
            {
                MatchParticipantStats? stats = game.GetParticipant(puuid)?.stats;
                if (RecentKdaStatsResolver.NeedsDetail(stats))
                {
                    // 仅为不可信摘要补查详情；跨玩家共用限流器，避免详情页同时加载时压满 LCU。
                    await DetailGate.WaitAsync(cancellationToken);
                    try
                    {
                        MatchDetail? detail = await matches.GetDetailAsync(game.GameId, cancellationToken: cancellationToken);
                        stats = RecentKdaStatsResolver.Resolve(stats, detail?.GetParticipant(puuid)?.stats);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch { stats = null; }
                    finally { DetailGate.Release(); }
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

            offset += games.Count;
            // 战绩由新到旧分页，遇到时间截止点或服务端总数后即可结束，无须继续翻旧记录。
            if (games.Count < PageSize ||
                (page?.Games?.GameCount is > 0 && offset >= page.Games.GameCount) ||
                games.Any(game => game.GameCreation > 0 && game.GameCreation < cutoff)) break;
        }

        return RecentModePerformanceEvaluator.Evaluate(modeText, assessments, wins);
    }
}
