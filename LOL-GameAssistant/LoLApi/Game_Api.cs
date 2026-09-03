using System.Collections.Concurrent;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// LCU（League Client Update）数据访问门面。
    /// 按领域拆分为多个 partial 文件：
    ///  - Game_Api.cs          核心：版本号、装备/技能缓存、英雄头像缓存
    ///  - Game_Api.Ranked.cs   排位数据
    ///  - Game_Api.MatchHistory.cs 战绩与对局详情
    ///  - Game_Api.Lobby.cs    大厅 / 匹配 / 对局流程
    /// </summary>
    public static partial class Game_Api
    {
        /// <summary>
        /// 游戏版本号（用于 DataDragon 图标路径）。
        /// </summary>
        public static string gameversion = "15.19.1";

        private static DateTime _lastVersionFetch = DateTime.MinValue;

        /// <summary>
        /// 装备信息（懒加载 + 线程安全）。
        /// </summary>
        public static List<ZBModel>? zBData = new List<ZBModel>();

        /// <summary>
        /// 召唤师技能信息（懒加载 + 线程安全）。
        /// </summary>
        public static List<JNModel>? jNData = new List<JNModel>();

        /// <summary>
        /// 装备/技能数据加载互斥门。
        /// </summary>
        private static readonly SemaphoreSlim DataGate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 对局详情内存缓存，避免对同一场对局重复请求。
        /// </summary>
        private static readonly ConcurrentDictionary<long, GameDetailModel.GameInfo> DetailCache = new();

        /// <summary>
        /// 对局详情缓存上限，超过后整体清空防止内存膨胀。
        /// </summary>
        private const int DetailCacheMax = 600;

        /// <summary>
        /// 英雄头像内存缓存（多个控件共享，避免重复下载）。
        /// </summary>
        private static readonly ConcurrentDictionary<int, Image> ChampionIconCache = new();

        /// <summary>
        /// 获取游戏最新版本（6 小时内不重复拉取）。
        /// </summary>
        public static async Task GetGameversion()
        {
            if ((DateTime.Now - _lastVersionFetch).TotalHours < 6 && !string.IsNullOrEmpty(gameversion)) return;
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("https://ddragon.leagueoflegends.com/api/versions.json");
            if (responseStream == null) return;
            List<string>? version = await responseStream.ReadAsJsonAsync<List<string>>();
            if (version != null && version.Count > 0)
            {
                gameversion = version[0];
                _lastVersionFetch = DateTime.Now;
            }
        }

        /// <summary>
        /// 懒加载装备数据（线程安全）。
        /// </summary>
        private static async Task<List<ZBModel>> GetItemsAsync()
        {
            if (zBData is { Count: > 0 }) return zBData;
            await DataGate.WaitAsync();
            try
            {
                if (zBData is { Count: > 0 }) return zBData;
                HttpClentHelper client = new HttpClentHelper();
                Stream? stream = await client.GetAsync("/lol-game-data/assets/v1/items.json");
                if (stream != null)
                {
                    var data = await stream.ReadAsJsonAsync<List<ZBModel>>();
                    if (data != null) zBData = data;
                }
            }
            finally
            {
                DataGate.Release();
            }
            return zBData ?? new List<ZBModel>();
        }

        /// <summary>
        /// 懒加载召唤师技能数据（线程安全）。
        /// </summary>
        private static async Task<List<JNModel>> GetSpellsAsync()
        {
            if (jNData is { Count: > 0 }) return jNData;
            await DataGate.WaitAsync();
            try
            {
                if (jNData is { Count: > 0 }) return jNData;
                HttpClentHelper client = new HttpClentHelper();
                Stream? stream = await client.GetAsync("/lol-game-data/assets/v1/summoner-spells.json");
                if (stream != null)
                {
                    var data = await stream.ReadAsJsonAsync<List<JNModel>>();
                    if (data != null) jNData = data;
                }
            }
            finally
            {
                DataGate.Release();
            }
            return jNData ?? new List<JNModel>();
        }

        /// <summary>
        /// 根据装备 ID 获取装备名称。
        /// </summary>
        public static async Task<string?> GetItemNameAsync(int itemId)
        {
            if (itemId <= 0) return null;
            var items = await GetItemsAsync();
            return items.FirstOrDefault(p => string.Equals(p.id, itemId.ToString(), StringComparison.Ordinal))?.name;
        }

        /// <summary>
        /// 根据召唤师技能 ID 获取技能名称。
        /// </summary>
        public static async Task<string?> GetSpellNameAsync(int spellId)
        {
            if (spellId <= 0) return null;
            var spells = await GetSpellsAsync();
            return spells.FirstOrDefault(p => string.Equals(p.id, spellId.ToString(), StringComparison.Ordinal))?.name;
        }

        /// <summary>
        /// 获取召唤师图标（DataDragon）。
        /// </summary>
        public static async Task<Stream> GetGameUserImg(string key)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"https://ddragon.leagueoflegends.com/cdn/{gameversion}/img/profileicon/{key}.png");
            return responseStream ?? Stream.Null;
        }

        /// <summary>
        /// 获取装备图标。
        /// </summary>
        public static async Task<Stream> GetGameZBImg(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "0")
            {
                using var bmp = Properties.Resources._null;
                var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return ms;
            }

            var items = await GetItemsAsync();
            string? path = items.FirstOrDefault(p => string.Equals(p.id, key, StringComparison.Ordinal))?.iconPath;
            if (string.IsNullOrEmpty(path)) return Stream.Null;

            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync(path);
            return responseStream ?? Stream.Null;
        }

        /// <summary>
        /// 获取召唤师技能图标。
        /// </summary>
        public static async Task<Stream> GetGameZHSJNImg(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "0") return Stream.Null;
            var spells = await GetSpellsAsync();
            string? path = spells.FirstOrDefault(p => string.Equals(p.id, key, StringComparison.Ordinal))?.iconPath;
            if (string.IsNullOrEmpty(path)) return Stream.Null;

            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync(path);
            return responseStream ?? Stream.Null;
        }

        /// <summary>
        /// 获取英雄图标（LCU 本地资源）。
        /// </summary>
        public static async Task<Stream> GetGameYXImg(int id)
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-game-data/assets/v1/champion-icons/{id}.png");
            return responseStream ?? Stream.Null;
        }

        /// <summary>
        /// 获取英雄头像（带内存缓存，供多个控件共享）。
        /// </summary>
        public static async Task<Image?> GetGameChampionIconAsync(int championId)
        {
            if (championId <= 0) return null;
            if (ChampionIconCache.TryGetValue(championId, out var cached)) return cached;

            try
            {
                using Stream? stream = await GetGameYXImg(championId);
                if (stream == null || stream == Stream.Null) return null;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                using var temp = Image.FromStream(ms);
                var image = new Bitmap(temp);
                ChampionIconCache[championId] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
