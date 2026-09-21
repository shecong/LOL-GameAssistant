namespace LOL_GameAssistant.Domain.Settings;

/// <summary>
/// 助手运行设置的领域模型。
/// JSON 字段名和本地加密细节不属于此模型，由基础设施负责兼容和映射。
/// </summary>
public sealed class AssistantSettings
{
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
        GameClientPath ??= "";
        WindowOpacityPercent = Math.Clamp(WindowOpacityPercent, 40, 100);
        HoldToTopHotkey = string.IsNullOrWhiteSpace(HoldToTopHotkey) ? "Oem3" : HoldToTopHotkey;
        QuickMessageLanguage = string.IsNullOrWhiteSpace(QuickMessageLanguage) ? "中文" : QuickMessageLanguage;
        QuickMessageText ??= "";
        QuickMessageHotkey = string.IsNullOrWhiteSpace(QuickMessageHotkey) ? "F8" : QuickMessageHotkey;
        QuickMessageSendIntervalSeconds = Math.Clamp(QuickMessageSendIntervalSeconds, 2, 30);
        CheckIntervalSeconds = Math.Max(1, CheckIntervalSeconds);
        AutoRefreshIntervalSeconds = Math.Max(10, AutoRefreshIntervalSeconds);
        BanChampions ??= new List<string>();
        PickChampions ??= new List<string>();
        Resolution = string.IsNullOrWhiteSpace(Resolution) ? "1920x1080" : Resolution;
        Ai.Normalize();
    }
}
