using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CoachThemeTests
{
    [Fact]
    public void AutomaticRunesApplyOnceUntilChampionChangesOrSelectionResets() => MatchListScrollingTests.OnUiThread(() =>
    {
        var context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1, GameMode = "ARAM", QueueId = 450 };
        var settings = new AssistantSettings
        {
            OpggBuildAssistantEnabled = false,
            AutoApplyRuneBuild = true,
            PersonalRunePresets = new[] { 1, 2 }.Select(id => new PersonalRunePreset
            {
                ChampionId = id, Mode = "aram", AutoApply = true,
                PrimaryStyleId = 8000, SubStyleId = 8100,
                RunePerkIds = new() { 8005, 8009, 9104, 8014, 8139, 8135 },
                SummonerSpellIds = new() { 4, 14 }
            }).ToList()
        };
        int applications = 0;
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(context)),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => settings),
            Service<IOpggBuildApplyService>((method, _) =>
            {
                Assert.Equal("ApplyPersonalRunePresetAsync", method.Name);
                applications++;
                return Task.FromResult(OpggBuildApplyResult.Success("Applied"));
            }));

        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        for (int i = 0; i < 3; i++)
        {
            form.RefreshOpggAvailability();
            form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        }
        Assert.Equal(1, applications);

        // 大乱斗的分路字段变化不代表用户更换英雄或方案。
        context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1, GameMode = "ARAM", QueueId = 450, MyRole = "TOP" };
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        Assert.Equal(1, applications);

        context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 2, GameMode = "ARAM", QueueId = 450, MyRole = "TOP" };
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        Assert.Equal(2, applications);

        form.ResetOpggChampSelectPrompt();
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        Assert.Equal(3, applications);
    });

    [Fact]
    public void ContentDrawingColorsFollowDarkLightAndDarkAgain() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new CoachForm(Service<IAiCoachingService>((_, _) => null),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => new AssistantSettings()),
            Service<IOpggBuildApplyService>((_, _) => null));
        string originalMode = UiTheme.Mode;
        try
        {
            foreach (string mode in new[] { "Dark", "Light", "Dark" })
            {
                UiTheme.SetMode(mode);
                UiTheme.Apply(form);
                ThemePalette palette = UiTheme.Palette;
                foreach (string name in new[] { "_validation", "_recommendation" })
                {
                    var input = (AntdUI.Input)typeof(CoachForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                    Assert.Equal(palette.SurfaceRaised, input.BackColor);
                    Assert.Equal(palette.TextPrimary, input.ForeColor);
                    Assert.Equal(palette.Border, input.BorderColor);
                    Assert.Equal(mode == "Dark" ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light, input.ColorScheme);
                }
            }
        }
        finally { UiTheme.SetMode(originalMode); }
    });

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
