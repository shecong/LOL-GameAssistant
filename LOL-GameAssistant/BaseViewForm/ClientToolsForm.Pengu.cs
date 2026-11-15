using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Infrastructure.Pengu;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class ClientToolsForm
{
    private Control CreatePenguPluginCard()
    {
        IPenguPluginService plugin = _penguPlugin;
        IPenguLocalLoaderService loader = AppCompositionRoot.PenguLocalLoader;
        var settingsService = new PenguVisualSettingsService(() => loader.Prepare().PluginsDirectory);
        var clientOptions = new PenguClientOptionsService(() => Path.Combine(Path.GetDirectoryName(loader.Prepare().CorePath)!, "config"));
        PenguPluginStatusForm? checkWindow = null;
        var card = Card("客户端视觉插件（Pengu）", 610, out FlowLayoutPanel body);
        var status = Caption("", 650);
        status.Height = 80;
        var enabled = new AntdUI.Checkbox { Text = "启用视觉功能", Width = 150, Height = 32, Checked = true };
        var badges = new AntdUI.Checkbox { Text = "英雄评级", Width = 130, Height = 32, Checked = true };
        var players = new AntdUI.Checkbox { Text = "开黑 / KDA / 胜率", Width = 200, Height = 32, Checked = true };
        var counters = new AntdUI.Checkbox { Text = "英雄克制", Width = 130, Height = 32, Checked = true };
        var navigation = new AntdUI.Checkbox { Text = "隐藏客户端导航", Width = 180, Height = 32 };
        var wallpaperEnabled = new AntdUI.Checkbox { Text = "启用壁纸", Width = 150, Height = 32, Checked = true };
        var optimized = new AntdUI.Checkbox { Text = "客户端优化", Width = 150, Height = 32 };
        var lowSpec = new AntdUI.Checkbox { Text = "低配模式", Width = 130, Height = 32 };
        var silent = new AntdUI.Checkbox { Text = "静默模式", Width = 130, Height = 32 };
        var riotApi = new AntdUI.Checkbox { Text = "Riot Client API", Width = 180, Height = 32 };
        var insecure = new AntdUI.Checkbox { Text = "不安全模式", Width = 150, Height = 32 };
        Tip(optimized, "调整客户端浏览器的后台调度，关闭部分统计、组件更新等后台功能。实际效果因设备而异。\n自动保存，重启 LOL 客户端后生效。");
        Tip(lowSpec, "减少客户端界面过渡动画，关闭平滑滚动，并开启客户端低配设置，适合希望减少界面特效的设备。\n自动保存，重启 LOL 客户端后生效。");
        Tip(silent, "抑制客户端自动激活和置顶，减少窗口抢占焦点；对局结束后可能需要手动切回客户端。此选项用于窗口行为，不控制音量。\n自动保存，重启 LOL 客户端后生效。");
        bool loadingSettings = true;
        bool updatingImage = false;
        var saveDelay = new System.Windows.Forms.Timer { Interval = 600 };
        var mode = Select(180);
        AddSelectItems(mode, "自动识别", "峡谷 / 排位", "极地大乱斗", "斗魂竞技场", "无限火力");
        mode.SelectedIndex = 0;
        var wallpaper = Input("图片直链，或选择本地图片", 620, "");
        var imageName = Caption("未设置壁纸", 620);
        string selectedName = "";
        string refreshNonce = "";
        void Run(Action action, string message)
        {
            try { action(); status.Text = plugin.Inspect().Message; SetStatus(message, true); }
            catch (Exception error) { status.Text = error.Message; SetStatus(error.Message, false); }
        }
        PenguVisualSettings Values() => new()
        {
            Enabled = enabled.Checked, ShowBadges = badges.Checked, ShowPlayerStats = players.Checked, ShowCounters = counters.Checked,
            HideNavigation = navigation.Checked, WallpaperEnabled = wallpaperEnabled.Checked, TierMode = new[] { "auto", "ranked", "aram", "arena", "urf" }[Math.Clamp(mode.SelectedIndex, 0, 4)],
            WallpaperUrl = wallpaper.Text, WallpaperName = selectedName, RefreshNonce = refreshNonce
        };
        void Install()
        {
            var prepared = loader.Prepare();
            plugin.SelectDirectory(prepared.PluginsDirectory);
            plugin.Install();
            plugin.SetEnabled(true);
        }
        void ReadSettings()
        {
            bool previousLoading = loadingSettings;
            loadingSettings = true;
            try {
            var settings = settingsService.Read();
            refreshNonce = settings.RefreshNonce;
            enabled.Checked = settings.Enabled; badges.Checked = settings.ShowBadges;
            players.Checked = settings.ShowPlayerStats; counters.Checked = settings.ShowCounters; navigation.Checked = settings.HideNavigation;
            wallpaperEnabled.Checked = settings.WallpaperEnabled;
            mode.SelectedIndex = Array.IndexOf(new[] { "auto", "ranked", "aram", "arena", "urf" }, settings.TierMode);
            wallpaper.Text = settings.WallpaperUrl; selectedName = settings.WallpaperName;
            imageName.Text = selectedName.Length > 0 ? selectedName : "";
            var native = clientOptions.Read();
            optimized.Checked = native.OptimizedClient; lowSpec.Checked = native.LowSpecMode;
            silent.Checked = native.SilentMode; riotApi.Checked = native.RiotClientApi; insecure.Checked = native.InsecureMode;
            } finally { loadingSettings = previousLoading; }
        }
        void SaveNow()
        {
            saveDelay.Stop();
            if (loadingSettings || updatingImage) return;
            Run(() => { Install(); settingsService.Save(Values()); }, "视觉设置已更新。");
        }

        bool installing = false;
        body.Controls.Add(Button("从 Gitee 安装 / 更新视觉组件", async (_, _) =>
        {
            if (installing) return;
            installing = true;
            status.Text = "正在从 Gitee 下载并校验视觉组件…";
            try
            {
                await loader.InstallFromGiteeAsync();
                if (IsDisposed) return;
                Run(() => { Install(); ReadSettings(); }, "Gitee 视觉组件已安装，请重新激活客户端加载并重启 LOL 客户端。");
            }
            catch (Exception error) { if (!IsDisposed) { status.Text = error.Message; SetStatus(error.Message, false); } }
            finally { installing = false; }
        }));
        body.Controls.Add(Button("Gitee 下载页面", (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PenguReleaseInstaller.ReleasePageUrl) { UseShellExecute = true })));
        body.Controls.Add(Button("激活客户端加载", (_, _) => Run(() => { Install(); settingsService.Save(Values()); loader.SetActivated(true); }, "已激活，请重启 LOL 客户端。此后直接从本软件控制视觉功能。")));
        body.Controls.Add(Button("停用客户端加载", (_, _) => Run(() => loader.SetActivated(false), "已停用加载入口，下次启动客户端生效。")));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(enabled); body.Controls.Add(badges); body.Controls.Add(players); body.Controls.Add(counters);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(navigation); body.Controls.Add(Caption("评级模式", 90)); body.Controls.Add(mode);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(optimized); body.Controls.Add(lowSpec); body.Controls.Add(silent);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(riotApi); body.Controls.Add(insecure);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Caption("以上五项自动保存，重启 LOL 客户端生效；不安全模式会关闭网页安全限制。", 650));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(wallpaperEnabled);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(wallpaper);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Button("选择本地图片", (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "选择客户端壁纸", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*" };
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            Run(() =>
            {
                Install();
                var settings = settingsService.ImportWallpaper(picker.FileName, Values());
                updatingImage = true;
                try { wallpaper.Text = settings.WallpaperUrl; selectedName = settings.WallpaperName; wallpaperEnabled.Checked = true; }
                finally { updatingImage = false; }
                imageName.Text = selectedName;
                settingsService.Save(Values());
            }, "壁纸已更新。");
        }));
        body.Controls.Add(Button("清除壁纸", (_, _) =>
        {
            updatingImage = true;
            try { wallpaper.Text = ""; selectedName = ""; imageName.Text = ""; }
            finally { updatingImage = false; }
            SaveNow();
        }));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Button("刷新视觉数据", (_, _) => Run(() =>
        {
            Install(); refreshNonce = Guid.NewGuid().ToString("N"); settingsService.Save(Values());
        }, "已发送刷新请求，客户端加载后会重新获取数据并重试壁纸。")));
        body.Controls.Add(Button("恢复默认视觉", (_, _) => Run(() => { Install(); settingsService.Save(new()); ReadSettings(); }, "已恢复默认视觉设置。")));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(imageName);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Button("检查加载状态", (_, _) => Run(() =>
        {
            if (checkWindow is { IsDisposed: false }) { checkWindow.Activate(); return; }
            checkWindow = new PenguPluginStatusForm(plugin, message => { if (!status.IsDisposed) status.Text = message; });
            checkWindow.Show(FindForm());
        }, "正在检查视觉组件加载状态。")));
        body.Controls.Add(Button("选择本地组件 / 源码目录", (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "选择 PenguLoader 源码根目录", SelectedPath = loader.SourceDirectory };
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            Run(() => { loader.SelectDirectory(picker.SelectedPath); Install(); ReadSettings(); }, "已切换本地视觉内核，请重新激活并重启客户端。");
        }));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(status);
        try { ReadSettings(); status.Text = plugin.Inspect().Message; }
        catch (Exception error) { status.Text = error.Message; }
        loadingSettings = false;
        foreach (var option in new[] { enabled, badges, players, counters, navigation, wallpaperEnabled }) option.CheckedChanged += (_, _) => SaveNow();
        foreach (var option in new[] { optimized, lowSpec, silent, riotApi, insecure }) option.CheckedChanged += (_, _) =>
        {
            if (loadingSettings) return;
            try
            {
                clientOptions.Save(new() { OptimizedClient = optimized.Checked, LowSpecMode = lowSpec.Checked,
                    SilentMode = silent.Checked, RiotClientApi = riotApi.Checked, InsecureMode = insecure.Checked });
                SetStatus("客户端选项已保存，重启 LOL 客户端后生效。", true);
            }
            catch (Exception error)
            {
                SetStatus(error.Message, false);
                try { ReadSettings(); } catch { /* Keep the save error visible if the source is unavailable. */ }
            }
        };
        mode.SelectedIndexChanged += (_, _) => SaveNow();
        wallpaper.TextChanged += (_, _) =>
        {
            if (loadingSettings || updatingImage) return;
            selectedName = ""; imageName.Text = "";
            saveDelay.Stop(); saveDelay.Start();
        };
        saveDelay.Tick += (_, _) => SaveNow();
        wallpaper.Leave += (_, _) => { if (saveDelay.Enabled) SaveNow(); };
        var refresh = new System.Windows.Forms.Timer { Interval = 3000 };
        refresh.Tick += (_, _) => { if (card.Visible) { try { status.Text = plugin.Inspect().Message; } catch (Exception error) { status.Text = error.Message; } } };
        refresh.Start();
        card.Disposed += (_, _) => { refresh.Dispose(); saveDelay.Dispose(); if (checkWindow is { IsDisposed: false }) checkWindow.Close(); };
        return card;
    }
}
