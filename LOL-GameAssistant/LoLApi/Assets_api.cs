using System.Text;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 资源加载api
    /// </summary>
    public static class Assets_api
    {
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