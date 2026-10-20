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
        int consumed = 0, missing = 0;
        bool hasMore = true;
        // 页码定位的是服务端原始记录，不是过滤后可读对局的位置。
        while (consumed < count)
        {
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            int take = Math.Min(100, count - consumed);
            int offset = checked(beginIndex + consumed);
            var result = await client.ReadAsync(context,
                $"/match-history-query/v1/products/lol/player/{Uri.EscapeDataString(puuid)}/SUMMARY?startIndex={offset}&count={take}", false, cancellationToken).ConfigureAwait(false);
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            if (result["games"] is not JArray rows || rows.Count > take) throw new IOException("SGP 战绩分页响应无效。");
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // SGP 会保留只有 metadata 的历史索引；已实测其单场 SUMMARY 返回 404。
                // 仅容忍可识别的空内容记录，其他格式、身份和大区异常仍严格拒绝。
                if (row is JObject entry && (entry["json"] == null || entry["json"]!.Type == JTokenType.Null) &&
                    entry["metadata"] is JObject metadata && metadata["match_id"]?.Type == JTokenType.String &&
                    !string.IsNullOrWhiteSpace(metadata.Value<string>("match_id")))
                {
                    string matchId = metadata.Value<string>("match_id")!;
                    string prefix = context.Platform + "_";
                    if (!matchId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        !long.TryParse(matchId[prefix.Length..], out long missingId) || missingId <= 0)
                        throw new IOException("SGP 历史索引的大区或对局 ID 无效。");
                    if (!ids.Add(missingId))
                        throw new IOException("SGP 返回重复对局，已停止分页，避免重复数据。");
                    missing++;
                    continue;
                }
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
            consumed += rows.Count;
            if (rows.Count < take) { hasMore = false; break; }
        }
        // SGP has no total count. GameCount is a pagination lower bound (one next-page sentinel).
        return new MatchHistoryResponse { Games = new MatchHistoryGames
        {
            Games = games, GameIndexBegin = beginIndex, GameIndexEnd = checked(beginIndex + consumed - 1),
            GameCount = checked(beginIndex + consumed + (hasMore ? 1 : 0)),
            MissingSummaryCount = missing,
            IsTotalKnown = !hasMore, HasMore = hasMore, IsTruncated = hasMore || missing > 0, Source = "SGP"
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
