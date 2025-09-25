using LOL_GameAssistant.Model;
using LOL_GameAssistant.Models;
using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.LoLApi
{
    public static class Game_Api
    {
        /// <summary>
        /// LOL排位数据
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static LolRankedDataParser.RankedData GetUserGame(String puuid)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");

            LolRankedDataParser parser = new LolRankedDataParser();
            return parser.ParseRankedData(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
        }

        /// <summary>
        /// 获取指定召唤师比赛记录
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static MatchHistoryResponse? GetUserGame(String puuid, String? begIndex = null, String? endIndex = null)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}");
            return JsonConvert.DeserializeObject<MatchHistoryResponse>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
        }
    }
}