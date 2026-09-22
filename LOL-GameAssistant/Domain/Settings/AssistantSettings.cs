namespace LOL_GameAssistant.Domain.Settings;

/// <summary>
/// 助手运行设置的领域模型。
/// JSON 字段名和本地加密细节不属于此模型，由基础设施负责兼容和映射。
/// </summary>
public sealed class AssistantSettings
{
    /// <summary>界面配色：跟随系统、浅色或深色。</summary>
    public string ThemeMode { get; set; } = "System";

    public bool AutoLaunchGameClient { get; set; }
    public string GameClientPath { get; set; } = "";
    public int WindowOpacityPercent { get; set; } = 100;
    public string HoldToTopHotkey { get; set; } = "Oem3";
    public bool HoldToTopOnlyWhenLeagueFocused { get; set; } = true;
    public bool QuickMessageClipboardEnabled { get; set; }
    public bool QuickMessageAutoSendEnabled { get; set; }
    public int QuickMessageSendIntervalSeconds { get; set; } = 3;
    public string QuickMessageLanguage { get; set; } = "中文";
    public string QuickMessageText { get; set; } = "我去支援，请注意地图。";
    public string QuickMessageHotkey { get; set; } = "F8";
    /// <summary>在选人阶段选定英雄后，显示 OP.GG 图文出装/符文选择器。</summary>
    public bool OpggBuildAssistantEnabled { get; set; }
    /// <summary>选人阵容加载完成后，汇总近期 KDA 并通过 LCU 发送到选人聊天。</summary>
    public bool ChampSelectKdaAnnouncementEnabled { get; set; }
    /// <summary>选人 KDA 汇总消息模板，支持 {players}、{allies}、{enemies} 占位符。</summary>
    public string ChampSelectKdaAnnouncementTemplate { get; set; } = "【选人近期 KDA 评估】\n{allies}\n{enemies}";
    public CloudAiSettings Ai { get; set; } = new();
    public bool AutoMatch { get; set; }
    public bool AutoAccept { get; set; }
    public bool AutoBan { get; set; }
    public bool AutoPick { get; set; }
    public List<string> BanChampions { get; set; } = new();
    public List<string> PickChampions { get; set; } = new();
    public int CheckIntervalSeconds { get; set; } = 2;
    public string Resolution { get; set; } = "1920x1080";
    public bool MinimizeToTray { get; set; }
    public bool AutoRefresh { get; set; }
    public int AutoRefreshIntervalSeconds { get; set; } = 30;
    public bool NotifyOnGameEnd { get; set; } = true;
    public bool LaunchOnStartup { get; set; }

    /// <summary>统一修正旧配置缺失或异常时的安全默认值。</summary>
    public void Normalize()
    {
        Ai ??= new CloudAiSettings();
        ThemeMode = ThemeMode is "Light" or "Dark" or "System" ? ThemeMode : "System";
        GameClientPath ??= "";
        WindowOpacityPercent = Math.Clamp(WindowOpacityPercent, 40, 100);
        HoldToTopHotkey = string.IsNullOrWhiteSpace(HoldToTopHotkey) ? "Oem3" : HoldToTopHotkey;
        QuickMessageLanguage = string.IsNullOrWhiteSpace(QuickMessageLanguage) ? "中文" : QuickMessageLanguage;
        QuickMessageText ??= "";
        QuickMessageHotkey = string.IsNullOrWhiteSpace(QuickMessageHotkey) ? "F8" : QuickMessageHotkey;
        QuickMessageSendIntervalSeconds = Math.Clamp(QuickMessageSendIntervalSeconds, 2, 30);
        ChampSelectKdaAnnouncementTemplate = string.IsNullOrWhiteSpace(ChampSelectKdaAnnouncementTemplate)
            ? "【选人近期 KDA 评估】\n{allies}\n{enemies}"
            : ChampSelectKdaAnnouncementTemplate.Trim()[..Math.Min(800, ChampSelectKdaAnnouncementTemplate.Trim().Length)];
        CheckIntervalSeconds = Math.Max(1, CheckIntervalSeconds);
        AutoRefreshIntervalSeconds = Math.Max(10, AutoRefreshIntervalSeconds);
        BanChampions ??= new List<string>();
        PickChampions ??= new List<string>();
        Resolution = string.IsNullOrWhiteSpace(Resolution) ? "1920x1080" : Resolution;
        Ai.Normalize();
    }
}
