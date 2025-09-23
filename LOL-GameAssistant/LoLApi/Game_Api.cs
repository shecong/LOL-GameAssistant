using LOL_GameAssistant.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}