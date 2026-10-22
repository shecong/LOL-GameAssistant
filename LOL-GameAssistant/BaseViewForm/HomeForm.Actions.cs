using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class HomeForm
{
    private void InitializeQuickActions()
    {
        var actions = new FlowLayoutPanel { Name = "homeQuickActions", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8, 3, 8, 3) };
        void Add(string text, string name, Func<Task> action)
        {
            var button = new AntdUI.Button { Text = text, Name = name, AutoSize = true };
            button.Click += async (_, _) =>
            {
                if (button.Loading) return; button.Loading = true;
                try { await action(); }
                catch (Exception ex) { if (!IsDisposed) UiMessage.error(FindForm()!, ex.Message); }
                finally { if (!button.IsDisposed) button.Loading = false; }
            };
            actions.Controls.Add(button);
        }
        Add("启动 LOL", "quickLaunch", async () =>
        {
            var settings = AppCompositionRoot.ApplicationSettingsStore.Load();
            var result = await AppCompositionRoot.GameClientLauncher.StartAndVerifyAsync(settings.GameClientPath, _loads.Token);
            if (!IsDisposed) UiMessage.info(FindForm()!, result.Message);
        });
        Add("配置当前英雄", "quickBuild", async () => { if (Program.GameMain is { } main) { main.ShowCoachPage(); await main.coachForm.ApplyCurrentBuildAsync(); } });
        Add("上一局 / 成长", "quickReview", async () =>
        {
            if (Program.GameMain is not { } main || string.IsNullOrWhiteSpace(userinfo?.Puuid)) return;
            main.ShowBattleQueryPage(); await main.battleQueryForm.OpenGrowthAsync(userinfo.Puuid, false);
        });
        Add("英雄池", "quickPool", async () =>
        {
            if (Program.GameMain is not { } main || string.IsNullOrWhiteSpace(userinfo?.Puuid)) return;
            main.ShowBattleQueryPage(); await main.battleQueryForm.OpenGrowthAsync(userinfo.Puuid, true);
        });
        Add("找队友", "quickLounge", () => { Program.GameMain?.ShowLoungePage(); return Task.CompletedTask; });
        Add("辅助预设", "quickPresets", () => { Program.GameMain?.ShowSettingsPage(); return Task.CompletedTask; });
        Controls.Add(actions);
    }
}
