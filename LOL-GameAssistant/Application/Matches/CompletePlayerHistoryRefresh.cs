using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>复用完整基线，只读取新增头部并校验末页；每天重新校准完整可读历史。</summary>
internal static class CompletePlayerHistoryRefresh
{
    internal static async Task<CompletePlayerHistorySnapshot> ReadAsync(IMatchHistoryService source, string puuid,
        MatchHistoryResponse? seed, CompletePlayerHistorySnapshot? cached,
        Action<MatchHistoryResponse> progress, CancellationToken token, DateTimeOffset? now = null)
    {
        var time = now ?? DateTimeOffset.UtcNow;
        MatchHistoryResponse? result = null;
        bool fullScan = true;
        if (cached != null && cached.FullScanAt <= time && time - cached.FullScanAt < TimeSpan.FromDays(1) && ValidBaseline(cached.Data))
        {
            result = await ReadIncrementAsync(source, puuid, seed, cached.Data, token).ConfigureAwait(false);
            fullScan = false;
        }
        if (result == null)
        {
            // 对齐失败时连头部也重新读取，避免刚结束对局时两分钟摘要缓存仍是旧索引。
            result = await CompletePlayerHistoryReader.ReadAsync(source, puuid, fullScan ? seed : null, progress, token).ConfigureAwait(false);
            fullScan = true;
        }
        token.ThrowIfCancellationRequested();
        progress(result);
        var snapshot = new CompletePlayerHistorySnapshot(result, fullScan ? time : cached!.FullScanAt, time);
        if (source is ICompletePlayerHistoryStore store)
            await store.SaveCompleteHistoryAsync(puuid, snapshot, token).ConfigureAwait(false);
        return snapshot;
    }

    private static bool ValidBaseline(MatchHistoryResponse response)
    {
        if (response.Games is not { IsTotalKnown: true, HasMore: false, GameIndexBegin: 0 } data || data.Games.Count == 0 ||
            data.GameCount != data.Games.Count + data.MissingSummaryCount || data.GameIndexEnd != data.GameCount - 1) return false;
        int previous = -1;
        var ids = new HashSet<long>();
        foreach (var game in data.Games)
        {
            if (game.GameId <= 0 || !ids.Add(game.GameId) || game.HistoryIndex is not { } index || index <= previous || index >= data.GameCount) return false;
            previous = index;
        }
        return true;
    }

    private static async Task<MatchHistoryResponse?> ReadIncrementAsync(IMatchHistoryService source, string puuid,
        MatchHistoryResponse? seed, MatchHistoryResponse cached, CancellationToken token)
    {
        string session = PlayerHistorySession.Key;
        var old = cached.Games!;
        var oldById = old.Games.ToDictionary(game => game.GameId);
        var fresh = new List<MatchHistoryGame>();
        var freshIds = new HashSet<long>();
        int offset = 0;
        var page = seed;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            page ??= await source.GetPageAsync(puuid, offset, checked(offset + 99), token).ConfigureAwait(false);
            if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
            var data = ValidatePage(page, offset);
            foreach (var game in data.Games)
            {
                if (!freshIds.Add(game.GameId)) return null;
                fresh.Add(game);
            }
            offset = data.GameIndexEnd + 1;
            if (!data.HasMore && data.IsTotalKnown)
                return Result(fresh, offset, data.Source);

            var overlaps = fresh.Where(game => oldById.ContainsKey(game.GameId)).ToArray();
            if (overlaps.Length > 0)
            {
                if (!freshIds.Contains(old.Games[0].GameId)) return null;
                int shift = overlaps[0].HistoryIndex!.Value - oldById[overlaps[0].GameId].HistoryIndex!.Value;
                if (shift < 0 || overlaps.Any(game => game.HistoryIndex - oldById[game.GameId].HistoryIndex != shift)) return null;
                if (old.Games.Any(game => game.HistoryIndex + shift < offset && !freshIds.Contains(game.GameId))) return null;
                int total = checked(old.GameCount + shift);
                // 末尾锚点必须仍存在，而且服务端确认末页，才能复用未重新下载的中间页。
                var last = old.Games[^1];
                int tailStart = checked(last.HistoryIndex!.Value + shift);
                if (tailStart < offset) return null;
                var tail = await source.GetPageAsync(puuid, tailStart, checked(tailStart + 99), token).ConfigureAwait(false);
                if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
                var tailData = ValidatePage(tail, tailStart);
                if (tailData.HasMore || !tailData.IsTotalKnown || tailData.GameIndexEnd != total - 1 ||
                    !tailData.Games.Any(game => game.GameId == last.GameId && game.HistoryIndex == tailStart)) return null;
                var merged = old.Games.Select(game => game.CopyAtHistoryIndex(checked(game.HistoryIndex!.Value + shift)))
                    .ToDictionary(game => game.GameId);
                foreach (var game in fresh.Concat(tailData.Games)) merged[game.GameId] = game;
                var ordered = merged.Values.OrderBy(game => game.HistoryIndex).ToList();
                if (ordered.Select(game => game.HistoryIndex).Distinct().Count() != ordered.Count || ordered.Count > total) return null;
                return Result(ordered, total, data.Source);
            }
            if (data.Games.Count + data.MissingSummaryCount == 0) throw new IOException("增量历史未确认末页。");
            page = null;
        }
    }

    private static MatchHistoryGames ValidatePage(MatchHistoryResponse? page, int begin)
    {
        if (page?.Games is not { } data || page.IsCached) throw new IOException("未取得实时历史，保留已有统计缓存。");
        int consumed = checked(data.Games.Count + data.MissingSummaryCount);
        if (data.GameIndexBegin != begin || data.GameIndexEnd != begin + consumed - 1 || data.MissingSummaryCount < 0)
            throw new IOException("历史分页索引无效。");
        int previous = begin - 1;
        for (int index = 0; index < data.Games.Count; index++)
        {
            var game = data.Games[index];
            if (data.MissingSummaryCount == 0) game.HistoryIndex = begin + index;
            if (game.GameId <= 0 || game.HistoryIndex is not { } raw || raw <= previous || raw > data.GameIndexEnd)
                throw new IOException("历史原始索引缺失，不能安全合并统计。");
            previous = raw;
        }
        return data;
    }

    private static MatchHistoryResponse Result(List<MatchHistoryGame> games, int total, string source) => new()
    {
        Games = new() { Games = games, GameIndexBegin = 0, GameIndexEnd = total - 1, GameCount = total,
            MissingSummaryCount = total - games.Count, IsTotalKnown = true, HasMore = false,
            IsTruncated = total > games.Count, Source = source }
    };
}
