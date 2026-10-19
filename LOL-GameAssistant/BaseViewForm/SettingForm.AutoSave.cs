using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    private void BindBooleanAutoSave()
    {
        void Switch(AntdUI.Switch control, Action<AssistantSettings, bool> update) =>
            control.CheckedChanged += (_, _) => PersistBoolean(control, () => control.Checked, value => control.Checked = value, update);
        void Check(AntdUI.Checkbox control, Action<AssistantSettings, bool> update) =>
            control.CheckedChanged += (_, _) => PersistBoolean(control, () => control.Checked, value => control.Checked = value, update);

        Switch(swi_open, (s, v) => s.AutoMatch = v);
        Switch(swi_gametrue, (s, v) => s.AutoAccept = v);
        Switch(swi_jyyx, (s, v) => s.AutoBan = v);
        Switch(swi_xyx, (s, v) => s.AutoPick = v);
        Switch(swi_tray, (s, v) => s.MinimizeToTray = v);
        Switch(swi_auto_refresh, (s, v) => s.AutoRefresh = v);
        Switch(swi_notify_end, (s, v) => s.NotifyOnGameEnd = v);
        Switch(swi_startup, (s, v) => s.LaunchOnStartup = v);
        Switch(_preselectOnly, (s, v) => s.AutoPickPreselectOnly = v);
        Switch(_skipFill, (s, v) => s.SkipAutoPickOnFill = v);
        Switch(_autoHonor, (s, v) => s.AutoHonor = v);
        _honorStrategy.SelectedIndexChanged += (_, _) => PersistValue(s => s.HonorStrategy = _honorStrategy.SelectedIndex switch { 1 => "friends", 2 => "manual", _ => "random" });
        Switch(_autoReturn, (s, v) => s.AutoReturnToLobby = v);
        Switch(_returnAndSearch, (s, v) => s.AutoReturnStartMatchmaking = v);
        Check(_autoSwapAramBench, (s, v) => s.AutoSwapAramBench = v);
        Check(_autoLaunchClient, (s, v) => s.AutoLaunchGameClient = v);
        Check(_onlyLeagueFocused, (s, v) => s.HoldToTopOnlyWhenLeagueFocused = v);
        Check(_battleOverlayEnabled, (s, v) => s.BattleOverlayEnabled = v);
        Check(_recommendationEnabled, (s, v) => s.Ai.RecommendationEnabled = v);
        Check(_opggBuildAssistantEnabled, (s, v) => s.OpggBuildAssistantEnabled = v);
        Check(_autoApplyRuneBuild, (s, v) => s.AutoApplyRuneBuild = v);
        Check(_mayhemOverlayEnabled, (s, v) => s.MayhemOverlayEnabled = v);
        Check(_champSelectCompanionEnabled, (s, v) => s.ChampSelectCompanionEnabled = v);
        Check(_champSelectKdaAnnouncementEnabled, (s, v) => s.ChampSelectKdaAnnouncementEnabled = v);
        Check(_gameKdaAnnouncementEnabled, (s, v) => s.GameKdaAnnouncementEnabled = v);
        Check(_gameKdaOnePlayerPerLine, (s, v) => s.GameKdaOnePlayerPerLine = v);
        _ratingMinimumGames.ValueChanged += (_, _) => PersistValue(s => s.RecentPerformanceMinimumGames = (int)_ratingMinimumGames.Value);
        Check(_dynamicRefresh, (s, v) => s.Ai.DynamicRefreshEnabled = v);
        Check(_showPopup, (s, v) => s.Ai.ShowRecommendationPopup = v);
        Check(_overlayEnabled, (s, v) => s.Ai.RecommendationOverlayEnabled = v);
        _clientPath.Leave += (_, _) => PersistValue(s => s.GameClientPath = _gameClientLauncher.NormalizeConfiguredDirectory(_clientPath.Text));
        _champSelectKdaAnnouncementTemplate.Leave += (_, _) => PersistValue(s => s.ChampSelectKdaAnnouncementTemplate = _champSelectKdaAnnouncementTemplate.Text);
        _opacity.ValueChanged += (_, _) => PersistValue(s => s.WindowOpacityPercent = (int)_opacity.Value);
        _themeMode.SelectedIndexChanged += (_, _) => PersistValue(s => s.ThemeMode = _themeMode.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" });
        _languageMode.SelectedIndexChanged += (_, _) => SwitchLanguage();
        _dynamicSeconds.ValueChanged += (_, _) => PersistValue(s => s.Ai.DynamicRefreshSeconds = (int)_dynamicSeconds.Value);
        _overlayDuration.ValueChanged += (_, _) => PersistValue(s => s.Ai.RecommendationOverlayDurationSeconds = (int)_overlayDuration.Value);
        _overlayOffsetX.ValueChanged += (_, _) => PersistValue(s => s.Ai.RecommendationOverlayOffsetX = (int)_overlayOffsetX.Value);
        _overlayOffsetY.ValueChanged += (_, _) => PersistValue(s => s.Ai.RecommendationOverlayOffsetY = (int)_overlayOffsetY.Value);
        _overlayPosition.SelectedIndexChanged += (_, _) => PersistValue(s => s.Ai.RecommendationOverlayPosition = IndexToOverlayPosition(_overlayPosition.SelectedIndex));
    }

    private bool PersistValue(Action<AssistantSettings> update)
    {
        if (_isLoading || IsDisposed) return false;
        try
        {
            var latest = _settingsStore.Load();
            update(latest);
            if (WindowHoldController.HasGameHotkeyConflict(latest) || WindowHoldController.HasBattleOverlayHotkeyConflict(latest))
                throw new InvalidOperationException("快捷键冲突，请更换按键。");
            latest.Normalize();
            _settingsStore.Save(latest);
            _config = latest;
            ApplySideEffects(latest, applyStartup: false);
            Program.GameMain?.ConfigureQuickShoutHotkeys(latest);
            label_cache_status.Text = "设置已自动保存到本机";
            return true;
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.WriteException(ex);
            label_cache_status.Text = $"保存失败：{ex.Message}";
            if ((FindForm() ?? Program.GameMain) is { } owner) UiMessage.error(owner, label_cache_status.Text);
            return false;
        }
    }

    private void PersistBoolean(Control control, Func<bool> read, Action<bool> restore,
        Action<AssistantSettings, bool> update)
    {
        if (_isLoading || IsDisposed) return;
        bool value = read();
        bool saved = false;
        try
        {
            // 只更新当前开关；未提交的连接配置、词库或其他页面的新设置不能被覆盖。
            AssistantSettings latest = _settingsStore.Load();
            update(latest, value);
            if (control == _battleOverlayEnabled && WindowHoldController.HasBattleOverlayHotkeyConflict(latest))
                throw new InvalidOperationException("对局面板快捷键与喊话或 KDA 快捷键冲突，请先更换快捷键。");
            _settingsStore.Save(latest);
            saved = true;
            _config = latest;
            ApplySideEffects(latest, applyStartup: control == swi_startup);
            Program.GameMain?.RefreshMayhemOverlaySetting();
            Program.GameMain?.RefreshChampSelectCompanionSetting();
            label_cache_status.Text = "开关已自动保存到本机";
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.WriteException(ex);
            if (!saved)
            {
                _isLoading = true;
                try { restore(!value); }
                finally { _isLoading = false; }
            }
            label_cache_status.Text = saved ? $"设置已保存，应用失败：{ex.Message}" : $"保存失败：{ex.Message}";
            if ((FindForm() ?? Program.GameMain) is { } owner) UiMessage.error(owner, label_cache_status.Text);
        }
    }
}
