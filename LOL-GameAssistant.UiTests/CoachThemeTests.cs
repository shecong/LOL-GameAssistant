using System.Reflection;
using Button = AntdUI.Button;
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
    public void AutomaticRecommendationPreloadsWithoutPopupAndManualClickOpensSavedChoice() => MatchListScrollingTests.OnUiThread(() =>
    {
        int collections = 0;
        var pendingContext = new TaskCompletionSource<AiGameContext>();
        var settings = new AssistantSettings { OpggBuildAssistantEnabled = true, AutoApplyRuneBuild = false };
        settings.OpggManualBuildSelections["122:aram:none"] = 2;
        var options = new[] { 1, 2 }.Select(order => new OpggBuildOption(order, [1054], [3078, 3053, 6333 + order], [],
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
        Task automatic = form.PromptOpggBuildIfNeededAsync();
        pendingContext.SetResult(new AiGameContext { Phase = "ChampSelect", MyChampionId = 122, GameMode = "ARAM" });
        PumpUntil(() => automatic.IsCompleted);
        automatic.GetAwaiter().GetResult();
        Assert.Empty(System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>());
        Assert.Equal(1, collections);
        Task<string> manual = form.OpenOpggBuildPickerAsync();
        PumpUntil(() => System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().Any());
        var picker = System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().Single();
        Assert.Equal(2, picker.SelectedOption?.Order);
        Assert.Equal("OP.GG 方案窗口已打开，请选择方案。", form.OpenOpggBuildPickerAsync().GetAwaiter().GetResult());
        Assert.Equal(2, collections);
        picker.Close();
        PumpUntil(() => manual.IsCompleted);
        Assert.Equal("已取消 OP.GG 出装配置。", manual.GetAwaiter().GetResult());
    });

    [Fact]
    public void ManualPickerWritesSelectedBuildExactlyOnceAndRemembersSuccessfulChoice() => MatchListScrollingTests.OnUiThread(() =>
    {
        int applications = 0;
        int saves = 0;
        var settings = new AssistantSettings();
        var options = new[] { 1, 2 }.Select(order => new OpggBuildOption(order, [1054], [3078, 3053, 6333 + order], [],
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
        Task<string> opened = form.OpenOpggBuildPickerAsync();
        PumpUntil(() => opened.IsCompleted);
        Assert.Equal("已配置方案 2", opened.GetAwaiter().GetResult());
        Assert.Equal(1, applications);
        Assert.Equal(1, saves);
        Assert.Equal(2, settings.OpggManualBuildSelections["122:aram:none"]);
    });

    private static IEnumerable<Control> Descendants(Control parent) =>
        new[] { parent }.Concat(parent.Controls.Cast<Control>().SelectMany(Descendants));

    [Theory]
    [InlineData(false, false, "CLASSIC", 420)]
    [InlineData(true, false, "CLASSIC", 420)]
    [InlineData(false, true, "CLASSIC", 420)]
    [InlineData(true, true, "CLASSIC", 420)]
    [InlineData(false, false, "PRACTICETOOL", 0)]
    [InlineData(false, false, "PRACTICETOOL", 2000)]
    public void CompanionRankedEntryOpensIndependentWindowAndAppliesOrCancels(bool cancel, bool reuse, string gameMode, int queueId) => MatchListScrollingTests.OnUiThread(() =>
    {
        int applications = 0;
        var applied = new TaskCompletionSource<OpggBuildApplyResult>();
        var option = new OpggBuildOption(1, [1054], [3078, 3053, 6333], [],
            8000, 8200, [8010, 9111, 9104, 8299, 8234, 8236], 100, 50, Mode: "ranked");
        using var coach = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(new AiGameContext
                { Phase = "ChampSelect", MyChampionId = 122, MyRole = "TOP", GameMode = gameMode, QueueId = queueId })),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((method, _) => method.Name == "Load" ? new AssistantSettings() : null),
            Service<IOpggBuildApplyService>((method, _) =>
            {
                if (method.Name == "GetBuildChoicesAsync")
                    return Task.FromResult(new OpggBuildChoices(true, "", "德莱厄斯", "上路", [option], Mode: "ranked", ChampionId: 122));
                applications++;
                return applied.Task;
            }),
            Service<IGameAssetService>((method, _) => method.Name.Contains("Icon")
                ? Task.FromResult<GameAsset?>(null) : (object)Task.FromResult<string?>(null)));
        using var host = new Form { Opacity = 0, ShowInTaskbar = false };
        Task<string>? firstEntry = null;
        if (reuse)
        {
            host.Show();
            firstEntry = coach.OpenOpggBuildPickerAsync(owner: host);
            Assert.Same(host, System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().Single().Owner);
        }
        ChampSelectCompanionTests.Fixture? entry = null;
        using var fixture = new ChampSelectCompanionTests.Fixture(() => new Rectangle(100, 100, 900, 600),
            openBuildPicker: token => coach.OpenOpggBuildPickerAsync(token, owner: entry!.Form));
        entry = fixture;
        fixture.Form.StartTracking();
        Task opened = fixture.Form.ChooseBuildAsync();
        PumpUntil(() => System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().Any());
        var picker = System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>().Single();
        Assert.Null(picker.Owner);
        Assert.False(picker.Modal);
        Assert.True(picker.ShowInTaskbar);
        Assert.True(picker.Enabled);
        Assert.False(fixture.Form.Visible);
        Assert.Same(opened, fixture.Form.ChooseBuildAsync());
        if (cancel)
        {
            fixture.Form.StopTracking();
            PumpUntil(() => opened.IsCompleted);
            Assert.True(picker.IsDisposed);
            Assert.False(fixture.Form.Visible);
            Assert.Equal(0, applications);
            if (firstEntry != null) PumpUntil(() => firstEntry.IsCompleted);
            return;
        }
        Button? apply = null;
        PumpUntil(() => (apply = picker.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>()
            .FirstOrDefault(button => button.Name == "applyRoute1" && button.Enabled)) != null);
        apply!.PerformClick();
        Assert.Equal(1, applications);
        Assert.Contains(picker.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>(),
            button => button.Text == "正在应用…");
        applied.SetResult(OpggBuildApplyResult.Success("排位方案已配置"));
        PumpUntil(() => picker.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>().Any(button => button.Text == "完成"));
        // 返回入口的结果不能被教练页的后台刷新覆盖。
        ((AntdUI.Label)typeof(CoachForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coach)!).Text = "后台刷新";
        picker.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>().Single(button => button.Text == "完成").PerformClick();
        PumpUntil(() => opened.IsCompleted);
        Assert.True(fixture.Form.Visible);
        Assert.Contains(Descendants(fixture.Form).OfType<AntdUI.Label>(), label => label.Text == "排位方案已配置");
        Assert.Equal(1, applications);
        if (firstEntry != null)
        {
            PumpUntil(() => firstEntry.IsCompleted);
            Assert.Equal("排位方案已配置", firstEntry.GetAwaiter().GetResult());
        }
    });

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
                SummonerSpellIds = new() { 4, 14 },
                ItemBlocks = [new("核心", [3006, 3031, 3094])]
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

    [Theory]
    [InlineData(450, "aram", false)]
    [InlineData(2400, "aram_mayhem", false)]
    [InlineData(450, "aram", true)]
    public void AutomaticBuildsFollowChampionChangesAndUseSavedChoiceWithoutPopup(int queue, string mode, bool personalRunes) => MatchListScrollingTests.OnUiThread(() =>
    {
        var context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1, GameMode = "ARAM", QueueId = queue };
        var settings = new AssistantSettings();
        Assert.True(settings.AutoApplyRuneBuild);
        settings.OpggManualBuildSelections[$"1:{mode}:none"] = 2;
        var personal = new PersonalRunePreset
        {
            ChampionId = 1, Mode = mode, AutoApply = true, PrimaryStyleId = 8100, SubStyleId = 8200,
            RunePerkIds = [8112, 8126, 8138, 8135, 8234, 8236], SummonerSpellIds = [4, 14]
        };
        if (personalRunes) settings.PersonalRunePresets.Add(personal);
        var applied = new List<(int Champion, int Order)>();
        var options = new[] { 1, 2 }.Select(order => new OpggBuildOption(order, [], [3006, 3031, 3094], [],
            8000, 8200, mode == "aram_mayhem" ? [] : [8010, 9111, 9104, 8299, 8234, 8236], 100, 50, Mode: mode)).ToArray();
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(context)),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => settings),
            Service<IOpggBuildApplyService>((method, args) =>
            {
                if (method.Name == "GetBuildChoicesAsync")
                    return Task.FromResult(new OpggBuildChoices(true, "", "英雄", "大乱斗", options, Mode: mode));
                Assert.Equal("ApplyBuildAsync", method.Name);
                Assert.True((bool)args[4]!); // 页面满时允许替换当前页。
                Assert.True((bool)args[6]!); // 同步替换装备。
                if (personalRunes && (int)args[0]! == 1)
                {
                    var option = (OpggBuildOption)args[2]!;
                    Assert.Equal(personal.RunePerkIds, option.RunePerkIds);
                    Assert.Equal(personal.PrimaryStyleId, option.PrimaryStyleId);
                    Assert.Equal(options[0].CoreItemIds, option.CoreItemIds);
                }
                applied.Add(((int)args[0]!, ((OpggBuildOption)args[2]!).Order));
                return Task.FromResult(OpggBuildApplyResult.Success("已应用"));
            }));
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 2, GameMode = "ARAM", QueueId = queue };
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1, GameMode = "ARAM", QueueId = queue };
        form.PromptOpggBuildIfNeededAsync().GetAwaiter().GetResult();
        Assert.Equal(new[] { (1, 2), (2, 1), (1, 2) }, applied);
        Assert.Empty(System.Windows.Forms.Application.OpenForms.OfType<OpggBuildPickerForm>());
    });

    [Theory]
    [InlineData("ChampSelect", 2, true)]
    [InlineData("InProgress", 1, true)]
    [InlineData("ChampSelect", 1, false)]
    public void AutomaticBuildRejectsStaleResponseOrDisabledSetting(string phase, int champion, bool enabled) => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new AssistantSettings();
        var context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1, GameMode = "ARAM" };
        var pending = new TaskCompletionSource<OpggBuildChoices>();
        using var form = new CoachForm(
            Service<IAiCoachingService>((_, _) => Task.FromResult(context)),
            Service<IRecommendationCoordinator>((method, _) => method.Name == "get_Current" ? RecommendationState.Initial : null),
            Service<IApplicationSettingsStore>((_, _) => settings),
            Service<IOpggBuildApplyService>((method, _) =>
            {
                Assert.Equal("GetBuildChoicesAsync", method.Name);
                return pending.Task;
            }));
        Task task = form.PromptOpggBuildIfNeededAsync();
        context = new AiGameContext { Phase = phase, MyChampionId = champion, GameMode = "ARAM" };
        settings.AutoApplyRuneBuild = enabled;
        pending.SetResult(new(true, "", "英雄", "大乱斗",
            [new(1, [], [3006, 3031, 3094], [], 8000, 8200, [8010, 9111, 9104, 8299, 8234, 8236], 100, 50, Mode: "aram")], Mode: "aram"));
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        Assert.Equal("", typeof(CoachForm).GetField("_opggPromptedContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form));
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
