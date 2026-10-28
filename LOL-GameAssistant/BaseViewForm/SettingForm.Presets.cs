using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    private Control CreatePresetBar()
    {
        var bar = new FlowLayoutPanel { Name = "assistantPresetBar", AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
        bar.Controls.Add(new AntdUI.Label { Text = "辅助预设", AutoSize = true, Padding = new Padding(0, 8, 8, 0) });
        foreach (var (preset, name) in new[] { (AssistantPreset.Ranked, "排位"), (AssistantPreset.Aram, "大乱斗"), (AssistantPreset.Quiet, "纯净辅助") })
        {
            var button = new AntdUI.Button { Text = name, Name = "preset" + preset, AutoSize = true };
            button.Click += (_, _) => ApplyPreset(preset);
            AttachTip(button, "立即保存辅助开关；关闭自动选禁、自动发送战绩和云端 AI 提醒。保留个人方案、密钥、换肤设置及按键绑定。");
            bar.Controls.Add(button);
        }
        return bar;
    }
    internal void ApplyPreset(AssistantPreset preset)
    {
        try
        {
            var latest = _settingsStore.Load(); AssistantPresetPolicy.Apply(latest, preset); _settingsStore.Save(latest);
            bool loading = _isLoading; _isLoading = true;
            try
            {
                _config = latest;
                swi_open.Checked = latest.AutoMatch; swi_gametrue.Checked = latest.AutoAccept;
                swi_xyx.Checked = latest.AutoPick; swi_jyyx.Checked = latest.AutoBan;
                _autoReturn.Checked = latest.AutoReturnToLobby; _returnAndSearch.Checked = latest.AutoReturnStartMatchmaking;
                _autoHonor.Checked = latest.AutoHonor; _autoSwapAramBench.Checked = latest.AutoSwapAramBench;
                _opggBuildAssistantEnabled.Checked = latest.OpggBuildAssistantEnabled; _autoApplyRuneBuild.Checked = latest.AutoApplyRuneBuild;
                _champSelectCompanionEnabled.Checked = latest.ChampSelectCompanionEnabled; _battleOverlayEnabled.Checked = latest.BattleOverlayEnabled;
                _mayhemOverlayEnabled.Checked = latest.MayhemOverlayEnabled;
                _enemyJungleRouteEnabled.Checked = latest.EnemyJungleRouteEnabled;
                _champSelectKdaAnnouncementEnabled.Checked = latest.ChampSelectKdaAnnouncementEnabled;
                _gameKdaAnnouncementEnabled.Checked = latest.GameKdaAnnouncementEnabled;
                _recommendationEnabled.Checked = latest.Ai.RecommendationEnabled;
                _overlayEnabled.Checked = latest.Ai.RecommendationOverlayEnabled; _showPopup.Checked = latest.Ai.ShowRecommendationPopup;
                swi_auto_refresh.Checked = latest.AutoRefresh;
            }
            finally { _isLoading = loading; }
            ApplySideEffects(latest, applyStartup: false);
            Program.GameMain?.ConfigureQuickShoutHotkeys(latest);
            Program.GameMain?.RefreshMayhemOverlaySetting(); Program.GameMain?.RefreshChampSelectCompanionSetting();
            label_cache_status.Text = "辅助预设已保存；保留个人方案、密钥和换肤设置。";
        }
        catch (Exception ex) { label_cache_status.Text = "预设保存失败：" + ex.Message; }
    }
}
