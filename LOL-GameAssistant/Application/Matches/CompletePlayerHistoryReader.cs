using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>按原始索引读到接口确认的末页，不设置账号历史总场数上限。</summary>
internal static class CompletePlayerHistoryReader
{
    internal static async Task<MatchHistoryResponse> ReadAsync(IMatchHistoryService source, string puuid,
        MatchHistoryResponse? seed, Action<MatchHistoryResponse> progress, CancellationToken token)
    {
        const int pageSize = 100;
        var games = new List<MatchHistoryGame>();
        var ids = new HashSet<long>();
        int offset = 0, missing = 0;
        string session = PlayerHistorySession.Key;
        var page = seed;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            bool initial = page != null;
            page ??= await source.GetPageAsync(puuid, offset, checked(offset + pageSize - 1), token);
            token.ThrowIfCancellationRequested();
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            if (page?.Games is not { } data || page.IsCached)
                throw new IOException("全量历史读取未取得实时数据，不能用旧缓存确认历史总数。");
            int consumed = checked(data.Games.Count + data.MissingSummaryCount);
            if (data.GameIndexBegin != offset || (!initial &&
                (consumed > pageSize || data.GameIndexEnd != offset + consumed - 1)) || consumed < 0)
                throw new IOException("历史分页索引无效，已停止读取。");
            foreach (var game in data.Games)
            {
                if (game.GameId <= 0 || !ids.Add(game.GameId))
                    throw new IOException("历史分页出现无效或重复对局，已停止读取，避免重复统计。");
                games.Add(game);
            }
            missing = checked(missing + data.MissingSummaryCount);
            offset = checked(offset + consumed);
            bool hasMore = data.HasMore || data.GameCount > offset;
            if (consumed == 0 && hasMore || !hasMore && !data.IsTotalKnown)
                throw new IOException("接口未确认历史末页，不能将当前记录视为全部历史。");
            var result = new MatchHistoryResponse { Games = new()
            {
                Games = games.ToList(), GameIndexBegin = 0, GameIndexEnd = offset - 1,
                GameCount = offset, MissingSummaryCount = missing, HasMore = hasMore,
                IsTotalKnown = !hasMore, IsTruncated = hasMore || missing > 0, Source = data.Source
            }};
            progress(result);
            if (!hasMore) return result;
            page = null;
        }
    }
}
