using LOL_GameAssistant.Model;
using LOL_GameAssistant.Models;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace LOL_GameAssistant.LoLApi
{
    public static class Game_Api
    {
        public static string gameversion = "15.19.1";

        public static List<ZBModel>? zBData=new List<ZBModel>();
        public static List<JNModel>? jNData = new List<JNModel>();
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
        public static void GetGameversion()
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"https://ddragon.leagueoflegends.com/api/versions.json");
            List<string>? version = JsonConvert.DeserializeObject<List<String>>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Result)));
            if (version != null)
            {
                gameversion = version[0];
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
            String? Path = "";
            if (string.IsNullOrEmpty(Key) || Key == "0")
            {
                // 修复：将Bitmap转换为Stream
                using (var bmp = LOL_GameAssistant.Properties.Resources._null)
                {
                    var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    return ms;
                }
            }
            //先读取装备信息
            if (zBData?.Count == 0)
            {
                HttpClentHelper zbclient = new HttpClentHelper();
                var zbresult = zbclient.GetAsync($"/lol-game-data/assets/v1/items.json");
                zBData= JsonConvert.DeserializeObject<List<ZBModel>>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(zbresult.Result)));
            } 
            Path = zBData?.Where(p => p.id.ToString() == Key).FirstOrDefault()?.iconPath;
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"{Path}");
            return new MemoryStream(Convert.FromBase64String(result.Result));
            //HttpClentHelper client = new HttpClentHelper();
            //var result = client.GetAsync($"/lol-game-data/assets/ASSETS/Items/Icons2D/{Key}.png");
            //return new MemoryStream(Convert.FromBase64String(result.Result));
        }

        /// <summary>
        /// 获取召唤师技能图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static Stream GetGameZHSJNImg(String Key)
        {
            String? Path = "";
            //先读取装备信息
            if (jNData==null || jNData?.Count == 0)
            {
                HttpClentHelper jnclient = new HttpClentHelper();
                var jnresult = jnclient.GetAsync($"/lol-game-data/assets/v1/summoner-spells.json");
                jNData = JsonConvert.DeserializeObject<List<JNModel>>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(jnresult.Result)));
            }
            Path=jNData?.Where(p => p.id.ToString() == Key).FirstOrDefault()?.iconPath;
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"{Path}");
            return new MemoryStream(Convert.FromBase64String(result.Result));
        }
        /// <summary>
        /// 获取英雄图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static Stream GetGameYXImg(int id)
        {
            HttpClentHelper client = new HttpClentHelper();
            var result = client.GetAsync($"/lol-game-data/assets/v1/champion-icons/{id}.png");
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