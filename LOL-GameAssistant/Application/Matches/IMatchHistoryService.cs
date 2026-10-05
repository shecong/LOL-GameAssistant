using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>
/// 战绩查询用例端口。
/// 当前返回的类型仍为 LCU 战绩 DTO；后续按页面逐步替换为领域读模型，
/// 表现层不再依赖旧的静态 Game_Api。
/// </summary>
public interface IMatchHistoryService
{
    /// <summary>读取指定玩家的一页历史战绩。</summary>
    Task<MatchHistoryResponse?> GetPageAsync(
        string puuid,
        int beginIndex,
        int endIndex,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定玩家的历史战绩集合。</summary>
    Task<MatchHistoryResponse?> GetAllAsync(
        string puuid,
        int maxGames = 5000,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定对局的完整详情。</summary>
    Task<MatchDetail?> GetDetailAsync(
        long gameId,
        bool useCache = true,
        CancellationToken cancellationToken = default);

    /// <summary>清空已缓存的对局详情。</summary>
    void ClearDetailCache();
}