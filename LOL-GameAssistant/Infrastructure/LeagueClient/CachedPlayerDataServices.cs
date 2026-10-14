using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>首页和对局共用的玩家资料缓存，接口空响应时按身份回退。</summary>
internal sealed class CachedPlayerProfileService : IPlayerProfileService
{
    private readonly IPlayerProfileService _inner;
    private readonly LocalDataCache<PlayerProfile> _cache;
    private string? _currentPuuid;

    internal CachedPlayerProfileService(IPlayerProfileService inner, string? root = null)
    {
        _inner = inner;
        _cache = new(Path.Combine(root ?? LocalDataCache<PlayerProfile>.Root, "player-data", "profiles"),
            TimeSpan.FromDays(30), value => !string.IsNullOrWhiteSpace(value.Puuid));
    }

    public async Task<PlayerProfile?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        PlayerProfile? player = await _inner.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(player?.Puuid)) _currentPuuid = player.Puuid;
        // 尚未确认本次客户端登录身份时，不以其他账号的资料替代当前账号。
        return _currentPuuid == null ? player : await _cache.GetLatestOrCachedAsync(
            $"puuid:{_currentPuuid}", () => Task.FromResult(player), cancellationToken).ConfigureAwait(false);
    }

    public Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default) =>
        _cache.GetLatestOrCachedAsync($"puuid:{puuid}",
            () => _inner.GetByPuuidAsync(puuid, cancellationToken), cancellationToken);

    public Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default) =>
        _cache.GetLatestOrCachedAsync($"riot:{gameName.Trim()}#{tagLine.Trim().TrimStart('#')}",
            () => _inner.FindByRiotIdAsync(gameName, tagLine, cancellationToken), cancellationToken);

    public Task<PlayerProfile?> FindByNameAsync(string gameName, CancellationToken cancellationToken = default) =>
        _cache.GetLatestOrCachedAsync($"name:{gameName.Trim()}",
            () => _inner.FindByNameAsync(gameName, cancellationToken), cancellationToken);
}

/// <summary>排位数据为空时复用该玩家的本地段位；无段位的非空队列仍正常更新。</summary>
internal sealed class CachedRankedStatsService : IRankedStatsService
{
    private readonly IRankedStatsService _inner;
    private readonly LocalDataCache<Snapshot> _cache;
    // 显式快照保证只读字典属性中的队列数据也能从磁盘完整恢复。
    private sealed record Snapshot(Dictionary<string, RankedQueue> Queues, Dictionary<string, DateTime> SeasonEndsAt);

    internal CachedRankedStatsService(IRankedStatsService inner, string? root = null)
    {
        _inner = inner;
        _cache = new(Path.Combine(root ?? LocalDataCache<Snapshot>.Root, "player-data", "ranked"),
            TimeSpan.FromDays(30), value => value.Queues?.Count > 0 && value.SeasonEndsAt != null);
    }

    public async Task<RankedOverview?> GetAsync(string puuid, bool isCurrentUser = false, CancellationToken cancellationToken = default)
    {
        Snapshot? value = await _cache.GetLatestOrCachedAsync(puuid, async () =>
        {
            var ranked = await _inner.GetAsync(puuid, isCurrentUser, cancellationToken).ConfigureAwait(false);
            return ranked == null ? null : new Snapshot(ranked.Queues, ranked.SeasonEndsAt);
        }, cancellationToken).ConfigureAwait(false);
        if (value == null) return null;
        var result = new RankedOverview();
        foreach (var item in value.Queues) result.Queues[item.Key] = item.Value;
        foreach (var item in value.SeasonEndsAt) result.SeasonEndsAt[item.Key] = item.Value;
        return result;
    }
}

/// <summary>按玩家、分页区间和对局 ID 保存战绩，空响应不覆盖已有记录。</summary>
internal sealed class CachedMatchHistoryService : IMatchHistoryService
{
    private readonly IMatchHistoryService _inner;
    private readonly LocalDataCache<MatchHistoryResponse> _pages;
    private readonly LocalDataCache<MatchDetail> _details;

    internal CachedMatchHistoryService(IMatchHistoryService inner, string? root = null)
    {
        _inner = inner;
        string directory = root ?? LocalDataCache<MatchHistoryResponse>.Root;
        _pages = new(Path.Combine(directory, "player-data", "history"), TimeSpan.FromDays(30),
            value => value.Games?.Games?.Count > 0 && value.Games.Games.All(game => game != null && game.GameId > 0));
        _details = new(Path.Combine(directory, "player-data", "details"), TimeSpan.FromDays(30),
            value => value.GameId > 0 && value.participants?.Count > 0);
    }

    public Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex, CancellationToken cancellationToken = default) =>
        _pages.GetLatestOrCachedAsync($"{puuid}:{beginIndex}:{endIndex}",
            () => _inner.GetPageAsync(puuid, beginIndex, endIndex, cancellationToken), cancellationToken);

    // 全量导出仍要求完整读取，不将历史分页缓存当作完整的导出结果。
    public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = 5000, CancellationToken cancellationToken = default) =>
        _inner.GetAllAsync(puuid, maxGames, cancellationToken);

    public Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true, CancellationToken cancellationToken = default) =>
        useCache
            ? _details.GetAsync(gameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => _inner.GetDetailAsync(gameId, true, cancellationToken), cancellationToken)
            : _details.GetLatestOrCachedAsync(gameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => _inner.GetDetailAsync(gameId, false, cancellationToken), cancellationToken);

    public void ClearDetailCache() => _inner.ClearDetailCache();
}
