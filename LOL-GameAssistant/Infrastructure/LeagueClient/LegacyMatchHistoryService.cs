using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 将旧 Game_Api 战绩实现收口到基础设施层的过渡适配器。
/// 取消 UI 对静态 API 的直接依赖后，再可独立替换其底层 LCU 请求实现。
/// </summary>
public sealed class LegacyMatchHistoryService : IMatchHistoryService
{
    /// <summary>读取指定玩家的一页历史战绩。</summary>
    public async Task<MatchHistoryResponse?> GetPageAsync(
        string puuid,
        int beginIndex,
        int endIndex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameHeadModel.MatchHistoryResponse? legacy = await Game_Api
            .GetUserGame(puuid, beginIndex.ToString(), endIndex.ToString(), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return LegacyMatchReadModelMapper.ToDomainPage(legacy, beginIndex, endIndex);
    }

    /// <summary>读取指定玩家的历史战绩集合。</summary>
    public async Task<MatchHistoryResponse?> GetAllAsync(
        string puuid,
        int maxGames = 5000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameHeadModel.MatchHistoryResponse? legacy = await Game_Api
            .GetAllUserGamesAsync(puuid, maxGames, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return LegacyMatchReadModelMapper.ToDomain(legacy);
    }

    /// <summary>读取指定对局的完整详情。</summary>
    public async Task<MatchDetail?> GetDetailAsync(
        long gameId,
        bool useCache = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameDetailModel.GameInfo? legacy = await Game_Api.GetGameDetail(gameId, useCache, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return LegacyMatchReadModelMapper.ToDomain(legacy);
    }

    /// <summary>清空已缓存的对局详情。</summary>
    public void ClearDetailCache() => Game_Api.ClearGameDetailCache();
}
