using System.Diagnostics;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Bootstrap;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class ClientToolsForm
{
    private Control CreatePenguPluginCard()
    {
        IPenguPluginService plugin = _penguPlugin;
        IPenguLoaderDownloadService loaderDownload = AppCompositionRoot.PenguLoaderDownload;
        CancellationTokenSource? downloading = null;
        PenguPluginStatusForm? checkWindow = null;
        var card = Card("客户端视觉插件（Pengu）", 240, out FlowLayoutPanel body);
        var path = Input("选择 Pengu Loader 的 plugins 文件夹", 450, plugin.PluginsDirectory);
        path.ReadOnly = true;
        var status = Caption("", 650);
        status.Height = 90;
        void Refresh()
        {
            try { status.Text = plugin.Inspect().Message; }
            catch (Exception error) { status.Text = error.Message; }
        }
        void Run(Action action, string message)
        {
            try { action(); Refresh(); SetStatus(message, true); }
            catch (Exception error) { status.Text = error.Message; SetStatus(error.Message, false); }
        }
        body.Controls.Add(Caption("可直接下载官方稳定版并安装插件，再打开 Loader 激活。重启 LOL 客户端后，检查右下角 ✦ 确认插件显示。", 650));
        body.SetFlowBreak(body.Controls[^1], true);
        var downloadButton = Button("下载 Loader 并安装插件", async (_, _) =>
        {
            if (downloading != null) return;
            using var source = new CancellationTokenSource();
            downloading = source;
            foreach (AntdUI.Button button in body.Controls.OfType<AntdUI.Button>()) button.Enabled = button.Name == "cancelPenguDownload";
            try
            {
                var progress = new Progress<PenguLoaderDownloadProgress>(value =>
                {
                    if (!status.IsDisposed) status.Text = value.Total > 0
                        ? $"{value.Message} · {value.Downloaded * 100 / value.Total}%" : value.Message;
                });
                PreparedPenguLoader loader = await loaderDownload.DownloadAsync(progress, source.Token);
                if (card.IsDisposed) return;
                source.Token.ThrowIfCancellationRequested();
                plugin.SelectDirectory(loader.PluginsDirectory);
                plugin.Install();
                path.Text = plugin.PluginsDirectory;
                status.Text = $"已下载 {loader.Version} 并安装视觉插件。点击“打开 Loader”启用开关，然后完整重启 LOL 客户端。\n{loader.ExecutablePath}";
                SetStatus("Pengu Loader 与插件已准备完成。", true);
            }
            catch (OperationCanceledException) { if (!status.IsDisposed) status.Text = "下载已取消，可重新下载。"; }
            catch (Exception error) { if (!status.IsDisposed) { status.Text = error.Message; SetStatus(error.Message, false); } }
            finally
            {
                downloading = null;
                if (!body.IsDisposed)
                    foreach (AntdUI.Button button in body.Controls.OfType<AntdUI.Button>()) button.Enabled = button.Name != "cancelPenguDownload";
            }
        });
        var cancelButton = Button("取消下载", (_, _) => downloading?.Cancel());
        cancelButton.Name = "cancelPenguDownload";
        cancelButton.Enabled = false;
        body.Controls.Add(downloadButton);
        body.Controls.Add(cancelButton);
        body.Controls.Add(Button("打开 Loader", (_, _) => Run(() =>
        {
            string? loaderDirectory = string.IsNullOrEmpty(plugin.PluginsDirectory) ? null : Path.GetDirectoryName(plugin.PluginsDirectory);
            string? executable = loaderDirectory == null ? null : new[] { "Pengu Loader.exe", "PenguLoader.exe", "pengu-loader.exe" }
                .Select(name => Path.Combine(loaderDirectory, name)).FirstOrDefault(File.Exists);
            executable ??= loaderDownload.GetInstalled()?.ExecutablePath;
            if (executable == null) throw new InvalidOperationException("请先下载 Loader，或选择已有 Loader 的 plugins 文件夹。");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable) });
        }, "已打开 Pengu Loader，请在 Loader 内启用开关后重启 LOL 客户端。")));
        card.Disposed += (_, _) => { downloading?.Cancel(); if (checkWindow is { IsDisposed: false }) checkWindow.Close(); };
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(path);
        body.Controls.Add(Button("选择目录", (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "选择 Pengu Loader 的 plugins 文件夹", UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (Directory.Exists(plugin.PluginsDirectory)) picker.SelectedPath = plugin.PluginsDirectory;
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            Run(() => { plugin.SelectDirectory(picker.SelectedPath); path.Text = plugin.PluginsDirectory; }, "已保存插件目录。");
        }));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(Button("安装 / 更新插件", (_, _) => Run(plugin.Install, "插件文件已安装。请重启 LOL 客户端后检查右下角 ✦。")));
        body.Controls.Add(Button("启用", (_, _) => Run(() => plugin.SetEnabled(true), "已启用，下次启动客户端生效。")));
        body.Controls.Add(Button("停用", (_, _) => Run(() => plugin.SetEnabled(false), "已停用，下次启动客户端生效。")));
        body.Controls.Add(Button("卸载插件", (_, _) => Run(plugin.Uninstall, "已移除插件文件。请重启客户端以清除当前面板；客户端保存的视觉偏好仍保留。")));
        body.Controls.Add(Button("检查加载状态", (_, _) =>
        {
            if (checkWindow is { IsDisposed: false }) { checkWindow.Activate(); return; }
            SetStatus("正在检查插件加载状态…", true);
            checkWindow = new PenguPluginStatusForm(plugin, message =>
            {
                if (status.IsDisposed) return;
                status.Text = message;
                SetStatus("插件检查已完成，详细结果见“插件加载检查”窗口。", true);
            });
            checkWindow.Show(FindForm());
        }));
        body.Controls.Add(Button("打开插件目录", (_, _) => Run(() =>
        {
            if (!Directory.Exists(plugin.PluginsDirectory)) throw new InvalidOperationException("请先选择插件目录。");
            Process.Start(new ProcessStartInfo(plugin.PluginsDirectory) { UseShellExecute = true });
        }, "已打开插件目录。")));
        body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(status);
        Refresh();
        return card;
    }
}
