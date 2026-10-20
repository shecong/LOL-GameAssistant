using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.Infrastructure.Sgp;

/// <summary>SGP SUMMARY 同时提供列表及完整参赛统计；DETAILS 是时间线，不能当作赛后详情。</summary>
public sealed class SgpMatchHistoryService : IMatchHistoryService, ILocalCacheStore
{
    private readonly SgpClient client;
    public SgpMatchHistoryService(SgpClient client) { this.client = client; LocalCacheRegistry.Register(this); }
    string ILocalCacheStore.DirectoryPath => Path.Combine(LocalDataCache<MatchDetail>.Root, "player-data", "details", "sgp-summaries");
    (int Entries, long Bytes) ILocalCacheStore.MeasureMemory() { lock (_sync) return (_summaries.Count, _summaries.Count * 16384L); }
    void ILocalCacheStore.Clear() => ClearDetailCache();
    public const int MaximumGames = 5000;
    private readonly object _sync = new();
    private readonly Dictionary<(string? LoginPlatform, string Platform, long Id), MatchDetail> _summaries = [];
    private int _generation;

    public async Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(puuid)) throw new ArgumentException("缺少玩家标识。", nameof(puuid));
        if (beginIndex < 0 || endIndex < beginIndex || (long)endIndex - beginIndex >= MaximumGames)
            throw new ArgumentOutOfRangeException(nameof(endIndex));
        var context = await client.GetContextAsync(cancellationToken).ConfigureAwait(false);
        string session = PlayerHistorySession.Key;
        int generation; lock (_sync) generation = _generation;
        int count = endIndex - beginIndex + 1;
        var games = new List<MatchHistoryGame>();
        var ids = new HashSet<long>();
        bool hasMore = true;
        while (games.Count < count)
        {
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            int take = Math.Min(100, count - games.Count);
            int offset = checked(beginIndex + games.Count);
            var result = await client.ReadAsync(context,
                $"/match-history-query/v1/products/lol/player/{Uri.EscapeDataString(puuid)}/SUMMARY?startIndex={offset}&count={take}", false, cancellationToken).ConfigureAwait(false);
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            if (result["games"] is not JArray rows || rows.Count > take) throw new IOException("SGP 战绩分页响应无效。");
            foreach (var row in rows)
            {
                var mapped = SgpMatchMapper.Map(row, context.Platform);
                mapped.Summary.IsCrossRegion = mapped.Detail.IsCrossRegion = context.IsCrossRegion;
                if (!ids.Add(mapped.Summary.GameId)) throw new IOException("SGP 返回重复对局，已停止分页，避免重复数据。");
                if (mapped.Summary.GetParticipant(puuid) == null) throw new IOException("SGP 对局中找不到请求的玩家。");
                games.Add(mapped.Summary);
                lock (_sync)
                {
                    if (_generation == generation)
                    {
                        if (_summaries.Count >= 1000) _summaries.Clear();
                        _summaries[(context.LoginPlatform, context.Platform, mapped.Detail.GameId)] = mapped.Detail;
                    }
                }
            }
            if (rows.Count < take) { hasMore = false; break; }
        }
        // SGP has no total count. GameCount is a pagination lower bound (one next-page sentinel).
        return new MatchHistoryResponse { Games = new MatchHistoryGames
        {
            Games = games, GameIndexBegin = beginIndex, GameIndexEnd = beginIndex + games.Count - 1,
            GameCount = checked(beginIndex + games.Count + (hasMore ? 1 : 0)),
            IsTotalKnown = !hasMore, HasMore = hasMore, IsTruncated = hasMore, Source = "SGP"
        } };
    }

    public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = MaximumGames, CancellationToken cancellationToken = default)
    {
        if (maxGames <= 0) throw new ArgumentOutOfRangeException(nameof(maxGames));
        return GetPageAsync(puuid, 0, Math.Min(maxGames, MaximumGames) - 1, cancellationToken);
    }

    public async Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true, CancellationToken cancellationToken = default)
    {
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        var context = await client.GetContextAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync) if (useCache && _summaries.TryGetValue((context.LoginPlatform, context.Platform, gameId), out var cached)) return cached;
        var data = await client.ReadAsync(context, $"/match-history-query/v1/products/lol/{context.Platform}_{gameId}/SUMMARY", false, cancellationToken).ConfigureAwait(false);
        var mapped = SgpMatchMapper.Map(data, context.Platform);
        mapped.Detail.IsCrossRegion = context.IsCrossRegion;
        if (mapped.Detail.GameId != gameId) throw new IOException("SGP 返回的对局 ID 不匹配。");
        return mapped.Detail;
    }

    public void ClearDetailCache() { lock (_sync) { _generation++; _summaries.Clear(); } }
}
