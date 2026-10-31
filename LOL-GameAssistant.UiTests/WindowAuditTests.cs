using System.Reflection;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class WindowAuditTests
{
    private static PlayerGrowthSnapshot Growth() => new("audit", new GrowthReport([], [], 0, "暂无对局样本"), [],
        ["召唤师峡谷"], DateTimeOffset.Now, false, "布局验收数据");
    private static T Empty<T>() where T : class => Service<T>((method, _) => Default(method.ReturnType));
    private static object? Default(Type type)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(string)) return "";
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GenericTypeArguments[0])
                .Invoke(null, [Default(type.GenericTypeArguments[0])]);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GenericTypeArguments[0], 0);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
    private static IClientFeatureService Features() => Service<IClientFeatureService>((method, _) => method.Name switch
    {
        "GetReplayStateAsync" => Task.FromResult(new ClientReplayState(1, ReplayAvailability.Downloadable, null, "录像可下载，尚未开始下载。")),
        "PreviewBackupAsync" => Task.FromResult(new ClientBackupPreview("测试备份", true, true,
            [new("游戏", "显示设置", "窗口模式", "无边框模式")], "请核对差异并选择需要恢复的类别。")),
        "GetPoroShopAsync" => Task.FromResult(new PoroShopSnapshot("audit", 300, 5,
            [new("one", "皮肤碎片 · 测试长名称布局", "测试", 20, PoroShopCurrency.Silver, 5, true, "可兑换")], "验收数据")),
        "ScanRewardsAsync" => Task.FromResult(new ClientRewardScan([], [new("每日奖励", "当前没有待领取奖励")])),
        _ => Default(method.ReturnType)
    });

    private static Dictionary<Type, Func<AntdUI.Window>> Windows()
    {
        var features = Features();
        var assets = Empty<IGameAssetService>();
        var settings = Service<IApplicationSettingsStore>((m, _) => m.Name == "Load" ? new AssistantSettings() : Default(m.ReturnType));
        var catalog = Service<IChampionCatalog>((m, _) => m.Name == "GetDisplayName" ? "测试英雄" : Default(m.ReturnType));
        var choices = new OpggBuildChoices(true, "当前条件没有推荐样本", "测试英雄", "上路", []);
        return new()
        {
            [typeof(AugmentReviewForm)] = () => new AugmentReviewForm(Empty<IAugmentScanner>(), Empty<IAugmentReviewService>()),
            [typeof(BackupPreviewForm)] = () => new BackupPreviewForm("测试备份", features),
            [typeof(BuildComparisonForm)] = () => new BuildComparisonForm(choices, assets),
            [typeof(ClientCapabilitiesForm)] = () => new ClientCapabilitiesForm(Service<IClientCapabilityService>((_, _) =>
                Task.FromResult(new ClientCapabilitySnapshot(DateTimeOffset.Now, "测试版本", "当前大区", true,
                    [new("GET", "/lol-test/v1/example", ClientCapabilityState.Advertised, "只读验收数据")], "目录读取完成")))),
            [typeof(ClientIdentityForm)] = () => new ClientIdentityForm(Service<IClientIdentityService>((m, _) => m.Name == "ReadAsync"
                ? Task.FromResult(new ClientIdentityCatalog(new("audit", "", "", 0, []), [], ["尚无可用装饰"])) : Default(m.ReturnType))),
            [typeof(EnemyJungleRouteForm)] = () => CreateJungle(),
            [typeof(GameKdaRemarkEditor)] = () => new GameKdaRemarkEditor(new Dictionary<string, string>(), _ => { }),
            [typeof(LiveGameSettingsForm)] = () => new LiveGameSettingsForm(default, _ => { }),
            [typeof(MatchDetailForm)] = () => new MatchDetailForm(new MatchDetail { GameId = 1, participants = [], participantIdentities = [] }, "audit", assets,
                Empty<IMatchHistoryService>(), Empty<LOL_GameAssistant.Application.Teams.IPremadeDetectionService>()),
            [typeof(OpggBuildPickerForm)] = () => new OpggBuildPickerForm(choices, assets, allowApply: false),
            [typeof(PartyHubForm)] = () => new PartyHubForm(Service<IPartyService>((m, _) => m.Name == "GetSnapshotAsync"
                ? Task.FromResult(new PartySnapshot(420, false, false, [new("audit", "测试玩家", true, "上路")], [], [], "当前队伍尚未准备就绪")) : Default(m.ReturnType))),
            [typeof(PlayerCandidatePickerForm)] = () => new PlayerCandidatePickerForm(new([new("audit", "测试玩家", "CN", "HN1", 100, "本机样本")], true, "请先选择玩家再查询")),
            [typeof(PoroShopForm)] = () => new PoroShopForm(features),
            [typeof(PracticeGoalForm)] = () => new PracticeGoalForm(Growth(), catalog, null),
            [typeof(ProfileAppearancePickerForm)] = () => new ProfileAppearancePickerForm(features, Empty<IProfileIconService>(), true),
            [typeof(ReplayManagerForm)] = () => new ReplayManagerForm(1, features),
            [typeof(RewardSelectionForm)] = () => new RewardSelectionForm(features),
            [typeof(RunePresetManagerForm)] = () => new RunePresetManagerForm(settings, Empty<IOpggBuildApplyService>(), Empty<IAiCoachingService>(), assets),
            [typeof(TclsAccountsForm)] = () => new TclsAccountsForm(Empty<ITclsAccountStore>(), null),
            [typeof(UpdateAvailableForm)] = () => new UpdateAvailableForm(new(new Version(99, 0), "验收版本", "https://example.invalid", "更新说明\n这是布局验收数据"), _ => { }),
            [typeof(AntdContentWindow)] = () => ContentWindow()
        };
    }
    private static AntdUI.Window CreateJungle()
    {
        var controller = new EnemyJungleRouteController(new(Empty<ILobbyService>(), Empty<IPlayerProfileService>(), Empty<IMatchHistoryService>(), Empty<IMatchTimelineService>()));
        var window = new EnemyJungleRouteForm(controller);
        window.Disposed += (_, _) => controller.Dispose();
        return window;
    }
    private static AntdUI.Window ContentWindow()
    {
        var window = new AntdContentWindow { Text = "内容详情", Size = new(640, 400), MinimumSize = new(420, 300), StartPosition = FormStartPosition.CenterParent };
        window.Controls.Add(new AntdUI.Input { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = "详情验收" });
        AntdWindowChrome.AddHeader(window); return window;
    }

    [Fact]
    public void WindowInventoryHasExplicitCoverage()
    {
        var covered = Windows().Keys.Concat([typeof(GameMain), typeof(ChampSelectCompanionForm)]).ToHashSet();
        // Main navigation and the client-following window have dedicated live layout/companion tests.
        Assert.DoesNotContain(typeof(GameMain).Assembly.GetTypes(), t => !t.IsAbstract && typeof(Form).IsAssignableFrom(t) && !covered.Contains(t));
    }

    [Theory]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("Light", true)]
    [InlineData("Dark", true)]
    public void DialogActionsFitAtDefaultAndMinimumSizes(string theme, bool minimum) => MatchListScrollingTests.OnUiThread(() =>
    {
        string old = UiTheme.Mode;
        var errors = new List<string>();
        try
        {
            UiTheme.SetMode(theme);
            using var owner = new AntdUI.Window { Size = new(1100, 780), StartPosition = FormStartPosition.Manual, Location = new(180, 80), Opacity = 0, ShowInTaskbar = false };
            owner.Show();
            foreach (var (type, create) in Windows())
            {
                using var window = create(); window.Opacity = 0; window.ShowInTaskbar = false; window.TopMost = false;
                window.Show(owner); System.Windows.Forms.Application.DoEvents();
                if (minimum && window.MinimumSize != Size.Empty) window.Size = window.MinimumSize;
                UiTheme.Apply(window); window.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                Assert.True(Screen.FromControl(window).WorkingArea.Contains(window.Bounds), type.Name + " 超出工作区");
                foreach (Control control in Children(window).Where(c => c.Visible))
                {
                    if (control is Button or TextBox or ComboBox or CheckBox or DataGridView or ListView or RichTextBox)
                        errors.Add(type.Name + " 使用原生交互控件 " + control.GetType().Name);
                    if (control is AntdUI.Button or AntdUI.Checkbox or AntdUI.Input && control.Height < 20)
                        errors.Add($"{type.Name}: {control.Text} 可交互控件高度不足 {control.Height}");
                    if (control.Parent is FlowLayoutPanel { AutoScroll: false } flow && control is AntdUI.IControl &&
                        !Rectangle.Inflate(flow.ClientRectangle, 1, 1).Contains(control.Bounds))
                        errors.Add($"{type.Name}: {control.Text} {control.Bounds} 超出操作栏 {flow.ClientRectangle}");
                }
                if (window is RunePresetManagerForm)
                    Assert.False(((FlowLayoutPanel)window.Controls.Find("runePresetEditor", true).Single()).HorizontalScroll.Visible, "符文编辑区不应出现横向滚动条");
                Capture(window, $"{type.Name}-{theme}-{(minimum ? "minimum" : "default")}");
            }
        }
        finally { UiTheme.SetMode(old); }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    });

    [Fact]
    public void ModelessWindowsCenterOnOwnerAndManualPositionsArePreserved() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new AntdUI.Window { StartPosition = FormStartPosition.Manual, Bounds = new(200, 100, 800, 600), Opacity = 0, ShowInTaskbar = false };
        owner.Show();
        using var child = new AntdUI.Window { StartPosition = FormStartPosition.CenterParent, Size = new(400, 300), Opacity = 0, ShowInTaskbar = false };
        AntdWindowChrome.AddHeader(child); child.Show(owner); System.Windows.Forms.Application.DoEvents();
        Assert.InRange(Math.Abs((child.Left * 2 + child.Width) - (owner.Left * 2 + owner.Width)), 0, 2);
        Assert.InRange(Math.Abs((child.Top * 2 + child.Height) - (owner.Top * 2 + owner.Height)), 0, 2);
        using var manual = new AntdUI.Window { StartPosition = FormStartPosition.Manual, Bounds = new(60, 60, 400, 300), Opacity = 0, ShowInTaskbar = false };
        AntdWindowChrome.AddHeader(manual); manual.Show(owner); System.Windows.Forms.Application.DoEvents();
        Assert.Equal(new Point(60, 60), manual.Location);
    });

    [Theory]
    [InlineData(760, "Dark")]
    [InlineData(1100, "Light")]
    public void EverySettingsCategoryHasUsableControlsAndReachableToolbars(int width, string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string old = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(theme);
            var settings = Service<IApplicationSettingsStore>((m, _) => m.Name == "Load" ? new AssistantSettings() : m.Name == "GetStoragePath" ? "audit-settings.json" : Default(m.ReturnType));
            using var page = new SettingForm(Empty<IGameClientLauncher>(), settings, Empty<ISettingsSecretProtector>(), Features());
            using var host = new AntdUI.Window { ClientSize = new(width, 780), Opacity = 0, ShowInTaskbar = false };
            page.Dock = DockStyle.Fill; host.Controls.Add(page); host.Show();
            var navigation = Field<AntdUI.Segmented>(page, "_settingsSegmented");
            var errors = new List<string>();
            for (int index = 0; index < navigation.Items.Count; index++)
            {
                navigation.SelectIndex = index; System.Windows.Forms.Application.DoEvents();
                UiTheme.Apply(page); page.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                foreach (Control control in Children(page).Where(c => c.Visible))
                {
                    if (control is AntdUI.Button or AntdUI.Checkbox or AntdUI.Input && control.Height < 20)
                        errors.Add($"{navigation.Items[index].Text}: {control.Name} 高度不足 {control.Height}");
                    if (control.Parent is FlowLayoutPanel { AutoScroll: false } flow && control is AntdUI.IControl &&
                        !Rectangle.Inflate(flow.ClientRectangle, 1, 1).Contains(control.Bounds))
                        errors.Add($"{navigation.Items[index].Text}: {control.Text} {control.Bounds} 超出操作栏 {flow.ClientRectangle}");
                }
                Capture(page, $"Settings-{index}-{width}-{theme}");
            }
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
        finally { UiTheme.SetMode(old); }
    });

    [Fact]
    public void ReturningFromSmallMonitorRestoresDesignedMinimumAndHandlesNegativeCoordinates() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new AntdUI.Window { MinimumSize = new(900, 650), Size = new(1000, 700), StartPosition = FormStartPosition.Manual };
        WindowLayout.FitToWorkingArea(window, new Rectangle(-800, 0, 800, 600), 96);
        Assert.Equal(new Size(768, 568), window.MinimumSize);
        Assert.True(new Rectangle(-784, 16, 768, 568).Contains(window.Bounds));
        WindowLayout.FitToWorkingArea(window, new Rectangle(0, 0, 1920, 1080), 96);
        Assert.Equal(new Size(900, 650), window.MinimumSize);
        WindowLayout.FitToWorkingArea(window, new Rectangle(0, 0, 2560, 1440), 144);
        Assert.Equal(new Size(1350, 975), window.MinimumSize);
    });

    [Fact]
    public void EditingGoalPreservesHeroOutsideRecentSample() => MatchListScrollingTests.OnUiThread(() =>
    {
        var existing = new PracticeGoal(PracticeMetric.Cs, 6, "召唤师峡谷", 99, DateTimeOffset.UtcNow, 5);
        using var window = new PracticeGoalForm(Growth(), Service<IChampionCatalog>((_, _) => "测试英雄"), existing);
        window.Show();
        Children(window).OfType<AntdUI.Button>().Single(b => b.Name == "saveGoal").PerformClick();
        Assert.Equal(99, window.Goal!.ChampionId);
    });

    [Fact]
    public void BackupRestoreRequiresSelectionAndRejectsDuplicateSubmission() => MatchListScrollingTests.OnUiThread(() =>
    {
        var completion = new TaskCompletionSource<ClientFeatureResult>(); int calls = 0;
        var features = Service<IClientFeatureService>((method, _) => method.Name == "PreviewBackupAsync"
            ? Task.FromResult(new ClientBackupPreview("audit", true, true, [], "请选择"))
            : Submit());
        Task<ClientFeatureResult> Submit() { calls++; return completion.Task; }
        using var window = new BackupPreviewForm("audit", features); window.Show(); System.Windows.Forms.Application.DoEvents();
        var checks = Children(window).OfType<AntdUI.Checkbox>().ToArray();
        var restore = Children(window).OfType<AntdUI.Button>().Single();
        foreach (var check in checks) check.Checked = false;
        Assert.False(restore.Enabled); restore.PerformClick(); Assert.Equal(0, calls);
        checks[0].Checked = true; Assert.True(restore.Enabled); restore.PerformClick();
        checks[1].Checked = true; Assert.False(restore.Enabled); restore.PerformClick(); Assert.Equal(1, calls);
        completion.SetResult(ClientFeatureResult.Success("已完成"));
        PumpUntil(() => restore.Enabled);
    });

    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    private static void Capture(Control control, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("WINDOW_AUDIT_PREVIEWS"); if (folder == null) return;
        Directory.CreateDirectory(folder); using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, control.ClientRectangle); image.Save(Path.Combine(folder, name + ".png"));
    }
}
