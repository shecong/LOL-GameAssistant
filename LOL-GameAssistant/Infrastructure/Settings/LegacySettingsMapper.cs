using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>旧 settings.json DTO 与领域设置模型间的兼容映射。</summary>
internal static class LegacySettingsMapper
{
    public static AssistantSettings ToDomain(SettingConfig source)
    {
        source.Normalize();
        var settings = new AssistantSettings
        {
            ThemeMode = source.ThemeMode,
            AutoLaunchGameClient = source.AutoLaunchGameClient,
            GameClientPath = source.GameClientPath,
            WindowOpacityPercent = source.WindowOpacityPercent,
            HoldToTopHotkey = source.HoldToTopHotkey,
            HoldToTopOnlyWhenLeagueFocused = source.HoldToTopOnlyWhenLeagueFocused,
            QuickMessageClipboardEnabled = source.QuickMessageClipboardEnabled,
            QuickMessageAutoSendEnabled = source.QuickMessageAutoSendEnabled,
            QuickMessageSendIntervalSeconds = source.QuickMessageSendIntervalSeconds,
            QuickMessageLanguage = source.QuickMessageLanguage,
            QuickMessageText = source.QuickMessageText,
            QuickMessageHotkey = source.QuickMessageHotkey,
            AutoMatch = source.AutoMatch,
            AutoAccept = source.AutoAccept,
            AutoBan = source.AutoBan,
            AutoPick = source.AutoPick,
            BanChampions = source.BanChampions.ToList(),
            PickChampions = source.PickChampions.ToList(),
            CheckIntervalSeconds = source.CheckIntervalSeconds,
            Resolution = source.Resolution,
            MinimizeToTray = source.MinimizeToTray,
            AutoRefresh = source.AutoRefresh,
            AutoRefreshIntervalSeconds = source.AutoRefreshIntervalSeconds,
            NotifyOnGameEnd = source.NotifyOnGameEnd,
            LaunchOnStartup = source.LaunchOnStartup,
            Ai = ToDomain(source.Ai)
        };
        settings.Normalize();
        return settings;
    }

    public static SettingConfig ToLegacy(AssistantSettings source)
    {
        source.Normalize();
        var settings = new SettingConfig
        {
            ThemeMode = source.ThemeMode,
            AutoLaunchGameClient = source.AutoLaunchGameClient,
            GameClientPath = source.GameClientPath,
            WindowOpacityPercent = source.WindowOpacityPercent,
            HoldToTopHotkey = source.HoldToTopHotkey,
            HoldToTopOnlyWhenLeagueFocused = source.HoldToTopOnlyWhenLeagueFocused,
            QuickMessageClipboardEnabled = source.QuickMessageClipboardEnabled,
            QuickMessageAutoSendEnabled = source.QuickMessageAutoSendEnabled,
            QuickMessageSendIntervalSeconds = source.QuickMessageSendIntervalSeconds,
            QuickMessageLanguage = source.QuickMessageLanguage,
            QuickMessageText = source.QuickMessageText,
            QuickMessageHotkey = source.QuickMessageHotkey,
            AutoMatch = source.AutoMatch,
            AutoAccept = source.AutoAccept,
            AutoBan = source.AutoBan,
            AutoPick = source.AutoPick,
            BanChampions = source.BanChampions.ToList(),
            PickChampions = source.PickChampions.ToList(),
            CheckIntervalSeconds = source.CheckIntervalSeconds,
            Resolution = source.Resolution,
            MinimizeToTray = source.MinimizeToTray,
            AutoRefresh = source.AutoRefresh,
            AutoRefreshIntervalSeconds = source.AutoRefreshIntervalSeconds,
            NotifyOnGameEnd = source.NotifyOnGameEnd,
            LaunchOnStartup = source.LaunchOnStartup,
            Ai = ToLegacy(source.Ai)
        };
        settings.Normalize();
        return settings;
    }

    private static CloudAiSettings ToDomain(AiSettings source) => new()
    {
        RecommendationEnabled = source.RecommendationEnabled,
        Enabled = source.Enabled,
        Provider = (LOL_GameAssistant.Domain.Settings.AiProvider)(int)source.Provider,
        Model = source.Model,
        BaseUrl = source.BaseUrl,
        EncryptedApiKey = source.EncryptedApiKey,
        DynamicRefreshEnabled = source.DynamicRefreshEnabled,
        DynamicRefreshSeconds = source.DynamicRefreshSeconds,
        ShowRecommendationPopup = source.ShowRecommendationPopup,
        RecommendationOverlayEnabled = source.RecommendationOverlayEnabled,
        RecommendationOverlayPosition = source.RecommendationOverlayPosition,
        RecommendationOverlayOffsetX = source.RecommendationOverlayOffsetX,
        RecommendationOverlayOffsetY = source.RecommendationOverlayOffsetY,
        RecommendationOverlayDurationSeconds = source.RecommendationOverlayDurationSeconds,
        AnakinEnabled = source.AnakinEnabled,
        AnakinEncryptedApiKey = source.AnakinEncryptedApiKey
    };

    private static AiSettings ToLegacy(CloudAiSettings source) => new()
    {
        RecommendationEnabled = source.RecommendationEnabled,
        Enabled = source.Enabled,
        Provider = (Entity.AiProvider)(int)source.Provider,
        Model = source.Model,
        BaseUrl = source.BaseUrl,
        EncryptedApiKey = source.EncryptedApiKey,
        DynamicRefreshEnabled = source.DynamicRefreshEnabled,
        DynamicRefreshSeconds = source.DynamicRefreshSeconds,
        ShowRecommendationPopup = source.ShowRecommendationPopup,
        RecommendationOverlayEnabled = source.RecommendationOverlayEnabled,
        RecommendationOverlayPosition = source.RecommendationOverlayPosition,
        RecommendationOverlayOffsetX = source.RecommendationOverlayOffsetX,
        RecommendationOverlayOffsetY = source.RecommendationOverlayOffsetY,
        RecommendationOverlayDurationSeconds = source.RecommendationOverlayDurationSeconds,
        AnakinEnabled = source.AnakinEnabled,
        AnakinEncryptedApiKey = source.AnakinEncryptedApiKey
    };
}