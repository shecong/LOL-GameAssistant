using Newtonsoft.Json;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Resources.ResXFileRef;

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
            return Encoding.UTF8.GetString(Convert.FromBase64String(result.Result));
        }
        public static string GetUser(String puuid)
        {
            Dictionary<string,String> dic = new Dictionary<string, string>();
            dic.Add("puuid", puuid);
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync("/lol-summoner/v2/summoners/puuid", dic);
            return Encoding.UTF8.GetString(Convert.FromBase64String(result.Result));
        }
        public static Stream GetImg(String id)
        { 
            HttpClentHelper client = new HttpClentHelper();
             var result = client.GetAsync($@"/lol-game-data/assets/v1/profile-icons/{id}.jpg");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }

    }
}
