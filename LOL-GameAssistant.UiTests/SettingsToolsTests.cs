using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Insights;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class SettingsToolsTests
{
    [Fact]
    public void HeavySettingsPagesAreCreatedOnDemandAndReused() => MatchListScrollingTests.OnUiThread(() =>
    {
        int saves = 0;
        var settings = new AssistantSettings
        {
            QuickMessageCustomPhrases = "按需加载的文案", WindowOpacityPercent = 78, ThemeMode = "Dark",
            Ai = new CloudAiSettings { Model = "saved-model", BaseUrl = "https://example.com/v1" }
        };
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name switch
            {
                "Load" => settings,
                "GetStoragePath" => "test-settings.json",
                "Save" => ++saves,
                _ => null
            }), Service<ISettingsSecretProtector>((_, _) => null),
            Service<IClientFeatureService>((_, _) => null));
        var segments = Field<Control[]>(form, "_settingsSegments");
        Assert.NotNull(segments[0]);
        Assert.All(segments.Skip(1), segment => Assert.Null(segment));
        ((Task)Invoke(form, "LoadCachedSettings")!).GetAwaiter().GetResult();
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        var navigation = Field<AntdUI.Segmented>(form, "_settingsSegmented");
        foreach (int index in new[] { 1, 2, 4 })
        {
            navigation.SelectIndex = index;
            var page = segments[index];
            Assert.NotNull(page);
            navigation.SelectIndex = 0;
            navigation.SelectIndex = index;
            Assert.Same(page, segments[index]);
        }
        Assert.Equal(78, Field<AntdUI.InputNumber>(form, "_opacity").Value);
        Assert.Equal(2, Field<AntdUI.Select>(form, "_themeMode").SelectedIndex);
        Assert.Equal("saved-model", Field<AntdUI.Select>(form, "_model").Text);
        Assert.Equal("https://example.com/v1", Field<AntdUI.Input>(form, "_baseUrl").Text);
        Field<AntdUI.Select>(form, "_model").Text = "unsaved-draft";
        navigation.SelectIndex = 0;
        navigation.SelectIndex = 4;
        Assert.Equal("unsaved-draft", Field<AntdUI.Select>(form, "_model").Text);
        navigation.SelectIndex = 3;
        var shout = Assert.IsType<QuickShoutForm>(segments[3]);
        Assert.Equal(settings.QuickMessageCustomPhrases, Field<AntdUI.Input>(shout, "_customPhrases").Text);
        navigation.SelectIndex = 5;
        var tools = Assert.IsType<ClientToolsForm>(segments[5]);
        navigation.SelectIndex = 0;
        navigation.SelectIndex = 3;
        Assert.Same(shout, segments[3]);
        Assert.Same(tools, segments[5]);
        Assert.Equal(0, saves);
        form.Dispose();
        Assert.True(shout.IsDisposed);
        Assert.True(tools.IsDisposed);
    });

    [Fact]
    public void ShoutHotkeyCanInitializeItsPageWithoutOpeningSettingsCategory() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name == "GetStoragePath"
                ? "test-settings.json" : new AssistantSettings()),
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        // 空的自定义文案不会向游戏发送，但仍走快捷键的实际初始化路径。
        form.SendRandomQuickShoutToGameAsync(custom: true).GetAwaiter().GetResult();
        Assert.IsType<QuickShoutForm>(Field<Control[]>(form, "_settingsSegments")[3]);
        Assert.Equal(0, Field<AntdUI.Segmented>(form, "_settingsSegmented").SelectIndex);
    });

    [Fact]
    public void CompanionCheckboxPersistsImmediatelyWithoutSavingAiSettings() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new AssistantSettings { ChampSelectCompanionEnabled = false, LanguageMode = "en-US" };
        int saves = 0;
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => settings,
            "GetStoragePath" => "test-settings.json",
            "Save" => Save((AssistantSettings)args[0]!),
            _ => null
        });
        object? Save(AssistantSettings value) { settings = value; saves++; return null; }
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        ((Task)Invoke(form, "LoadCachedSettings")!).GetAwaiter().GetResult();
        Assert.Equal(0, saves);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        settings.AutoHonor = true; // 另一页面在此期间保存的修改必须保留。
        Field<AntdUI.Checkbox>(form, "_champSelectCompanionEnabled").Checked = true;
        Assert.True(settings.ChampSelectCompanionEnabled);
        Assert.True(settings.AutoHonor);
        Assert.Equal("en-US", settings.LanguageMode);
        Assert.Equal(1, saves);
        Field<AntdUI.Checkbox>(form, "_champSelectCompanionEnabled").Checked = false;
        Assert.False(settings.ChampSelectCompanionEnabled);
        Assert.Equal(2, saves);
    });

    [Fact]
    public void SavingOpenSettingsPageKeepsNewerSkinSettings() => MatchListScrollingTests.OnUiThread(() =>
    {
        var latest = new AssistantSettings();
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(latest)),
            "Save" => Save((AssistantSettings)args[0]!),
            "GetStoragePath" => "test-settings.json",
            _ => null
        });
        object? Save(AssistantSettings value) { latest = value; return null; }
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        ((Task)Invoke(form, "LoadCachedSettings")!).GetAwaiter().GetResult();
        latest.SkinCoreEnabled = true;
        latest.SkinHotkeysEnabled = true;
        latest.SkinPreviousHotkey = "F7";
        latest.SkinNextHotkey = "F8";
        latest.SkinPickerHotkey = "Ctrl+F10";
        typeof(SettingForm).GetField("_isLoading", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, false);
        Field<AntdUI.Switch>(form, "swi_gametrue").Checked = true;
        Assert.True(latest.AutoAccept);
        Assert.True(latest.SkinCoreEnabled);
        Assert.True(latest.SkinHotkeysEnabled);
        Assert.Equal("F7", latest.SkinPreviousHotkey);
        Assert.Equal("F8", latest.SkinNextHotkey);
        Assert.Equal("Ctrl+F10", latest.SkinPickerHotkey);
    });

    [Fact]
    public void KdaSwitchesPersistImmediatelyWithoutSavingAiSettings() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new AssistantSettings { ChampSelectKdaAnnouncementEnabled = true, QuickLobbyQueueId = 450 };
        int saves = 0;
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => settings,
            "GetStoragePath" => "test-settings.json",
            "Save" => Save((AssistantSettings)args[0]!),
            _ => null
        });
        object? Save(AssistantSettings value) { settings = value; saves++; return null; }
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        ((Task)Invoke(form, "LoadCachedSettings")!).GetAwaiter().GetResult();
        Assert.Equal(0, saves);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        Field<AntdUI.Checkbox>(form, "_gameKdaAnnouncementEnabled").Checked = true;
        Assert.True(settings.GameKdaAnnouncementEnabled);
        Assert.True(Field<AntdUI.Checkbox>(form, "_gameKdaOnePlayerPerLine").Enabled);
        Field<AntdUI.Checkbox>(form, "_gameKdaOnePlayerPerLine").Checked = true;
        Assert.True(settings.GameKdaOnePlayerPerLine);
        Field<AntdUI.Checkbox>(form, "_gameKdaAnnouncementEnabled").Checked = false;
        Assert.False(settings.GameKdaAnnouncementEnabled);
        // 手动 KDA 同样使用逐人格式，关闭自动发送后仍可配置。
        Assert.True(Field<AntdUI.Checkbox>(form, "_gameKdaOnePlayerPerLine").Enabled);
        Assert.True(settings.ChampSelectKdaAnnouncementEnabled);
        Assert.Equal(450, settings.QuickLobbyQueueId);
        Assert.Equal(3, saves);
    });

    [Fact]
    public void MovedAutomationSettingsLoadAndSaveWithoutChangingOtherSettings() => MatchListScrollingTests.OnUiThread(() =>
    {
        var settings = new AssistantSettings
        {
            AutoAccept = true, AutoAcceptDelayMinMilliseconds = 300, AutoAcceptDelayMaxMilliseconds = 900,
            AutoPickPreselectOnly = true, SkipAutoPickOnFill = true, AutoHonor = true,
            AutoReturnToLobby = true, AutoReturnStartMatchmaking = true, QuickLobbyQueueId = 450,
            LanguageMode = "en-US"
        };
        int saves = 0;
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => settings,
            "GetStoragePath" => "test-settings.json",
            "Save" => Save((AssistantSettings)args[0]!),
            _ => null
        });
        object? Save(AssistantSettings value) { settings = value; saves++; return null; }
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null), store,
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        ((Task)Invoke(form, "LoadCachedSettings")!).GetAwaiter().GetResult();
        Assert.Equal(0, saves);
        Assert.True(Field<AntdUI.Switch>(form, "_autoHonor").Checked);
        Assert.Equal(450m, Field<AntdUI.InputNumber>(form, "_quickQueue").Value);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        Field<AntdUI.Switch>(form, "_autoHonor").Checked = false;
        Field<AntdUI.InputNumber>(form, "_acceptMin").Value = 500;
        Field<AntdUI.InputNumber>(form, "_quickQueue").Value = 430;
        Assert.Equal(3, saves);
        Assert.False(settings.AutoHonor);
        Assert.True(settings.AutoReturnToLobby);
        Assert.True(settings.AutoReturnStartMatchmaking);
        Assert.True(settings.AutoPickPreselectOnly);
        Assert.True(settings.SkipAutoPickOnFill);
        Assert.True(settings.AutoAccept);
        Assert.Equal(500, settings.AutoAcceptDelayMinMilliseconds);
        Assert.Equal(900, settings.AutoAcceptDelayMaxMilliseconds);
        Assert.Equal(430, settings.QuickLobbyQueueId);
        Assert.Equal("en-US", settings.LanguageMode);
    });

    [Fact]
    public void LobbyActionsUseSelectedQueueAndCorrectService() => MatchListScrollingTests.OnUiThread(() =>
    {
        var calls = new List<(string Method, object?[] Args)>();
        var features = Service<IClientFeatureService>((method, args) =>
        {
            calls.Add((method.Name, args));
            return Task.FromResult(ClientFeatureResult.Success("完成"));
        });
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name == "GetStoragePath" ? "test-settings.json" : new AssistantSettings()),
            Service<ISettingsSecretProtector>((_, _) => null), features);
        Field<AntdUI.InputNumber>(form, "_quickQueue").Value = 450;
        foreach (string name in new[] { "快速创建大厅", "取消当前匹配", "退出英雄选择" })
            Click(Children(form).OfType<AntdUI.Button>().Single(button => button.Text == name));
        Assert.Equal(new[] { "CreateQuickLobbyAsync", "DeclineReadyCheckAsync", "DodgeChampionSelectAsync" }, calls.Select(c => c.Method));
        Assert.Equal(450, calls[0].Args[0]);
    });

    [Theory]
    [InlineData(760)]
    [InlineData(1100)]
    public void ClientToolsCategoriesFitAndRemovedToolsAreAbsent(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var tools = new ClientToolsForm(
            Service<IClientFeatureService>((method, _) => method.Name == "ListGameSettingsBackupsAsync"
                ? Task.FromResult<IReadOnlyList<ClientSettingsBackup>>([]) : null),
            Service<IChampionInsightsService>((_, _) => null),
            Service<IProfileIconService>((_, _) => null));
        using var host = new Form { ClientSize = new Size(width, 680), ShowInTaskbar = false };
        host.Controls.Add(tools);
        host.Show();
        var navigation = Children(tools).OfType<AntdUI.Segmented>().Single();
        Assert.Equal(6, navigation.Items.Count);
        Assert.DoesNotContain(Children(tools).OfType<AntdUI.Button>(), button =>
            button.Text is "保存自动化设置" or "快速创建大厅" or "取消当前匹配" or "退出英雄选择" or "交换极地大乱斗备战英雄");
        for (int index = 0; index < navigation.Items.Count; index++)
        {
            navigation.SelectIndex = index;
            System.Windows.Forms.Application.DoEvents();
            // 好友目录是可滚动列表，内容允许超出视口；操作工具区仍必须完整显示。
            foreach (FlowLayoutPanel body in Children(tools).OfType<FlowLayoutPanel>().Where(p => p.Visible && p.Dock == DockStyle.Fill && !p.AutoScroll))
                foreach (Control control in body.Controls)
                    Assert.True(body.ClientRectangle.Contains(control.Bounds), $"Category {index}: {control.Text} {control.Bounds} outside {body.ClientRectangle}");
        }
        string? preview = Environment.GetEnvironmentVariable("CLIENT_TOOLS_PREVIEW");
        if (width == 1100 && !string.IsNullOrEmpty(preview))
        {
            navigation.SelectIndex = 1;
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(preview);
        }
    });

    private static object? Invoke(object target, string name) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Click(Control control) => control.GetType()
        .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [EventArgs.Empty]);
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));
    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
