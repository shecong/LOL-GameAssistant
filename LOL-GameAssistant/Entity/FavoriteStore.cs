using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 收藏玩家数据。
    /// </summary>
    public class FavoritePlayer
    {
        public string Puuid { get; set; } = "";
        public string GameName { get; set; } = "";
        public string TagLine { get; set; } = "";
        public string? SummonerLevel { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 收藏列表的本地 JSON 持久化。
    /// </summary>
    public static class FavoriteStore
    {
        private static readonly string CacheFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favorites.json");

        public static string GetCacheFilePath() => CacheFilePath;

        public static List<FavoritePlayer> Load()
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return new List<FavoritePlayer>();
                string json = File.ReadAllText(CacheFilePath);
                return JsonConvert.DeserializeObject<List<FavoritePlayer>>(json) ?? new List<FavoritePlayer>();
            }
            catch
            {
                return new List<FavoritePlayer>();
            }
        }

        public static void Save(List<FavoritePlayer> favorites)
        {
            try
            {
                string json = JsonConvert.SerializeObject(favorites, Formatting.Indented);
                File.WriteAllText(CacheFilePath, json);
            }
            catch
            {
                // 保存失败不阻断主流程
            }
        }
    }
}
