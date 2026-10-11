using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class CoachThemeTests
{
    [Fact]
    public void ManualClickWaitsForAutomaticCollectionAndCancellationDoesNotReleaseItsLock() => MatchListScrollingTests.OnUiThread(() =>
    {
        int collections = 0;
        var pending = new TaskCompletionSource<AiGameContext>();
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => { collections++; return pending.Task; }),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => new AssistantSettings { OpggBuildAssistantEnabled = true }),
            Service<IOpggBuildApplyService>((_, _) => throw new InvalidOperationException("No builds before context arrives.")));
        Task automatic = form.PromptOpggBuildIfNeededAsync();
        using var cancellation = new CancellationTokenSource();
        Task<string> manual = form.OpenOpggBuildPickerAsync(cancellation.Token);
        Assert.False(manual.IsCompleted);
        Assert.Equal(1, collections);
        Assert.True(form.PromptOpggBuildIfNeededAsync().IsCompleted);
        cancellation.Cancel();
        PumpUntil(() => manual.IsCompleted);
        Assert.Equal("已取消 OP.GG 方案选择。", manual.GetAwaiter().GetResult());
        Assert.True(form.PromptOpggBuildIfNeededAsync().IsCompleted);
        Assert.Equal(1, collections);
        pending.SetResult(new AiGameContext());
        PumpUntil(() => automatic.IsCompleted);
        automatic.GetAwaiter().GetResult();
    });

    [Theory]
    [InlineData("CLASSIC", "未知位置")]
    [InlineData("UNSUPPORTED", "TOP")]
    public void IncompleteContextDoesNotMarkTheChampionAsPrompted(string mode, string role) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(new AiGameContext
                { Phase = "ChampSelect", MyChampionId = 122, GameMode = mode, MyRole = role })),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => new AssistantSettings { OpggBuildAssistantEnabled = true }),
            Service<IOpggBuildApplyService>((_, _) => throw new InvalidOperationException("Wait for complete context.")));
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        Assert.Equal("", typeof(CoachForm).GetField("_opggPromptedContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form));
    });

    [Fact]
    public void SavedChoiceStillOpensPickerWhenAutoApplyIsOffAndManualClickReusesIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        int collections = 0;
        var pendingContext = new TaskCompletionSource<AiGameContext>();
        var settings = new AssistantSettings { OpggBuildAssistantEnabled = true, AutoApplyRuneBuild = false };
        settings.OpggManualBuildSelections["122:aram:none"] = 2;
        var options = new[] { 1, 2 }.Select(order => new OpggBuildOption(order, [1054], [3078, 3053, 6333], [],
            8000, 8200, [8010, 9111, 9104, 8299, 8234, 8236], 100, 50, Mode: "aram")).ToArray();
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) =>
            {
                collections++;
                return pendingContext.Task;
            }),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => settings),
            Service<IOpggBuildApplyService>((method, _) => method.Name == "GetBuildChoicesAsync"
                ? Task.FromResult(new OpggBuildChoices(true, "", "德莱厄斯", "大乱斗", options, Mode: "aram", ChampionId: 122))
                : throw new InvalidOperationException("Browsing must not apply a saved choice.")),
            Service<IGameAssetService>((method, _) => method.Name.Contains("Icon")
                ? Task.FromResult<GameAsset?>(null) : (object)Task.FromResult<string?>(null)));
        bool opened = false;
        Task automatic = form.PromptOpggBuildIfNeededAsync();
        Task<string> queuedManual = form.OpenOpggBuildPickerAsync();
        Assert.False(queuedManual.IsCompleted);
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var picker = System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().SingleOrDefault();
            if (picker == null || !queuedManual.IsCompleted) return;
            timer.Stop();
            try
            {
                opened = true;
                Assert.Equal(2, picker.SelectedOption?.Order);
                Assert.Equal("OP.GG 方案窗口已打开，请选择方案。", queuedManual.GetAwaiter().GetResult());
                Assert.Equal("OP.GG 方案窗口已打开，请选择方案。", form.OpenOpggBuildPickerAsync().GetAwaiter().GetResult());
                Assert.Equal(1, collections);
            }
            finally { picker.Close(); }
        };
        timer.Start();
        pendingContext.SetResult(new AiGameContext { Phase = "ChampSelect", MyChampionId = 122, GameMode = "ARAM" });
        PumpUntil(() => automatic.IsCompleted);
        automatic.GetAwaiter().GetResult();
        Assert.True(opened);
    });

    [Fact]
    public void ManualPickerWritesSelectedBuildExactlyOnceAndRemembersSuccessfulChoice() => MatchListScrollingTests.OnUiThread(() =>
    {
        int applications = 0;
        int saves = 0;
        var settings = new AssistantSettings();
        var options = new[] { 1, 2 }.Select(order => new OpggBuildOption(order, [1054], [3078, 3053, 6333], [],
            8000, 8200, [8010, 9111, 9104, 8299, 8234, 8236], 100, 50, Mode: "aram")).ToArray();
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(new AiGameContext
                { Phase = "ChampSelect", MyChampionId = 122, GameMode = "ARAM" })),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((method, _) =>
            {
                if (method.Name == "Save") { saves++; return null; }
                return settings;
            }),
            Service<IOpggBuildApplyService>((method, args) =>
            {
                if (method.Name == "GetBuildChoicesAsync")
                    return Task.FromResult(new OpggBuildChoices(true, "", "德莱厄斯", "大乱斗", options, Mode: "aram", ChampionId: 122));
                Assert.Equal("ApplyBuildAsync", method.Name);
                Assert.Equal(2, ((OpggBuildOption)args[2]!).Order);
                Assert.True((bool)args[4]!);
                Assert.True((bool)args[5]!);
                applications++;
                return Task.FromResult(OpggBuildApplyResult.Success("已配置方案 2"));
            }),
            Service<IGameAssetService>((method, _) => method.Name.Contains("Icon")
                ? Task.FromResult<GameAsset?>(null) : (object)Task.FromResult<string?>(null)));
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var picker = System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().SingleOrDefault();
            if (picker == null) return;
            timer.Stop();
            try
            {
                var row = picker.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>()
                    .Single(button => button.Name == "applyRoute2");
                row.PerformClick();
            }
            finally { if (picker.Visible) picker.Close(); }
        };
        timer.Start();
        Assert.Equal("已配置方案 2", form.OpenOpggBuildPickerAsync().GetAwaiter().GetResult());
        Assert.Equal(1, applications);
        Assert.Equal(1, saves);
        Assert.Equal(2, settings.OpggManualBuildSelections["122:aram:none"]);
    });

    private static IEnumerable<Control> Descendants(Control parent) =>
        new[] { parent }.Concat(parent.Controls.Cast<Control>().SelectMany(Descendants));

    private static void PumpUntil(Func<bool> complete)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!complete() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert.True(complete());
    }

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
