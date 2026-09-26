using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 应用设置配置，支持本地 JSON 缓存。
    /// </summary>
    public class SettingConfig
    {
        [JsonProperty("themeMode")]
        public string ThemeMode { get; set; } = "System";

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

        /// <summary>两次主动喊话的最小间隔（秒）。</summary>
        [JsonProperty("quickMessageSendIntervalSeconds")]
        public int QuickMessageSendIntervalSeconds { get; set; } = 3;

        [JsonProperty("quickMessageCustomPhrases")]
        public string QuickMessageCustomPhrases { get; set; } = "";

        public bool QuickShoutPerCharacter { get; set; }
        public bool QuickShoutSendToAll { get; set; }
        public bool QuickShoutUseClipboard { get; set; }
        public bool QuickShoutHotkeysEnabled { get; set; } = true;
        public string QuickShoutBuiltInHotkey { get; set; } = "F6";
        public string QuickShoutCustomHotkey { get; set; } = "F7";
        public string QuickShoutBatchHotkey { get; set; } = "F8";
        public bool QuickShoutMultiSelectEnabled { get; set; }
        public List<string> QuickShoutSelectedPhrases { get; set; } = new();

        /// <summary>是否在选人后显示 OP.GG 图文出装与符文选择器。</summary>
        [JsonProperty("opggBuildAssistantEnabled")]
        public bool OpggBuildAssistantEnabled { get; set; } = false;

        [JsonProperty("champSelectKdaAnnouncementEnabled")]
        public bool ChampSelectKdaAnnouncementEnabled { get; set; } = false;

        [JsonProperty("gameKdaAnnouncementEnabled")]
        public bool GameKdaAnnouncementEnabled { get; set; } = false;

        [JsonProperty("gameKdaOnePlayerPerLine")]
        public bool GameKdaOnePlayerPerLine { get; set; } = false;

        [JsonProperty("champSelectKdaAnnouncementTemplate")]
        public string ChampSelectKdaAnnouncementTemplate { get; set; } = "【选人近期 KDA 评估】\n{allies}";

        /// <summary>云端 AI 与推荐功能的设置。</summary>
        [JsonProperty("ai")]
        public AiSettings Ai { get; set; } = new();

        /// <summary>自动匹配对局</summary>
        [JsonProperty("autoMatch")]
        public bool AutoMatch { get; set; } = false;

        /// <summary>自动接受对局</summary>
        [JsonProperty("autoAccept")]
        public bool AutoAccept { get; set; } = false;

        [JsonProperty("autoAcceptDelayMinMilliseconds")]
        public int AutoAcceptDelayMinMilliseconds { get; set; } = 0;

        [JsonProperty("autoAcceptDelayMaxMilliseconds")]
        public int AutoAcceptDelayMaxMilliseconds { get; set; } = 0;

        /// <summary>自动禁用英雄</summary>
        [JsonProperty("autoBan")]
        public bool AutoBan { get; set; } = false;

        /// <summary>自动选用英雄</summary>
        [JsonProperty("autoPick")]
        public bool AutoPick { get; set; } = false;

        /// <summary>只预选、不锁定；适合不希望助手代替最终确认的场景。</summary>
        [JsonProperty("autoPickPreselectOnly")]
        public bool AutoPickPreselectOnly { get; set; } = false;

        /// <summary>被补到非主/副位置时停止自动选人。</summary>
        [JsonProperty("skipAutoPickOnFill")]
        public bool SkipAutoPickOnFill { get; set; } = true;

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

        [JsonProperty("autoReturnToLobby")]
        public bool AutoReturnToLobby { get; set; } = false;

        [JsonProperty("autoReturnStartMatchmaking")]
        public bool AutoReturnStartMatchmaking { get; set; } = false;

        [JsonProperty("autoHonor")]
        public bool AutoHonor { get; set; } = false;

        [JsonProperty("quickLobbyQueueId")]
        public int QuickLobbyQueueId { get; set; } = 430;

        /// <summary>用户明确选过的 OP.GG 路线：英雄/模式/分路 → 路线序号。</summary>
        [JsonProperty("opggManualBuildSelections")]
        public Dictionary<string, int> OpggManualBuildSelections { get; set; } = new();

        /// <summary>开机自动启动</summary>
        [JsonProperty("launchOnStartup")]
        public bool LaunchOnStartup { get; set; } = false;

        /// <summary>兼容旧版本 settings.json，确保新增设置始终可用。</summary>
        public void Normalize()
        {
            Ai ??= new AiSettings();
            ThemeMode = ThemeMode is "Light" or "Dark" or "System" ? ThemeMode : "System";
            WindowOpacityPercent = Math.Clamp(WindowOpacityPercent, 40, 100);
            HoldToTopHotkey = string.IsNullOrWhiteSpace(HoldToTopHotkey) ? "Oem3" : HoldToTopHotkey;
            QuickMessageCustomPhrases ??= "";
            QuickShoutBuiltInHotkey = string.IsNullOrWhiteSpace(QuickShoutBuiltInHotkey) ? "F6" : QuickShoutBuiltInHotkey;
            QuickShoutCustomHotkey = string.IsNullOrWhiteSpace(QuickShoutCustomHotkey) ? "F7" : QuickShoutCustomHotkey;
            QuickShoutBatchHotkey = string.IsNullOrWhiteSpace(QuickShoutBatchHotkey) ? "F8" : QuickShoutBatchHotkey;
            QuickShoutSelectedPhrases ??= new List<string>();
            QuickMessageSendIntervalSeconds = Math.Clamp(QuickMessageSendIntervalSeconds, 2, 30);
            AutoAcceptDelayMinMilliseconds = Math.Clamp(AutoAcceptDelayMinMilliseconds, 0, 15000);
            AutoAcceptDelayMaxMilliseconds = Math.Clamp(AutoAcceptDelayMaxMilliseconds, 0, 15000);
            if (AutoAcceptDelayMinMilliseconds > AutoAcceptDelayMaxMilliseconds)
                (AutoAcceptDelayMinMilliseconds, AutoAcceptDelayMaxMilliseconds) = (0, 0);
            ChampSelectKdaAnnouncementTemplate = string.IsNullOrWhiteSpace(ChampSelectKdaAnnouncementTemplate)
                ? "【选人近期 KDA 评估】\n{allies}"
                : ChampSelectKdaAnnouncementTemplate.Trim()[..Math.Min(800, ChampSelectKdaAnnouncementTemplate.Trim().Length)];
            Ai.Normalize();
            QuickLobbyQueueId = Math.Max(1, QuickLobbyQueueId);
            OpggManualBuildSelections ??= new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// 设置缓存管理器（JSON 文件持久化）。
    /// </summary>
    public static class SettingCache
    {
        private static readonly string CacheFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LOL-GameAssistant", "settings.json");

        private static readonly string LegacyFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>
        /// 从本地 JSON 文件加载设置。
        /// </summary>
        public static SettingConfig Load()
        {
            foreach (string path in new[] { CacheFilePath, CacheFilePath + ".bak", LegacyFilePath })
            {
                try { if (File.Exists(path)) return LoadFromFile(path); }
                catch { /* 损坏或不可读时尝试下一份。 */ }
            }
            return new SettingConfig();
        }

        private static SettingConfig LoadFromFile(string path)
        {
            string json = File.ReadAllText(path);
            var config = JsonConvert.DeserializeObject<SettingConfig>(json) ?? new SettingConfig();
            config.Normalize();
            return config;
        }

        /// <summary>
        /// 保存设置到本地 JSON 文件。
        /// </summary>
        public static void Save(SettingConfig config)
        {
            config.Normalize();
            string json = JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            string directory = Path.GetDirectoryName(CacheFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(directory);
            string temporaryPath = CacheFilePath + ".tmp";
            string backupPath = CacheFilePath + ".bak";

            File.WriteAllText(temporaryPath, json);
            if (File.Exists(CacheFilePath))
            {
                // Replace keeps the last known good settings file available if a power loss or
                // process termination happens while the new file is being committed.
                File.Replace(temporaryPath, CacheFilePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, CacheFilePath);
            }
        }

        /// <summary>
        /// 获取缓存文件路径。
        /// </summary>
        public static string GetCacheFilePath() => CacheFilePath;
    }
}
