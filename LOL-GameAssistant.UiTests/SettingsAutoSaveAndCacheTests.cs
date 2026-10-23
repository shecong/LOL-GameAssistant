using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Files;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SettingsAutoSaveAndCacheTests
{
    [Fact]
    public async Task MatchCacheIncludesSharedParticipantsAndClearsOnlyThatCategory()
    {
        using var folder = new TemporaryDirectory();
        int clears = 0;
        var service = new LocalCacheManagementService(folder.Path, matchCaches:
            [(() => (3, 4096L), () => clears++)]);
        var usage = (await service.GetUsageAsync()).Single(value => value.Category == LocalCacheCategory.MatchDetails);
        Assert.True(usage.MemoryEntries >= 3);
        Assert.True(usage.MemoryDataBytes >= 4096);
        await service.ClearAsync(LocalCacheCategory.AugmentIcons);
        Assert.Equal(0, clears);
        await service.ClearAsync(LocalCacheCategory.MatchDetails);
        Assert.Equal(1, clears);
    }

    [Fact]
    public void EverySettingsCheckboxAndAutomationSwitchSavesOnlyItsValueWithoutAiDrafts() => MatchListScrollingTests.OnUiThread(() =>
    {
        var latest = new AssistantSettings();
        int saves = 0;
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(latest)),
            "GetStoragePath" => "test-settings.json",
            "Save" => Save((AssistantSettings)args[0]!), _ => null
        });
        object? Save(AssistantSettings value) { latest = value; saves++; return null; }
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => ""), store,
            Service<ISettingsSecretProtector>((_, _) => throw new InvalidOperationException("不得提交密钥草稿")),
            Service<IClientFeatureService>((_, _) => null));
        Task Load() => (Task)typeof(SettingForm).GetMethod("LoadCachedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
        Pump(Load()); Pump(Load());
        Assert.Equal(0, saves);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        Field<AntdUI.Input>(form, "_baseUrl").Text = "http://invalid.example";
        Field<AntdUI.Input>(form, "_apiKey").Text = "未完成的密钥草稿";
        latest.SkinNextHotkey = "Ctrl+F8";
        latest.SkinCoreMode = LOL_GameAssistant.Domain.Skins.SkinCoreMode.KernelRelay;
        string[] settingsFields = ["swi_open:AutoMatch", "swi_gametrue:AutoAccept", "swi_jyyx:AutoBan", "swi_xyx:AutoPick",
            "swi_tray:MinimizeToTray", "swi_auto_refresh:AutoRefresh", "swi_notify_end:NotifyOnGameEnd",
            "_preselectOnly:AutoPickPreselectOnly", "_skipFill:SkipAutoPickOnFill", "_autoHonor:AutoHonor",
            "_autoReturn:AutoReturnToLobby", "_returnAndSearch:AutoReturnStartMatchmaking", "_autoSwapAramBench:AutoSwapAramBench",
            "_autoLaunchClient:AutoLaunchGameClient", "_onlyLeagueFocused:HoldToTopOnlyWhenLeagueFocused", "_battleOverlayEnabled:BattleOverlayEnabled",
            "_opggBuildAssistantEnabled:OpggBuildAssistantEnabled", "_autoApplyRuneBuild:AutoApplyRuneBuild", "_mayhemOverlayEnabled:MayhemOverlayEnabled",
            "_champSelectCompanionEnabled:ChampSelectCompanionEnabled", "_champSelectKdaAnnouncementEnabled:ChampSelectKdaAnnouncementEnabled",
            "_gameKdaAnnouncementEnabled:GameKdaAnnouncementEnabled", "_gameKdaOnePlayerPerLine:GameKdaOnePlayerPerLine",
            "_recommendationEnabled:Ai.RecommendationEnabled", "_dynamicRefresh:Ai.DynamicRefreshEnabled", "_showPopup:Ai.ShowRecommendationPopup",
            "_overlayEnabled:Ai.RecommendationOverlayEnabled"];
        foreach (string mapping in settingsFields)
        {
            string[] parts = mapping.Split(':');
            var control = Field<Control>(form, parts[0]);
            var checkedProperty = control.GetType().GetProperty("Checked")!;
            bool original = (bool)checkedProperty.GetValue(control)!;
            int before = saves;
            checkedProperty.SetValue(control, !original);
            Assert.Equal(before + 1, saves);
            object target = latest;
            string property = parts[1];
            if (property.StartsWith("Ai.")) { target = latest.Ai; property = property[3..]; }
            Assert.Equal(!original, (bool)target.GetType().GetProperty(property)!.GetValue(target)!);
            Assert.Equal("", latest.Ai.BaseUrl);
            Assert.Equal("", latest.Ai.EncryptedApiKey);
            Assert.Equal("Ctrl+F8", latest.SkinNextHotkey);
            Assert.Equal(LOL_GameAssistant.Domain.Skins.SkinCoreMode.KernelRelay, latest.SkinCoreMode);
            checkedProperty.SetValue(control, original);
            Assert.Equal(before + 2, saves);
        }
        var minimum = Field<AntdUI.InputNumber>(form, "_ratingMinimumGames");
        Assert.Equal(10, minimum.Value);
        int beforeMinimum = saves;
        minimum.Value = 15;
        Assert.Equal(beforeMinimum + 1, saves);
        Assert.Equal(15, latest.RecentPerformanceMinimumGames);
        Field<AntdUI.InputNumber>(form, "_aiBudget").Value = 42;
        Field<AntdUI.InputNumber>(form, "_aiMinimumSeconds").Value = 45;
        Assert.Equal(42, latest.Ai.MatchRequestBudget);
        Assert.Equal(45, latest.Ai.MinimumRequestSeconds);
        Assert.Equal("", latest.Ai.BaseUrl);
        // 不在测试中改变 Windows 登录启动注册表；启动开关使用同一个保存绑定。
        Assert.DoesNotContain(Children(form).OfType<AntdUI.Button>(), button => button.Text is "保存客户端设置" or "保存窗口设置");
    });

    [Fact]
    public void QuickShoutSwitchesSaveImmediatelyAndLoadingDoesNotSave() => MatchListScrollingTests.OnUiThread(() =>
    {
        var latest = new AssistantSettings(); int saves = 0;
        using var form = new QuickShoutForm(null!, (_, _, _, _, _) => throw new InvalidOperationException(),
            (_, _, _, _, _) => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException("开关不应提交词库草稿"), update => { update(latest); saves++; return true; });
        form.LoadSettings(latest);
        Assert.Equal(0, saves);
        Field<AntdUI.Input>(form, "_customPhrases").Text = new string('草', 501);
        foreach (string name in new[] { "_perCharacter", "_sendToAll", "_useClipboard", "_hotkeysEnabled", "_kdaHotkeyEnabled", "_playerHotkeysEnabled" })
        {
            var checkbox = Field<AntdUI.Checkbox>(form, name);
            int before = saves;
            checkbox.Checked = !checkbox.Checked;
            Assert.Equal(before + 1, saves);
        }
        Assert.Equal("", latest.QuickMessageCustomPhrases);
        int beforeSelection = saves;
        Field<ListBox>(form, "_phrases").SelectedIndex = 2;
        Assert.Equal(beforeSelection + 1, saves);
        Assert.Single(latest.QuickShoutSelectedPhrases);
    });

    [Fact]
    public void SavingAiConnectionDoesNotOverwriteNewerSwitchesOrWindowSettings() => MatchListScrollingTests.OnUiThread(() =>
    {
        var latest = new AssistantSettings(); int saves = 0;
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => ""),
            Service<IApplicationSettingsStore>((method, args) => method.Name switch
            {
                "Load" => Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(latest)),
                "GetStoragePath" => "test-settings.json", "Save" => Save((AssistantSettings)args[0]!), _ => null
            }), Service<ISettingsSecretProtector>((_, _) => "protected-test-value"), Service<IClientFeatureService>((_, _) => null));
        object? Save(AssistantSettings value) { latest = value; saves++; return null; }
        Pump((Task)typeof(SettingForm).GetMethod("LoadCachedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        latest.AutoHonor = true; latest.ThemeMode = "Dark"; latest.Ai.DynamicRefreshEnabled = false;
        Field<AntdUI.Input>(form, "_baseUrl").Text = "https://api.example.test/v1";
        Field<Control>(form, "_model").Text = "test-model";
        Field<AntdUI.Input>(form, "_apiKey").Text = "test-value";
        typeof(SettingForm).GetMethod("SaveExtendedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        Assert.Equal(1, saves); Assert.True(latest.AutoHonor); Assert.Equal("Dark", latest.ThemeMode);
        Assert.False(latest.Ai.DynamicRefreshEnabled); Assert.Equal("test-model", latest.Ai.Model);
        Assert.Equal("protected-test-value", latest.Ai.EncryptedApiKey);
    });

    [Fact]
    public async Task ClearingOneCategoryRemovesDiskAndMemoryAndPreservesOtherData()
    {
        using var folder = new TemporaryDirectory();
        string assets = Path.Combine(folder.Path, "assets");
        var cache = new LocalDataCache<string>(assets, TimeSpan.FromDays(1), text => text.Length > 0);
        await cache.GetAsync("icon", () => Task.FromResult<string?>("资源"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "builds"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "builds", "other.json"), "build");
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "settings.json"), "settings");
        var service = new LocalCacheManagementService(folder.Path);
        var before = (await service.GetUsageAsync()).Single(row => row.Category == LocalCacheCategory.GameAssets);
        Assert.Equal(1, before.FileCount); Assert.True(before.DiskBytes > 0); Assert.Equal(1, before.MemoryEntries);
        var result = await service.ClearAsync(LocalCacheCategory.GameAssets);
        Assert.Equal(0, result.FailedFiles); Assert.Equal(1, result.DeletedFiles);
        var after = (await service.GetUsageAsync()).Single(row => row.Category == LocalCacheCategory.GameAssets);
        Assert.Equal(0, after.FileCount); Assert.Equal(0, after.MemoryEntries);
        Assert.True(File.Exists(Path.Combine(folder.Path, "builds", "other.json")));
        Assert.Equal("settings", await File.ReadAllTextAsync(Path.Combine(folder.Path, "settings.json")));
        Assert.Equal("new", await cache.GetAsync("icon", () => Task.FromResult<string?>("new")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ClearAsync((LocalCacheCategory)999));
    }

    [Fact]
    public async Task ClearingDuringDownloadDoesNotRestoreOldDataOrEvictNewRequest()
    {
        using var folder = new TemporaryDirectory();
        var cache = new LocalDataCache<string>(Path.Combine(folder.Path, "assets"), TimeSpan.FromDays(1), value => value.Length > 0);
        var oldData = new TaskCompletionSource<string?>();
        var newData = new TaskCompletionSource<string?>();
        var old = cache.GetAsync("same", () => oldData.Task);
        await new LocalCacheManagementService(folder.Path).ClearAsync(LocalCacheCategory.GameAssets);
        var current = cache.GetAsync("same", () => newData.Task);
        oldData.SetResult("old"); Assert.Equal("old", await old);
        Assert.Empty(Directory.GetFiles(Path.Combine(folder.Path, "assets"), "*.json"));
        Assert.Equal(0, cache.MeasureMemory().Entries);
        var another = cache.GetAsync("same", () => throw new InvalidOperationException("不得重复下载新请求"));
        newData.SetResult("new");
        Assert.Equal("new", await current); Assert.Equal("new", await another);
        Assert.Equal("new", await cache.GetAsync("same", () => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task LockedFileReportsPartialFailureAndCancelledClearPreservesFiles()
    {
        using var folder = new TemporaryDirectory();
        string assets = Path.Combine(folder.Path, "assets"); Directory.CreateDirectory(assets);
        string locked = Path.Combine(assets, "locked.json");
        await File.WriteAllTextAsync(locked, "locked");
        await File.WriteAllTextAsync(Path.Combine(assets, "ok.json"), "ok");
        var service = new LocalCacheManagementService(folder.Path);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ClearAsync(LocalCacheCategory.GameAssets, cts.Token));
        Assert.Equal(2, Directory.GetFiles(assets).Length);
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await service.ClearAsync(LocalCacheCategory.GameAssets);
            Assert.True(result.FailedFiles > 0); Assert.Single(Directory.GetFiles(assets));
        }
        Assert.Equal(0, (await service.ClearAsync(LocalCacheCategory.GameAssets)).FailedFiles);
        Assert.Empty(Directory.GetFiles(assets));
    }

    [Fact]
    public void CachePanelDisplaysCategoriesAndIgnoresLateResultAfterClose() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<LocalCacheUsage>>(); CancellationToken token = default;
        using var panel = new LocalCacheToolsControl(Service<ILocalCacheManagementService>((_, args) => { token = (CancellationToken)args[0]!; return pending.Task; }));
        var task = panel.RefreshUsageAsync(); panel.Dispose(); Assert.True(token.IsCancellationRequested);
        pending.SetResult([]); Pump(task);
    });

    [Fact]
    public void CachePanelCoalescesStartupRefreshAndDoesNotCancelActiveScan() => MatchListScrollingTests.OnUiThread(() =>
    {
        int scans = 0;
        CancellationToken token = default;
        var pending = new TaskCompletionSource<IReadOnlyList<LocalCacheUsage>>();
        using var panel = new LocalCacheToolsControl(Service<ILocalCacheManagementService>((_, args) =>
        {
            scans++;
            token = (CancellationToken)args[0]!;
            return pending.Task;
        }));
        using var host = new Form { ShowInTaskbar = false };
        host.Controls.Add(panel);
        host.Show();
        Pump(panel.RefreshUsageAsync());
        Assert.Equal(1, scans);
        Assert.False(token.IsCancellationRequested);
        pending.SetResult([]);
        System.Windows.Forms.Application.DoEvents();
        Pump(panel.RefreshUsageAsync());
        Assert.Equal(2, scans);
    });

    [Fact]
    public void CachePanelHandlesCancelledScanAfterDisposal() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<LocalCacheUsage>>();
        CancellationToken token = default;
        using var panel = new LocalCacheToolsControl(Service<ILocalCacheManagementService>((_, args) =>
        {
            token = (CancellationToken)args[0]!;
            return pending.Task;
        }));
        Task refresh = panel.RefreshUsageAsync();
        panel.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetCanceled(token);
        Pump(refresh);
        Assert.True(refresh.IsCompletedSuccessfully);
    });

    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));

    [Fact]
    public void FailedSettingWriteRestoresSwitchAndDoesNotChangePersistedSetting() => MatchListScrollingTests.OnUiThread(() =>
    {
        var persisted = new AssistantSettings { AutoLaunchGameClient = false };
        using var form = new SettingForm(Service<IGameClientLauncher>((_, _) => ""),
            Service<IApplicationSettingsStore>((method, _) => method.Name switch
            {
                "Load" => Newtonsoft.Json.JsonConvert.DeserializeObject<AssistantSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(persisted)),
                "GetStoragePath" => "test-settings.json", "Save" => throw new IOException("文件已占用"), _ => null
            }), Service<ISettingsSecretProtector>((_, _) => ""), Service<IClientFeatureService>((_, _) => null));
        Pump((Task)typeof(SettingForm).GetMethod("LoadCachedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!);
        typeof(SettingForm).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, false);
        var control = Field<AntdUI.Checkbox>(form, "_autoLaunchClient");
        control.Checked = true;
        Assert.False(control.Checked); Assert.False(persisted.AutoLaunchGameClient);
        Assert.Contains("保存失败", Field<AntdUI.Label>(form, "label_cache_status").Text);
    });

    [Fact]
    public void FailedQuickShoutWriteRestoresCheckbox() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new QuickShoutForm(null!, (_, _, _, _, _) => throw new InvalidOperationException(),
            (_, _, _, _, _) => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => { }, _ => false);
        form.LoadSettings(new());
        var checkbox = Field<AntdUI.Checkbox>(form, "_sendToAll");
        checkbox.Checked = true;
        Assert.False(checkbox.Checked);
        Assert.Contains("未保存", Field<AntdUI.Label>(form, "_status").Text);
    });

    [Theory]
    [InlineData(760)]
    [InlineData(1100)]
    public void CachePanelShowsSizesAndKeepsClearButtonsInsideRows(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        var clears = new List<LocalCacheCategory>();
        var usage = Enum.GetValues<LocalCacheCategory>().Select(category => new LocalCacheUsage(category, category.ToString(),
            "C:\\Users\\测试用户\\AppData\\Local\\LOL-GameAssistant\\cache\\v1\\assets", 1024 * 1024, 5, 1024, 2)).ToArray();
        var service = Service<ILocalCacheManagementService>((method, args) =>
        {
            if (method.Name == "GetUsageAsync") return Task.FromResult<IReadOnlyList<LocalCacheUsage>>(usage);
            clears.Add((LocalCacheCategory)args[0]!);
            return Task.FromResult(new LocalCacheClearResult(5, 0, "已清除"));
        });
        using var panel = new LocalCacheToolsControl(service);
        using var host = new Form { ClientSize = new Size(width, 550), ShowInTaskbar = false };
        host.Controls.Add(panel); host.Show();
        Pump(panel.RefreshUsageAsync());
        var buttons = Children(panel).OfType<AntdUI.Button>().Where(button => button.Tag is LocalCacheCategory).ToArray();
        Assert.Equal(4, buttons.Length);
        foreach (var button in buttons) Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds));
        Assert.Contains(Children(panel).OfType<AntdUI.Label>(), label => (label.Text ?? "").Contains("1 MB") && (label.Text ?? "").Contains("1 KB"));
        foreach (var category in Enum.GetValues<LocalCacheCategory>()) Pump(panel.ClearCategoryAsync(category));
        Assert.Equal(Enum.GetValues<LocalCacheCategory>(), clears);
        string? preview = Environment.GetEnvironmentVariable("CACHE_TOOLS_PREVIEW");
        if (width == 1100 && !string.IsNullOrEmpty(preview))
        {
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
            bitmap.Save(preview);
        }
    });
    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lol-cache-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            Directory.Delete(resolved, recursive: true);
        }
    }
}
