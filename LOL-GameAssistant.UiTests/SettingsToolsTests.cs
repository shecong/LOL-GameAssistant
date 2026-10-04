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
        Assert.False(Field<AntdUI.Checkbox>(form, "_gameKdaOnePlayerPerLine").Enabled);
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
        Assert.Equal(4, navigation.Items.Count);
        Assert.DoesNotContain(Children(tools).OfType<AntdUI.Button>(), button =>
            button.Text is "保存自动化设置" or "快速创建大厅" or "取消当前匹配" or "退出英雄选择" or "交换极地大乱斗备战英雄");
        for (int index = 0; index < 4; index++)
        {
            navigation.SelectIndex = index;
            System.Windows.Forms.Application.DoEvents();
            foreach (FlowLayoutPanel body in Children(tools).OfType<FlowLayoutPanel>().Where(p => p.Visible && p.Dock == DockStyle.Fill))
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
