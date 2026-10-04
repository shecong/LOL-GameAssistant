using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CoachThemeTests
{
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
