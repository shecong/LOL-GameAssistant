using System.Text;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 资源加载api
    /// </summary>
    public static class Assets_api
    {
        /// <summary>
        /// 通过 Riot ID（游戏名 + 标签）搜索召唤师，返回 JSON 字符串
        /// </summary>
        public static async Task<string> SearchSummonerByRiotId(string gameName, string tagLine)
        {
            using var client = new HttpClentHelper();

            // 方式一：通过 query 参数搜索
            var queryParams = new Dictionary<string, string>
            {
                { "name", gameName },
                { "tagLine", tagLine }
            };
            Stream? response = await client.GetAsync("/lol-summoner/v1/summoners", queryParams);

            if (response == null)
            {
                // 方式二：通过 REST 路径搜索
                response = await client.GetAsync($"/lol-summoner/v1/summoners/by-name/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}");
            }

            if (response == null)
            {
                // 方式三：不带 tagLine 的遗留搜索
                response = await client.GetAsync($"/lol-summoner/v1/summoners/by-name/{Uri.EscapeDataString(gameName)}");
            }

            if (response == null) return string.Empty;

            using var reader = new StreamReader(response, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        public static async Task<string> GetUser()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-summoner/v1/current-summoner");
            if (responseStream == null)
            {
                return String.Empty;
            }
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                // 4. ReadToEndAsync() 会将流中的所有内容异步读取到一个字符串中
                string content = await reader.ReadToEndAsync();
                return content;
            }
        }

        public static async Task<string> GetUser(String? puuid)
        {
            if (puuid == null) return String.Empty;
            Dictionary<string, String> dic = new Dictionary<string, string>();
            dic.Add("puuid", puuid);
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-summoner/v2/summoners/puuid/{puuid}");
            if (responseStream == null)
            {
                return String.Empty;
            }
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                // 4. ReadToEndAsync() 会将流中的所有内容异步读取到一个字符串中
                string content = await reader.ReadToEndAsync();
                return content;
            }
        }

        public static async Task<Stream> GetImg(String? id)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($@"/lol-game-data/assets/v1/profile-icons/{id}.jpg");
            if (responseStream == null)
            {
                return Stream.Null;
            }
            return responseStream;
        }
    }
}
