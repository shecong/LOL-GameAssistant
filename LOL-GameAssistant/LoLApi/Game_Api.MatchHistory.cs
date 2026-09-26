using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// Game_Api 战绩部分：比赛记录与单场对局详情（带内存缓存）。
    /// </summary>
    public static partial class Game_Api
    {
        /// <summary>
        /// 获取指定召唤师的比赛记录（支持本地分页区间）。
        /// </summary>
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetUserGame(string? puuid, string? begIndex = null, string? endIndex = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(puuid)) return null;
            HttpClientHelper client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync(
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex ?? "0"}&endIndex={endIndex ?? "9999"}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameHeadModel.MatchHistoryResponse>()
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 分页拉取指定玩家的全部对局（LCU 单次请求通常有数量上限，按 100 场一页循环，
        /// 直到取满 gameCount 或返回不足一页为止）。
        /// </summary>
        /// <param name="puuid">玩家 puuid。</param>
        /// <param name="maxGames">安全上限，防止异常数据导致无限循环。</param>
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetAllUserGamesAsync(
            string? puuid,
            int maxGames = 5000,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(puuid)) return null;

            var merged = new GameHeadModel.MatchHistoryResponse
            {
                Games = new GameHeadModel.GamesContainer
                {
                    Games = new List<GameHeadModel.GameInfo>()
                }
            };

            const int pageSize = 100;
            int begIndex = 0;
            int total = -1;
            var seenIds = new HashSet<long>();

            while (begIndex < maxGames)
            {
                int endIndex = begIndex + pageSize - 1;
                var page = await GetUserGame(puuid, begIndex.ToString(), endIndex.ToString(), cancellationToken).ConfigureAwait(false);
                var games = page?.Games?.Games;
                if (games == null)
                    throw new InvalidOperationException("战绩列表读取中断，无法保证结果完整。");
                if (games.Count == 0)
                {
                    if (total > begIndex) throw new InvalidOperationException("战绩列表提前结束，无法保证结果完整。");
                    break;
                }

                var newGames = games.Where(game => seenIds.Add(game.GameId)).ToList();
                if (newGames.Count == 0)
                    throw new InvalidOperationException("战绩分页返回重复数据，无法保证结果完整。");
                merged.Games.Games.AddRange(newGames);
                total = Math.Max(total, page!.Games!.GameCount);
                begIndex += games.Count;

                // 返回不足一页，或已取满总场数时结束
                if (games.Count < pageSize || (total > 0 && begIndex >= total)) break;
            }

            merged.Games.GameCount = merged.Games.Games.Count;
            if (total > begIndex)
                throw new InvalidOperationException($"战绩只读取到 {begIndex}/{total} 场，无法保证结果完整。");
            return merged;
        }

        /// <summary>
        /// 获取单场对局详情（long 重载，带缓存）。
        /// </summary>
        public static async Task<GameDetailModel.GameInfo?> GetGameDetail(long gameId, bool useCache = true,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (useCache && DetailCache.TryGetValue(gameId, out var cached)) return cached;
            HttpClientHelper client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync($"/lol-match-history/v1/games/{gameId}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (responseStream == null) return null;
            GameDetailModel.GameInfo? game = await responseStream.ReadAsJsonAsync<GameDetailModel.GameInfo>()
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (game != null)
            {
                if (DetailCache.Count >= DetailCacheMax) DetailCache.Clear();
                DetailCache[gameId] = game;
            }
            return game;
        }

        /// <summary>
        /// 获取单场对局详情（字符串 ID 重载，带缓存）。
        /// </summary>
        public static async Task<GameDetailModel.GameInfo?> GetGameDetail(string? gameId, bool useCache = true,
            CancellationToken cancellationToken = default)
        {
            if (!long.TryParse(gameId, out long id)) return null;
            return await GetGameDetail(id, useCache, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 清空对局详情缓存（切换查询对象时调用）。
        /// </summary>
        public static void ClearGameDetailCache()
        {
            DetailCache.Clear();
        }
    }
}