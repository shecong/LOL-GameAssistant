using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 应用设置配置，支持本地 JSON 缓存。
    /// </summary>
    public class SettingConfig
    {
        /// <summary>自动匹配对局</summary>
        [JsonProperty("autoMatch")]
        public bool AutoMatch { get; set; } = false;

        /// <summary>自动接受对局</summary>
        [JsonProperty("autoAccept")]
        public bool AutoAccept { get; set; } = false;

        /// <summary>自动禁用英雄</summary>
        [JsonProperty("autoBan")]
        public bool AutoBan { get; set; } = false;

        /// <summary>自动选用英雄</summary>
        [JsonProperty("autoPick")]
        public bool AutoPick { get; set; } = false;

        /// <summary>禁用英雄列表</summary>
        [JsonProperty("banChampions")]
        public List<string> BanChampions { get; set; } = new();

        /// <summary>选用英雄列表</summary>
        [JsonProperty("pickChampions")]
        public List<string> PickChampions { get; set; } = new();

        /// <summary>自动禁用检查间隔（秒），自动选英雄不使用此间隔</summary>
        [JsonProperty("checkIntervalSeconds")]
        public int CheckIntervalSeconds { get; set; } = 2;

        /// <summary>窗口分辨率（宽x高）</summary>
        [JsonProperty("resolution")]
        public string Resolution { get; set; } = "1920x1080";

        /// <summary>关闭时最小化到系统托盘</summary>
        [JsonProperty("minimizeToTray")]
        public bool MinimizeToTray { get; set; } = false;

        /// <summary>对局数据自动刷新</summary>
        [JsonProperty("autoRefresh")]
        public bool AutoRefresh { get; set; } = false;

        /// <summary>对局数据自动刷新间隔（秒）</summary>
        [JsonProperty("autoRefreshIntervalSeconds")]
        public int AutoRefreshIntervalSeconds { get; set; } = 30;

        /// <summary>对局结束时托盘提醒</summary>
        [JsonProperty("notifyOnGameEnd")]
        public bool NotifyOnGameEnd { get; set; } = true;

        /// <summary>开机自动启动</summary>
        [JsonProperty("launchOnStartup")]
        public bool LaunchOnStartup { get; set; } = false;
    }

    /// <summary>
    /// 设置缓存管理器（JSON 文件持久化）。
    /// </summary>
    public static class SettingCache
    {
        private static readonly string CacheFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>
        /// 从本地 JSON 文件加载设置。
        /// </summary>
        public static SettingConfig Load()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                    return new SettingConfig();

                string json = File.ReadAllText(CacheFilePath);
                return JsonConvert.DeserializeObject<SettingConfig>(json) ?? new SettingConfig();
            }
            catch
            {
                return new SettingConfig();
            }
        }

        /// <summary>
        /// 保存设置到本地 JSON 文件。
        /// </summary>
        public static void Save(SettingConfig config)
        {
            try
            {
                string json = JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取缓存文件路径。
        /// </summary>
        public static string GetCacheFilePath() => CacheFilePath;
    }
}
