using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using static LOL_GameAssistant.GameMain;

namespace LOL_GameAssistant.LoLApi
{
    public static class Game_Api
    {
        /// <summary>
        /// 游戏版本号
        /// </summary>
        public static string gameversion = "15.19.1";

        /// <summary>
        /// 装备信息（初始化为 null，与 jNData 保持一致）
        /// </summary>
        public static List<ZBModel>? zBData = null;

        public static List<JNModel>? jNData = null;

        /// <summary>
        /// LOL排位数据
        /// </summary>
        public static async Task<LolRankedDataParser.RankedData?> GetUserGame(String? puuid)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-ranked/v1/ranked-stats/{puuid}");
            if (responseStream == null) return null;

            LolRankedDataParser parser = new LolRankedDataParser();
            var jsonStr = await responseStream.ReadAsStringJsonAsync();
            if (string.IsNullOrEmpty(jsonStr)) return null;
            return parser.ParseRankedData(jsonStr);
        }

        /// <summary>
        /// 获取游戏最新版本
        /// </summary>
        public static async Task GetGameversionAsync()
        {
            try
            {
                HttpClentHelper client = new HttpClentHelper();
                Stream? responseStream = await client.GetAsync($"https://ddragon.leagueoflegends.com/api/versions.json");
                if (responseStream == null) return;

                List<string>? version = await responseStream.ReadAsJsonAsync<List<string>>();
                if (version != null && version.Count > 0)
                {
                    gameversion = version[0];
                }
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"获取游戏版本失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定召唤师比赛记录
        /// </summary>
        public static async Task<GameHeadModel.MatchHistoryResponse?> GetUserGame(String? puuid, String? begIndex = null, String? endIndex = null)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex={begIndex}&endIndex={endIndex}");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameHeadModel.MatchHistoryResponse>();
        }

        /// <summary>
        /// 获取召唤师图标
        /// </summary>
        public static async Task<Stream> GetGameUserImg(String Key)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/profileicon/{Key}.png");
            if (responseStream == null) return Stream.Null;
            return responseStream;
        }

        /// <summary>
        /// 获取装备图标（返回安全的 MemoryStream 副本）
        /// </summary>
        public static async Task<Stream> GetGameZBImg(String Key)
        {
            if (string.IsNullOrEmpty(Key) || Key == "0")
            {
                using (var bmp = LOL_GameAssistant.Properties.Resources._null)
                {
                    var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    return ms;
                }
            }
            // 按需加载装备信息
            if (zBData == null)
            {
                HttpClentHelper zbclient = new HttpClentHelper();
                Stream? zbStream = await zbclient.GetAsync($"/lol-game-data/assets/v1/items.json");
                if (zbStream != null)
                {
                    zBData = await zbStream.ReadAsJsonAsync<List<ZBModel>>();
                }
            }

            String? Path = zBData?.Find(p => p.id.ToString() == Key)?.iconPath;
            if (string.IsNullOrEmpty(Path))
            {
                return Stream.Null;
            }

            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"{Path}");
            if (responeStream == null) return Stream.Null;

            // 复制到 MemoryStream 使调用方可安全使用 Image.FromStream
            var ms = new MemoryStream();
            await responeStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// 获取召唤师技能图标（返回安全的 MemoryStream 副本）
        /// </summary>
        public static async Task<Stream> GetGameZHSJNImg(String Key)
        {
            // 按需加载召唤师技能信息
            if (jNData == null)
            {
                HttpClentHelper jnclient = new HttpClentHelper();
                Stream? jnStream = await jnclient.GetAsync($"/lol-game-data/assets/v1/summoner-spells.json");
                if (jnStream != null)
                {
                    jNData = await jnStream.ReadAsJsonAsync<List<JNModel>>();
                }
            }

            String? Path = jNData?.Find(p => p.id.ToString() == Key)?.iconPath;
            if (string.IsNullOrEmpty(Path))
            {
                return Stream.Null;
            }

            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"{Path}");
            if (responeStream == null) return Stream.Null;

            var ms = new MemoryStream();
            await responeStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// 获取英雄图标（返回安全的 MemoryStream 副本）
        /// </summary>
        public static async Task<Stream> GetGameYXImg(int id)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responeStream = await client.GetAsync($"/lol-game-data/assets/v1/champion-icons/{id}.png");
            if (responeStream == null) return Stream.Null;

            var ms = new MemoryStream();
            await responeStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// 获取单场对局详情
        /// </summary>
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
        public static async Task OpenGameServerAsync()
        {
            try
            {
                HttpClentHelper client = new HttpClentHelper();
                await client.PostAsync($"/lol-lobby/v2/lobby/matchmaking/search");
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"自动匹配失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 自动接受对局
        /// </summary>
        public static async Task GameTrueServerAsync()
        {
            try
            {
                HttpClentHelper client = new HttpClentHelper();
                await client.PostAsync($"/lol-matchmaking/v1/ready-check/accept");
            }
            catch (Exception ex)
            {
                infoMsg.AddMsg($"自动接受失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取对局实时信息
        /// </summary>
        public static async Task<LobbyGameInfo?> GameNowServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-lobby/v2/lobby");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<LobbyGameInfo>();
        }

        /// <summary>
        /// 获取游戏状态
        /// </summary>
        public static async Task<String?> GameFlowPhaseServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-gameflow/v1/gameflow-phase");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<String>();
        }

        /// <summary>
        /// 进入游戏后可查询队伍信息
        /// </summary>
        public static async Task<GameSessionResponse?> GameLineInfoServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-gameflow/v1/session");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameSessionResponse>();
        }
    }
}
