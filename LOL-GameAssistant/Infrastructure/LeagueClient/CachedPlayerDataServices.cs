using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Infrastructure.GameData;
using System.Collections.Concurrent;

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
        if (!string.IsNullOrWhiteSpace(player?.Puuid))
        {
            _currentPuuid = player.Puuid;
            PlayerHistorySession.SetAccount(player.Puuid);
        }
        // 尚未确认本次客户端登录身份时，不以其他账号的资料替代当前账号。
        return _currentPuuid == null ? player : await ReadWithMetadata(
            $"puuid:{_currentPuuid}", () => Task.FromResult(player), cancellationToken).ConfigureAwait(false);
    }

    public Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default) =>
        ReadWithMetadata($"puuid:{puuid}",
            () => _inner.GetByPuuidAsync(puuid, cancellationToken), cancellationToken);

    public Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default) =>
        ReadWithMetadata($"riot:{gameName.Trim()}#{tagLine.Trim().TrimStart('#')}",
            () => _inner.FindByRiotIdAsync(gameName, tagLine, cancellationToken), cancellationToken);

    public Task<PlayerProfile?> FindByNameAsync(string gameName, CancellationToken cancellationToken = default) =>
        ReadWithMetadata($"name:{gameName.Trim()}",
            () => _inner.FindByNameAsync(gameName, cancellationToken), cancellationToken);
    private async Task<PlayerProfile?> ReadWithMetadata(string key, Func<Task<PlayerProfile?>> read, CancellationToken token)
    {
        var result = await _cache.GetLatestWithMetadataAsync(key, read, token).ConfigureAwait(false);
        return result.Value is { } profile ? profile with { IsCached = result.Cached, AcquiredAt = result.AcquiredAt } : null;
    }
}

/// <summary>排位数据为空时复用该玩家的本地段位；无段位的非空队列仍正常更新。</summary>
internal sealed class CachedRankedStatsService : IRankedStatsService
{
    private string? _currentPuuid;
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
        if (isCurrentUser) _currentPuuid = puuid;
        var readResult = await _cache.GetLatestWithMetadataAsync($"record-v2:{puuid}", async () =>
        {
            var ranked = await _inner.GetAsync(puuid, isCurrentUser || _currentPuuid == puuid, cancellationToken).ConfigureAwait(false);
            return ranked == null ? null : new Snapshot(ranked.Queues, ranked.SeasonEndsAt);
        }, cancellationToken).ConfigureAwait(false);
        var value = readResult.Value;
        if (value == null) return null;
        var result = new RankedOverview { IsCached = readResult.Cached, AcquiredAt = readResult.AcquiredAt };
        foreach (var item in value.Queues) result.Queues[item.Key] = item.Value;
        foreach (var item in value.SeasonEndsAt) result.SeasonEndsAt[item.Key] = item.Value;
        return result;
    }
}

/// <summary>按玩家、分页区间和对局 ID 保存战绩，空响应不覆盖已有记录。</summary>
internal sealed class CachedMatchHistoryService : IMatchHistoryService, IPlayerHistorySnapshotSource
{
    private readonly IMatchHistoryService _inner;
    private readonly LocalDataCache<MatchHistoryResponse> _pages;
    private readonly LocalDataCache<MatchDetail> _details;
    private sealed record StoredBatch(DateTimeOffset AcquiredAt, MatchHistoryResponse Data);
    private readonly LocalDataCache<StoredBatch> _snapshots;
    private readonly ConcurrentDictionary<(string Puuid, int Begin, int End), Lazy<Task<MatchHistoryResponse?>>> _pendingPages = new();

    internal CachedMatchHistoryService(IMatchHistoryService inner, string? root = null)
    {
        _inner = inner;
        string directory = root ?? LocalDataCache<MatchHistoryResponse>.Root;
        _pages = new(Path.Combine(directory, "player-data", "history"), TimeSpan.FromDays(30),
            value => value.Games?.Games?.Count > 0 && value.Games.Games.All(game => game != null && game.GameId > 0));
        _details = new(Path.Combine(directory, "player-data", "details"), TimeSpan.FromDays(30),
            value => value.GameId > 0 && value.participants?.Count > 0);
        _snapshots = new(Path.Combine(directory, "player-data", "snapshots"), TimeSpan.FromDays(30),
            value => value.Data.Games != null);
    }

    public async Task<PlayerHistorySnapshot> ReadSnapshotAsync(string puuid, CancellationToken cancellationToken)
    {
        bool fresh = false;
        string? failure = null;
        var stored = await _snapshots.GetLatestOrCachedAsync($"{PlayerHistorySession.Key}:{puuid}", async () =>
        {
            try
            {
                var page = await _inner.GetPageAsync(puuid, 0, MatchDataPolicy.SummaryLimit - 1, cancellationToken).ConfigureAwait(false);
                if (page?.Games == null) throw new IOException("战绩摘要未返回有效数据");
                fresh = true;
                return new StoredBatch(DateTimeOffset.UtcNow, page);
            }
            catch (Exception error) { failure = error.Message; throw; }
        }, cancellationToken).ConfigureAwait(false);
        return new(puuid, stored?.Data, stored?.AcquiredAt ?? DateTimeOffset.UtcNow,
            stored == null ? HistoryDataState.Unavailable : fresh ? HistoryDataState.Ready : HistoryDataState.Cached, failure);
    }

    public Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (puuid, beginIndex, endIndex);
        Lazy<Task<MatchHistoryResponse?>>? candidate = null;
        candidate = new(() => ReadPageAsync(key, candidate!));
        // A closed card only cancels its own wait, not the batch shared with another view.
        return _pendingPages.GetOrAdd(key, candidate).Value.WaitAsync(cancellationToken);
    }

    private async Task<MatchHistoryResponse?> ReadPageAsync((string Puuid, int Begin, int End) key,
        Lazy<Task<MatchHistoryResponse?>> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            return await _pages.GetLatestOrCachedAsync($"{key.Puuid}:{key.Begin}:{key.End}",
                () => _inner.GetPageAsync(key.Puuid, key.Begin, key.End, deadline.Token), deadline.Token).ConfigureAwait(false);
        }
        finally { _pendingPages.TryRemove(new KeyValuePair<(string, int, int), Lazy<Task<MatchHistoryResponse?>>>(key, request)); }
    }

    // 全量导出仍要求完整读取，不将历史分页缓存当作完整的导出结果。
    public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = 5000, CancellationToken cancellationToken = default) =>
        _inner.GetAllAsync(puuid, maxGames, cancellationToken);

    public Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true, CancellationToken cancellationToken = default) =>
        useCache
            ? _details.GetAsync(PlayerHistorySession.Key + ":" + gameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => _inner.GetDetailAsync(gameId, true, cancellationToken), cancellationToken)
            : _details.GetLatestOrCachedAsync(PlayerHistorySession.Key + ":" + gameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => _inner.GetDetailAsync(gameId, false, cancellationToken), cancellationToken);

    public void ClearDetailCache() => _inner.ClearDetailCache();
}
