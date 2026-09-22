using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>
/// 战绩查询用例端口。
/// 当前返回的类型仍为 LCU 战绩 DTO；后续按页面逐步替换为领域读模型，
/// 表现层不再依赖旧的静态 Game_Api。
/// </summary>
public interface IMatchHistoryService
{
    Task<MatchHistoryResponse?> GetPageAsync(
        string puuid,
        int beginIndex,
        int endIndex,
        CancellationToken cancellationToken = default);

    Task<MatchHistoryResponse?> GetAllAsync(
        string puuid,
        int maxGames = 5000,
        CancellationToken cancellationToken = default);

    Task<MatchDetail?> GetDetailAsync(
        long gameId,
        bool useCache = true,
        CancellationToken cancellationToken = default);

    void ClearDetailCache();
}