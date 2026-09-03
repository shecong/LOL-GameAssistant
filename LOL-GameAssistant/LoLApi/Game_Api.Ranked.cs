using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// Game_Api 排位数据部分：调用 LCU 排位接口并解析（含失败诊断）。
    /// </summary>
    public static partial class Game_Api
    {
        /// <summary>
        /// 获取指定召唤师的排位数据（单双排/灵活组排等）。
        /// 优先 v1 按 puuid 查询；当前玩家查询失败时回退 current-summoner 端点。
        /// </summary>
        public static async Task<LolRankedDataParser.RankedData?> GetRankedStatsAsync(string? puuid, bool isCurrentUser = false)
        {
            if (string.IsNullOrEmpty(puuid)) return null;

            string? json = await GetRankedStatsRawAsync(puuid);

            // 按 puuid 查询失败时，当前玩家回退 current-summoner 端点
            if (string.IsNullOrEmpty(json) && isCurrentUser)
            {
                json = await GetCurrentSummonerRankedStatsRawAsync();
            }

            if (string.IsNullOrEmpty(json)) return null;

            LolRankedDataParser parser = new LolRankedDataParser();
            var data = parser.ParseRankedData(json);

            // 解析失败或缺少主要排位队列时写诊断日志，便于定位客户端返回结构变化
            bool hasSolo = data != null && parser.GetQueueData(data, LolRankedDataParser.QueueTypes.RANKED_SOLO_5x5) != null;
            bool hasFlex = data != null && parser.GetQueueData(data, LolRankedDataParser.QueueTypes.RANKED_FLEX_SR) != null;
            if (data == null || (!hasSolo && !hasFlex))
            {
                WriteRankedDebugLog(puuid, json!);
            }
            return data;
        }

        /// <summary>
        /// 获取排位接口原始 JSON（用于诊断“排位数据获取失败”）。
        /// </summary>
        public static async Task<string?> GetRankedStatsRawAsync(string? puuid)
        {
            if (string.IsNullOrEmpty(puuid)) return null;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");
            if (responseStream == null) return null;
            return await responseStream.ReadAsStringJsonAsync();
        }

        /// <summary>
        /// 获取当前召唤师的排位接口原始 JSON（备用端点）。
        /// </summary>
        private static async Task<string?> GetCurrentSummonerRankedStatsRawAsync()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-ranked/v1/current-ranked-stats");
            if (responseStream == null) return null;
            return await responseStream.ReadAsStringJsonAsync();
        }

        /// <summary>
        /// 排位解析失败时写入诊断日志（程序目录 ranked_debug.log），便于定位接口结构变化。
        /// </summary>
        private static void WriteRankedDebugLog(string puuid, string json)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ranked_debug.log");
                string snippet = json.Length > 1200 ? json[..1200] : json;
                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] puuid={puuid}\n{snippet}\n\n");
            }
            catch
            {
                // 诊断日志失败不影响主流程
            }
        }
    }
}
