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
            QuickMessageSendIntervalSeconds = source.QuickMessageSendIntervalSeconds,
            QuickMessageCustomPhrases = source.QuickMessageCustomPhrases,
            QuickShoutPerCharacter = source.QuickShoutPerCharacter,
            QuickShoutSendToAll = source.QuickShoutSendToAll,
            QuickShoutUseClipboard = source.QuickShoutUseClipboard,
            QuickShoutHotkeysEnabled = source.QuickShoutHotkeysEnabled,
            QuickShoutBuiltInHotkey = source.QuickShoutBuiltInHotkey,
            QuickShoutCustomHotkey = source.QuickShoutCustomHotkey,
            QuickShoutBatchHotkey = source.QuickShoutBatchHotkey,
            QuickShoutMultiSelectEnabled = source.QuickShoutMultiSelectEnabled,
            QuickShoutSelectedPhrases = source.QuickShoutSelectedPhrases.ToList(),
            OpggBuildAssistantEnabled = source.OpggBuildAssistantEnabled,
            ChampSelectKdaAnnouncementEnabled = source.ChampSelectKdaAnnouncementEnabled,
            GameKdaAnnouncementEnabled = source.GameKdaAnnouncementEnabled,
            GameKdaOnePlayerPerLine = source.GameKdaOnePlayerPerLine,
            ChampSelectKdaAnnouncementTemplate = source.ChampSelectKdaAnnouncementTemplate,
            AutoMatch = source.AutoMatch,
            AutoAccept = source.AutoAccept,
            AutoAcceptDelayMinMilliseconds = source.AutoAcceptDelayMinMilliseconds,
            AutoAcceptDelayMaxMilliseconds = source.AutoAcceptDelayMaxMilliseconds,
            AutoBan = source.AutoBan,
            AutoPick = source.AutoPick,
            AutoPickPreselectOnly = source.AutoPickPreselectOnly,
            SkipAutoPickOnFill = source.SkipAutoPickOnFill,
            BanChampions = source.BanChampions.ToList(),
            PickChampions = source.PickChampions.ToList(),
            CheckIntervalSeconds = source.CheckIntervalSeconds,
            Resolution = source.Resolution,
            MinimizeToTray = source.MinimizeToTray,
            AutoRefresh = source.AutoRefresh,
            AutoRefreshIntervalSeconds = source.AutoRefreshIntervalSeconds,
            NotifyOnGameEnd = source.NotifyOnGameEnd,
            AutoReturnToLobby = source.AutoReturnToLobby,
            AutoReturnStartMatchmaking = source.AutoReturnStartMatchmaking,
            AutoHonor = source.AutoHonor,
            QuickLobbyQueueId = source.QuickLobbyQueueId,
            OpggManualBuildSelections = new Dictionary<string, int>(source.OpggManualBuildSelections),
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
            QuickMessageSendIntervalSeconds = source.QuickMessageSendIntervalSeconds,
            QuickMessageCustomPhrases = source.QuickMessageCustomPhrases,
            QuickShoutPerCharacter = source.QuickShoutPerCharacter,
            QuickShoutSendToAll = source.QuickShoutSendToAll,
            QuickShoutUseClipboard = source.QuickShoutUseClipboard,
            QuickShoutHotkeysEnabled = source.QuickShoutHotkeysEnabled,
            QuickShoutBuiltInHotkey = source.QuickShoutBuiltInHotkey,
            QuickShoutCustomHotkey = source.QuickShoutCustomHotkey,
            QuickShoutBatchHotkey = source.QuickShoutBatchHotkey,
            QuickShoutMultiSelectEnabled = source.QuickShoutMultiSelectEnabled,
            QuickShoutSelectedPhrases = source.QuickShoutSelectedPhrases.ToList(),
            OpggBuildAssistantEnabled = source.OpggBuildAssistantEnabled,
            ChampSelectKdaAnnouncementEnabled = source.ChampSelectKdaAnnouncementEnabled,
            GameKdaAnnouncementEnabled = source.GameKdaAnnouncementEnabled,
            GameKdaOnePlayerPerLine = source.GameKdaOnePlayerPerLine,
            ChampSelectKdaAnnouncementTemplate = source.ChampSelectKdaAnnouncementTemplate,
            AutoMatch = source.AutoMatch,
            AutoAccept = source.AutoAccept,
            AutoAcceptDelayMinMilliseconds = source.AutoAcceptDelayMinMilliseconds,
            AutoAcceptDelayMaxMilliseconds = source.AutoAcceptDelayMaxMilliseconds,
            AutoBan = source.AutoBan,
            AutoPick = source.AutoPick,
            AutoPickPreselectOnly = source.AutoPickPreselectOnly,
            SkipAutoPickOnFill = source.SkipAutoPickOnFill,
            BanChampions = source.BanChampions.ToList(),
            PickChampions = source.PickChampions.ToList(),
            CheckIntervalSeconds = source.CheckIntervalSeconds,
            Resolution = source.Resolution,
            MinimizeToTray = source.MinimizeToTray,
            AutoRefresh = source.AutoRefresh,
            AutoRefreshIntervalSeconds = source.AutoRefreshIntervalSeconds,
            NotifyOnGameEnd = source.NotifyOnGameEnd,
            AutoReturnToLobby = source.AutoReturnToLobby,
            AutoReturnStartMatchmaking = source.AutoReturnStartMatchmaking,
            AutoHonor = source.AutoHonor,
            QuickLobbyQueueId = source.QuickLobbyQueueId,
            OpggManualBuildSelections = new Dictionary<string, int>(source.OpggManualBuildSelections),
            LaunchOnStartup = source.LaunchOnStartup,
            Ai = ToLegacy(source.Ai)
        };
        settings.Normalize();
        return settings;
    }

    private static CloudAiSettings ToDomain(AiSettings source) => new()
    {
        RecommendationEnabled = source.RecommendationEnabled,
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
        RecommendationOverlayDurationSeconds = source.RecommendationOverlayDurationSeconds
    };

    private static AiSettings ToLegacy(CloudAiSettings source) => new()
    {
        RecommendationEnabled = source.RecommendationEnabled,
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
        RecommendationOverlayDurationSeconds = source.RecommendationOverlayDurationSeconds
    };
}
