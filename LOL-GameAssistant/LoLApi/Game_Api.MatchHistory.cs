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
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetUserGame(string? puuid, string? begIndex = null, string? endIndex = null)
        {
            if (string.IsNullOrEmpty(puuid)) return null;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync(
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex ?? "0"}&endIndex={endIndex ?? "9999"}");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameHeadModel.MatchHistoryResponse>();
        }

        /// <summary>
        /// 分页拉取指定玩家的全部对局（LCU 单次请求通常有数量上限，按 100 场一页循环，
        /// 直到取满 gameCount 或返回不足一页为止）。
        /// </summary>
        /// <param name="puuid">玩家 puuid。</param>
        /// <param name="maxGames">安全上限，防止异常数据导致无限循环。</param>
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetAllUserGamesAsync(
            string? puuid,
            int maxGames = 5000)
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

            while (begIndex < maxGames)
            {
                int endIndex = begIndex + pageSize - 1;
                var page = await GetUserGame(puuid, begIndex.ToString(), endIndex.ToString());
                var games = page?.Games?.Games;
                if (games == null || games.Count == 0) break;

                merged.Games.Games.AddRange(games);
                total = Math.Max(total, page!.Games!.GameCount);
                begIndex += games.Count;

                // 返回不足一页，或已取满总场数时结束
                if (games.Count < pageSize || (total > 0 && begIndex >= total)) break;
            }

            merged.Games.GameCount = merged.Games.Games.Count;
            return merged;
        }

        /// <summary>
        /// 获取单场对局详情（long 重载，带缓存）。
        /// </summary>
        public static async Task<GameDetailModel.GameInfo?> GetGameDetail(long gameId, bool useCache = true)
        {
            if (useCache && DetailCache.TryGetValue(gameId, out var cached)) return cached;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-match-history/v1/games/{gameId}");
            if (responseStream == null) return null;
            var game = await responseStream.ReadAsJsonAsync<GameDetailModel.GameInfo>();
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
        public static async Task<GameDetailModel.GameInfo?> GetGameDetail(string? gameId, bool useCache = true)
        {
            if (!long.TryParse(gameId, out long id)) return null;
            return await GetGameDetail(id, useCache);
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
