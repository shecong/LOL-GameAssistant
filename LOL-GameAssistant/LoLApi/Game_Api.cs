using LOL_GameAssistant.Model;
using LOL_GameAssistant.Models;
using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.LoLApi
{
    public static class Game_Api
    {
        public static string gameversion= "15.19.1";

        /// <summary>
        /// LOL排位数据
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static LolRankedDataParser.RankedData GetUserGame(String? puuid)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");

            LolRankedDataParser parser = new LolRankedDataParser();
            return parser.ParseRankedData(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
        }
        /// <summary>
        /// 获取游戏最新版本
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static  void  GetGameversion()
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"https://ddragon.leagueoflegends.com/api/versions.json");
            List<string>? version=JsonConvert.DeserializeObject<List<String>>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
            if (version != null)
            {
                gameversion= version[0];
            }
        }
        /// <summary>
        /// 获取指定召唤师比赛记录
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static GameHeadModel.MatchHistoryResponse? GetUserGame(String? puuid, String? begIndex = null, String? endIndex = null)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}");
            return JsonConvert.DeserializeObject<GameHeadModel.MatchHistoryResponse>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
        }

        /// <summary>
        /// 获取召唤师图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static Stream GetGameUserImg(String Key)
        { 
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/profileicon/{Key}.png");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }

        /// <summary>
        /// 获取装备图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static Stream GetGameZBImg(String Key)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/item/{Key}.png");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }

        /// <summary>
        /// 获取召唤师技能图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static Stream GetGameZHSJNImg(String Key)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/spell/{Key}.png");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }

        /// <summary>
        /// 获取单场对局详情
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static GameDetailModel.GameInfo? GetGameDetail(String? gameId)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-match-history/v1/games/{gameId}");
            return JsonConvert.DeserializeObject<GameDetailModel.GameInfo>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
        }
    }
}