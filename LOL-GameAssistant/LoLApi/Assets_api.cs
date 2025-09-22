using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 资源加载api
    /// </summary>
    public static class Assets_api
    {

        public static string GetUser()
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync("/lol-summoner/v1/current-summoner");
            return result.Result;
        }
        public static string GetUser(String puuid)
        {
            Dictionary<string,String> dic = new Dictionary<string, string>();
            dic.Add("puuid", puuid);
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync("/lol-summoner/v2/summoners/puuid", dic);
            return result.Result;
        }
    }
}
