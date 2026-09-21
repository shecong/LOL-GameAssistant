using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 应用设置配置，支持本地 JSON 缓存。
    /// </summary>
public class SettingConfig
{
        /// <summary>是否在启动助手时直接启动已配置的国服 LOL 客户端。</summary>
        [JsonProperty("autoLaunchGameClient")]
        public bool AutoLaunchGameClient { get; set; } = false;

        /// <summary>LOL 安装文件夹。启动时会自动扫描其子目录中的 LeagueClient.exe。</summary>
        [JsonProperty("gameClientPath")]
        public string GameClientPath { get; set; } = "";

        /// <summary>助手主窗口透明度，范围 40-100。</summary>
        [JsonProperty("windowOpacityPercent")]
        public int WindowOpacityPercent { get; set; } = 100;

        /// <summary>按住时临时置顶显示助手的虚拟按键名称，默认键盘左上角的 · 键。</summary>
        [JsonProperty("holdToTopHotkey")]
        public string HoldToTopHotkey { get; set; } = "Oem3";

        /// <summary>仅在 LOL 客户端或游戏窗口位于前台时响应置顶快捷键。</summary>
        [JsonProperty("holdToTopOnlyWhenLeagueFocused")]
        public bool HoldToTopOnlyWhenLeagueFocused { get; set; } = true;

        /// <summary>旧版“复制到剪贴板”开关，仅为兼容已有 settings.json 保留。</summary>
        [JsonProperty("quickMessageClipboardEnabled")]
        public bool QuickMessageClipboardEnabled { get; set; } = false;

        /// <summary>是否启用用户快捷键触发的一次性弹幕自动发送。</summary>
        [JsonProperty("quickMessageAutoSendEnabled")]
        public bool QuickMessageAutoSendEnabled { get; set; } = false;

        /// <summary>两次快捷弹幕发送的最小间隔（秒），避免误触连续发送。</summary>
        [JsonProperty("quickMessageSendIntervalSeconds")]
        public int QuickMessageSendIntervalSeconds { get; set; } = 3;

        [JsonProperty("quickMessageLanguage")]
        public string QuickMessageLanguage { get; set; } = "中文";

        [JsonProperty("quickMessageText")]
        public string QuickMessageText { get; set; } = "我去支援，请注意地图。";

        [JsonProperty("quickMessageHotkey")]
        public string QuickMessageHotkey { get; set; } = "F8";

        /// <summary>云端 AI 与推荐功能的设置。</summary>
        [JsonProperty("ai")]
        public AiSettings Ai { get; set; } = new();

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

        /// <summary>兼容旧版本 settings.json，确保新增设置始终可用。</summary>
        public void Normalize()
        {
            Ai ??= new AiSettings();
            WindowOpacityPercent = Math.Clamp(WindowOpacityPercent, 40, 100);
            HoldToTopHotkey = string.IsNullOrWhiteSpace(HoldToTopHotkey) ? "Oem3" : HoldToTopHotkey;
            QuickMessageLanguage = string.IsNullOrWhiteSpace(QuickMessageLanguage) ? "中文" : QuickMessageLanguage;
            QuickMessageText ??= "";
            QuickMessageHotkey = string.IsNullOrWhiteSpace(QuickMessageHotkey) ? "F8" : QuickMessageHotkey;
            QuickMessageSendIntervalSeconds = Math.Clamp(QuickMessageSendIntervalSeconds, 2, 30);
            Ai.Normalize();
        }
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
                var config = JsonConvert.DeserializeObject<SettingConfig>(json) ?? new SettingConfig();
                config.Normalize();
                return config;
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
                config.Normalize();
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
