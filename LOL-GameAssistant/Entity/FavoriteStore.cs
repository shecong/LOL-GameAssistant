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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LOL-GameAssistant", "favorites.json");

        private static readonly string LegacyFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favorites.json");

        public static string GetCacheFilePath() => CacheFilePath;

        public static List<FavoritePlayer> Load()
        {
            foreach (string path in new[] { CacheFilePath, CacheFilePath + ".bak", LegacyFilePath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<List<FavoritePlayer>>(json) ?? new List<FavoritePlayer>();
                }
                catch { /* 损坏或不可读时尝试下一份。 */ }
            }
            return new List<FavoritePlayer>();
        }

        public static void Save(List<FavoritePlayer> favorites)
        {
            string json = JsonConvert.SerializeObject(favorites, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath)!);
            string temporaryPath = CacheFilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(CacheFilePath))
                File.Replace(temporaryPath, CacheFilePath, CacheFilePath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, CacheFilePath);
        }
    }
}