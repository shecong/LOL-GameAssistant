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
        PenguPluginStatusForm? checkWindow = null;
        var card = Card("客户端视觉插件（Pengu）", 490, out FlowLayoutPanel body);
        var status = Caption("", 650);
        status.Height = 80;
        var enabled = new AntdUI.Checkbox { Text = "启用视觉功能", Width = 150, Height = 32, Checked = true };
        var badges = new AntdUI.Checkbox { Text = "英雄评级", Width = 130, Height = 32, Checked = true };
        var players = new AntdUI.Checkbox { Text = "开黑 / KDA / 胜率", Width = 200, Height = 32, Checked = true };
        var navigation = new AntdUI.Checkbox { Text = "隐藏客户端导航", Width = 180, Height = 32 };
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
            Enabled = enabled.Checked, ShowBadges = badges.Checked, ShowPlayerStats = players.Checked,
            HideNavigation = navigation.Checked, TierMode = new[] { "auto", "ranked", "aram", "arena", "urf" }[Math.Clamp(mode.SelectedIndex, 0, 4)],
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
            var settings = settingsService.Read();
            refreshNonce = settings.RefreshNonce;
            enabled.Checked = settings.Enabled; badges.Checked = settings.ShowBadges;
            players.Checked = settings.ShowPlayerStats; navigation.Checked = settings.HideNavigation;
            mode.SelectedIndex = Array.IndexOf(new[] { "auto", "ranked", "aram", "arena", "urf" }, settings.TierMode);
            wallpaper.Text = settings.WallpaperUrl; selectedName = settings.WallpaperName;
            imageName.Text = selectedName.Length > 0 ? selectedName : "未选择本地图片";
        }
        body.Controls.Add(Caption("由 sc助手直接控制，无需启动 Pengu 管理窗口。首次接入或更新 core.dll 后重启 LOL 客户端；设置在客户端加载后约 1 秒生效。", 650));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Button("安装 / 更新视觉组件", (_, _) => Run(Install, "已安装视觉组件，请重新启动 LOL 客户端加载新版本。")));
        body.Controls.Add(Button("激活客户端加载", (_, _) => Run(() => { Install(); settingsService.Save(Values()); loader.SetActivated(true); }, "已激活，请重启 LOL 客户端。此后直接从本软件控制视觉功能。")));
        body.Controls.Add(Button("停用客户端加载", (_, _) => Run(() => loader.SetActivated(false), "已停用加载入口，下次启动客户端生效。")));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(enabled); body.Controls.Add(badges); body.Controls.Add(players);
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(navigation); body.Controls.Add(Caption("评级模式", 90)); body.Controls.Add(mode);
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
                wallpaper.Text = settings.WallpaperUrl; selectedName = settings.WallpaperName;
                imageName.Text = selectedName + "（点击应用设置生效）";
            }, "本地图片已复制，点击应用设置生效。");
        }));
        body.Controls.Add(Button("清除壁纸", (_, _) => { wallpaper.Text = ""; selectedName = ""; imageName.Text = "壁纸已清除（点击应用设置生效）"; }));
        body.Controls.Add(Button("应用设置", (_, _) => Run(() => { Install(); settingsService.Save(Values()); }, "设置已保存。已加载新视觉组件的客户端将在约 1 秒内应用。")));
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
        body.Controls.Add(Button("选择本地源码目录", (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "选择 PenguLoader 源码根目录", SelectedPath = loader.SourceDirectory };
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            Run(() => { loader.SelectDirectory(picker.SelectedPath); Install(); ReadSettings(); }, "已切换本地视觉内核，请重新激活并重启客户端。");
        }));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(status);
        try { ReadSettings(); status.Text = plugin.Inspect().Message; }
        catch (Exception error) { status.Text = error.Message; }
        var refresh = new System.Windows.Forms.Timer { Interval = 3000 };
        refresh.Tick += (_, _) => { if (card.Visible) { try { status.Text = plugin.Inspect().Message; } catch (Exception error) { status.Text = error.Message; } } };
        refresh.Start();
        card.Disposed += (_, _) => { refresh.Dispose(); if (checkWindow is { IsDisposed: false }) checkWindow.Close(); };
        return card;
    }
}
