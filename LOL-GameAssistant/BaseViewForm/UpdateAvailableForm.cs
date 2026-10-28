using Panel = AntdUI.Panel;
using ComboBox = AntdUI.Select;
using TextBox = AntdUI.Input;
using ProgressBar = AntdUI.Progress;
using System.Diagnostics;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>非模态更新提示：用户可选择下载、加入 QQ 群，或继续使用当前版本。</summary>
internal sealed class UpdateAvailableForm : AntdUI.Window
{
    private readonly CancellationTokenSource _downloadLifetime = new();
    private PreparedUpdate? _prepared;
    private bool _downloading;
    private bool _installing;
    internal const string QqGroupNumber = "1067459719";
    internal const string QqGroupUrl = "mqqapi://card/show_pslcard?src_type=internal&version=1&uin="
        + QqGroupNumber + "&card_type=group&source=qrcode";

    internal UpdateAvailableForm(UpdateRelease release, Action<string>? openUrl = null, UpdatePackageService? packageService = null)
    {
        packageService ??= new UpdatePackageService();
        Text = UiLanguage.IsEnglish ? "Update available" : "发现新版本";
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(680, 440);
        MinimumSize = new Size(580, 360);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font(UiMetrics.FontFamily, 10);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new AntdUI.Label
        {
            Dock = DockStyle.Fill, AutoSize = true, TextMultiLine = true,
            Text = UiLanguage.IsEnglish
                ? $"New version: {release.TagName} (current: {AppVersion.Current.ToString(3)})\nUpdating is optional. Choose a download source or QQ group {QqGroupNumber}."
                : $"发现新版本 {release.TagName}，当前版本 {AppVersion.Current.ToString(3)}\n可选择下载源，或加入 QQ 群 {QqGroupNumber} 获取。更新可稍后进行。"
        }, 0, 0);
        content.Controls.Add(new TextBox
        {
            Name = "UpdateReleaseNotes", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            AutoScroll = true, Margin = new Padding(0, 12, 0, 12),
            Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
                ? (UiLanguage.IsEnglish ? "No release notes provided." : "此版本未提供更新说明。")
                : release.ReleaseNotes.ReplaceLineEndings(Environment.NewLine)
        }, 0, 1);
        var transfer = new Panel { Dock = DockStyle.Fill };
        var transferStatus = new AntdUI.Label
        {
            Name = "UpdateDownloadStatus", Dock = DockStyle.Fill, TextMultiLine = true,
            Text = UiLanguage.IsEnglish ? "Download first, then choose when to restart and install."
                : "先下载并校验，完成后可选择重启安装；下载期间可继续使用。"
        };
        var transferProgress = new ProgressBar { Dock = DockStyle.Bottom, Height = 12 };
        transfer.Controls.Add(transferStatus);
        transfer.Controls.Add(transferProgress);
        content.Controls.Add(transfer, 0, 2);
        var sourceRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 8) };
        sourceRow.Controls.Add(new AntdUI.Label
        {
            Text = UiLanguage.IsEnglish ? "Download source:" : "下载源：", AutoSize = true, Margin = new Padding(0, 6, 8, 0)
        });
        var source = new ComboBox { Name = "UpdateDownloadSource", List = true, DropDownArrow = true, Width = 190 };
        if (release.ServerManifestUrl != null) source.Items.Add(UiLanguage.IsEnglish ? "Server download" : "服务器下载");
        source.Items.Add(UiLanguage.IsEnglish ? "GitHub download" : "GitHub 下载");
        source.SelectedIndex = release.ServerManifestUrl != null && !release.PreferServerDownload ? 1 : 0;
        sourceRow.Controls.Add(source);
        content.Controls.Add(sourceRow, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var autoUpdate = new AntdUI.Button { Name = "DownloadAndInstallUpdate", Text = UiLanguage.IsEnglish ? "Download update" : "下载更新", AutoSize = true, Height = 40, Type = AntdUI.TTypeMini.Primary, Enabled = false };
        var download = new AntdUI.Button { Name = "DownloadUpdate", Text = UiLanguage.IsEnglish ? "GitHub download" : "GitHub 下载", AutoSize = true, Height = 40 };
        var group = new AntdUI.Button { Name = "GetUpdateFromQq", Text = UiLanguage.IsEnglish ? "Get via QQ group" : "QQ群获取", AutoSize = true, Height = 40 };
        var later = new AntdUI.Button { Name = "UpdateLater", Text = UiLanguage.IsEnglish ? "Later" : "稍后再说", AutoSize = true, Height = 40 };
        download.Click += (_, _) => Open(release.ReleaseUrl);
        group.Click += (_, _) => Open(QqGroupUrl);
        later.Click += (_, _) => Close();
        actions.Controls.AddRange([autoUpdate, download, group, later]);
        content.Controls.Add(actions, 0, 4);
        Controls.Add(content);
        AntdWindowChrome.AddHeader(this);
        CancelButton = later;
        UiTheme.Apply(this);
        FormClosing += (_, e) =>
        {
            if (_installing) { e.Cancel = true; return; }
            _downloadLifetime.Cancel();
        };
        Disposed += (_, _) => _downloadLifetime.Cancel();
        Shown += async (_, _) =>
        {
            autoUpdate.Enabled = source.Enabled = false;
            transferStatus.Text = UiLanguage.IsEnglish ? "Checking downloaded update…" : "正在检查已下载的更新包…";
            try
            {
                _prepared = await packageService.TryRestoreAsync(release, null, _downloadLifetime.Token);
                if (IsDisposed) return;
                if (_prepared != null) ShowPrepared();
                else transferStatus.Text = UiLanguage.IsEnglish ? "Download first, then choose when to restart and install."
                    : "先下载并校验，完成后可选择重启安装；已完成的下载会保留。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                RuntimeDiagnostics.WriteException(ex);
                if (!IsDisposed) transferStatus.Text = "未能恢复本地更新包，可重新下载：" + ex.Message;
            }
            finally
            {
                if (!IsDisposed) { autoUpdate.Enabled = true; source.Enabled = _prepared == null; }
            }
        };
        autoUpdate.Click += async (_, _) =>
        {
            if (_downloading) { _downloadLifetime.Cancel(); return; }
            if (_installing) return;
            try
            {
                if (_prepared == null)
                {
                    _downloading = true;
                    source.Enabled = false;
                    autoUpdate.Text = UiLanguage.IsEnglish ? "Cancel download" : "取消下载";
                    var progress = new Progress<UpdateDownloadProgress>(value =>
                    {
                        if (IsDisposed) return;
                        transferStatus.Text = $"{value.Stage}  {value.Percent}%";
                        transferProgress.Value = Math.Clamp(value.Percent / 100F, 0, 1);
                    });
                    bool useServer = release.ServerManifestUrl != null && source.SelectedIndex == 0;
                    _prepared = await packageService.PrepareAsync(release, progress, _downloadLifetime.Token, useServer);
                    if (IsDisposed) return;
                    ShowPrepared();
                    return;
                }
                if (Owner is GameMain main && main.gameFlowPhase is GameFlowPhase.InProgress or GameFlowPhase.Reconnect or GameFlowPhase.ChampSelect)
                {
                    transferStatus.Text = "当前正在选人或对局，请结束后再安装更新。";
                    return;
                }
                _installing = true;
                autoUpdate.Enabled = later.Enabled = false;
                transferStatus.Text = "正在启动更新器，请稍候…";
                await UpdateInstaller.LaunchAsync(_prepared);
                // Application.Exit 使用 ApplicationExitCall，不会触发“关闭时最小化到托盘”。
                _installing = false;
                System.Windows.Forms.Application.Exit();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) transferStatus.Text = "下载已取消，当前版本不受影响。关闭此窗口后可重新下载。";
                if (!IsDisposed) autoUpdate.Enabled = false;
            }
            catch (Exception ex)
            {
                RuntimeDiagnostics.WriteException(ex);
                if (!IsDisposed)
                {
                    transferStatus.Text = "更新未完成：" + ex.Message;
                    autoUpdate.Text = _prepared == null ? "重试下载" : "重试安装";
                    autoUpdate.Enabled = later.Enabled = true;
                    source.Enabled = _prepared == null;
                }
            }
            finally { _downloading = false; _installing = false; }
        };

        void ShowPrepared()
        {
            transferStatus.Text = UiLanguage.IsEnglish ? "Update downloaded. Restart and install now, or close this window and install later."
                : "更新包已下载并校验，可重启安装；关闭窗口或重启助手后仍可继续安装，无需重复下载。";
            transferProgress.Value = 1;
            autoUpdate.Text = UiLanguage.IsEnglish ? "Restart and install" : "重启并安装";
            source.Enabled = false;
        }

        void Open(string url)
        {
            try
            {
                if (openUrl != null) openUrl(url);
                else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UiMessage.warn(this, $"无法打开链接，可在浏览器访问项目发布页，或搜索 QQ 群 {QqGroupNumber} 获取更新。\n{ex.Message}");
            }
        }
    }

    /// <summary>非模态窗口显式按主窗口居中，并避免超出当前屏幕工作区。</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Owner is { IsDisposed: false } owner)
        {
            Location = new Point(owner.Left + (owner.Width - Width) / 2,
                owner.Top + (owner.Height - Height) / 2);
            WindowLayout.FitToWorkingArea(this);
        }
    }
}
