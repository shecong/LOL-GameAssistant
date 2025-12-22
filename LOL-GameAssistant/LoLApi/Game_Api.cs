using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.LoLApi
{
    public static class Game_Api
    {
        /// <summary>
        /// 游戏版本号
        /// </summary>
        public static string gameversion = "15.19.1";

        /// <summary>
        /// 装备信息
        /// </summary>
        public static List<ZBModel>? zBData = new List<ZBModel>();

        public static List<JNModel>? jNData = new List<JNModel>();

        /// <summary>
        /// LOL排位数据
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<LolRankedDataParser.RankedData> GetUserGame(String? puuid)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");

            LolRankedDataParser parser = new LolRankedDataParser();
            return parser.ParseRankedData(await responseStream.ReadAsStringJsonAsync<String>());
        }

        /// <summary>
        /// 获取游戏最新版本
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async void GetGameversion()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"https://ddragon.leagueoflegends.com/api/versions.json");
            List<string>? version = await responseStream.ReadAsJsonAsync<List<string>>();
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
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetUserGame(String? puuid, String? begIndex = null, String? endIndex = null)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}");
            return await responseStream.ReadAsJsonAsync<GameHeadModel.MatchHistoryResponse>();
        }

        /// <summary>
        /// 获取召唤师图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<Stream> GetGameUserImg(String Key)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/profileicon/{Key}.png");
            if (responseStream == null) return Stream.Null;
            return responseStream;
        }

        /// <summary>
        /// 获取装备图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<Stream> GetGameZBImg(String Key)
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
                Stream? zbStream = await zbclient.GetAsync($"/lol-game-data/assets/v1/items.json");
                zBData = await zbStream.ReadAsJsonAsync<List<ZBModel>>();
            }
            Path = zBData?.Where(p => p.id.ToString() == Key).FirstOrDefault()?.iconPath;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"{Path}");
            if (responeStream == null) return Stream.Null;
            return responeStream;
            //HttpClentHelper client = new HttpClentHelper();
            //var result = client.GetAsync($"/lol-game-data/assets/ASSETS/Items/Icons2D/{Key}.png");
            //return new MemoryStream(Convert.FromBase64String(result.Result));
        }

        /// <summary>
        /// 获取召唤师技能图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<Stream> GetGameZHSJNImg(String Key)
        {
            String? Path = "";
            //先读取装备信息
            if (jNData == null || jNData?.Count == 0)
            {
                HttpClentHelper jnclient = new HttpClentHelper();
                Stream? jnStream = await jnclient.GetAsync($"/lol-game-data/assets/v1/summoner-spells.json");
                jNData = await jnStream.ReadAsJsonAsync<List<JNModel>>();
            }
            Path = jNData?.Where(p => p.id.ToString() == Key).FirstOrDefault()?.iconPath;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"{Path}");
            if (responeStream == null) return Stream.Null;
            return responeStream;
        }

        /// <summary>
        /// 获取英雄图标
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<Stream> GetGameYXImg(int id)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"/lol-game-data/assets/v1/champion-icons/{id}.png");
            if (responeStream == null) return Stream.Null;
            return responeStream;
        }

        /// <summary>
        /// 获取单场对局详情
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async Task<GameDetailModel.GameInfo?> GetGameDetail(String? gameId)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"/lol-match-history/v1/games/{gameId}");
            if (responeStream == null) return null;
            return await responeStream.ReadAsJsonAsync<GameDetailModel.GameInfo>();
        }

        /// <summary>
        /// 自动匹配对局
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async void OpenGameServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.PostAsync($"/lol-lobby/v2/lobby/matchmaking/search");
        }

        /// <summary>
        /// 自动接受对局
        /// </summary>
        /// <param name="puuid"></param>
        /// <returns></returns>
        public static async void GameTrueServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.PostAsync($"/lol-matchmaking/v1/ready-check/accept");
        }
    }
}