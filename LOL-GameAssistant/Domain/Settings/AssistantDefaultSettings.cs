namespace LOL_GameAssistant.Domain.Settings;

/// <summary>以本机确认的开关组合为新配置预设；不包含账号、密钥、路径或个人英雄/皮肤记录。</summary>
public static class AssistantDefaultSettings
{
    public static AssistantSettings Create() => new()
    {
        AutoLaunchGameClient = true,
        TclsAutoLoginEnabled = true,
        SkinCoreEnabled = true,
        SkinCommunitySortEnabled = true,
        RankBorderAutoApplyEnabled = false,
        UnlimitedZoomEnabled = false,
        SkinHotkeysEnabled = true,
        BattleOverlayEnabled = true,
        HoldToTopOnlyWhenLeagueFocused = true,
        QuickShoutPerCharacter = false,
        QuickShoutSendToAll = true,
        QuickShoutUseClipboard = false,
        QuickShoutHotkeysEnabled = true,
        QuickShoutMultiSelectEnabled = true,
        OpggBuildAssistantEnabled = true,
        ChampSelectKdaAnnouncementEnabled = false,
        GameKdaAnnouncementEnabled = false,
        GameKdaHotkeyEnabled = true,
        GameKdaPlayerHotkeysEnabled = true,
        GameKdaOnePlayerPerLine = false,
        Ai = new()
        {
            RecommendationEnabled = false,
            DynamicRefreshEnabled = true,
            StructuredOutputEnabled = true,
            ShowRecommendationPopup = false,
            RecommendationOverlayEnabled = false
        },
        AutoMatch = false,
        AutoAccept = true,
        AutoBan = false,
        AutoPick = true,
        AutoPickPreselectOnly = true,
        AutoSwapAramBench = false,
        SkipAutoPickOnFill = true,
        MinimizeToTray = false,
        AutoRefresh = false,
        NotifyOnGameEnd = false,
        AutoReturnToLobby = false,
        AutoReturnStartMatchmaking = false,
        AutoHonor = false,
        AutoApplyRuneBuild = true,
        MayhemOverlayEnabled = true,
        ChampSelectCompanionEnabled = true,
        EnemyJungleRouteEnabled = true,
        LaunchOnStartup = false
    };
}
