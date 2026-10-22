namespace LOL_GameAssistant.Domain.Settings;

public enum AssistantPreset { Ranked, Aram, Quiet }
public static class AssistantPresetPolicy
{
    public static void Apply(AssistantSettings settings, AssistantPreset preset)
    {
        bool enabled = preset != AssistantPreset.Quiet;
        settings.AutoMatch = settings.AutoPick = settings.AutoBan = false;
        settings.AutoReturnStartMatchmaking = settings.AutoReturnToLobby = settings.AutoHonor = false;
        settings.AutoAccept = enabled;
        settings.AutoSwapAramBench = preset == AssistantPreset.Aram && settings.AramBenchPriorityChampions.Count > 0;
        settings.OpggBuildAssistantEnabled = enabled;
        settings.AutoApplyRuneBuild = false;
        settings.ChampSelectCompanionEnabled = settings.BattleOverlayEnabled = enabled;
        settings.MayhemOverlayEnabled = preset == AssistantPreset.Aram;
        settings.ChampSelectKdaAnnouncementEnabled = settings.GameKdaAnnouncementEnabled = false;
        settings.Ai.RecommendationEnabled = false;
        settings.Ai.RecommendationOverlayEnabled = settings.Ai.ShowRecommendationPopup = false;
        if (!enabled)
        {
            settings.QuickShoutHotkeysEnabled = settings.GameKdaHotkeyEnabled = settings.GameKdaPlayerHotkeysEnabled = false;
            settings.AutoRefresh = false;
        }
        settings.Normalize();
    }
}
