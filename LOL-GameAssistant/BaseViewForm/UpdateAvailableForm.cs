using System.Diagnostics;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>非模态更新提示：用户可选择下载、加入 QQ 群，或继续使用当前版本。</summary>
internal sealed class UpdateAvailableForm : AntdUI.Window
{
    internal const string QqGroupNumber = "1067459719";
    internal const string QqGroupUrl = "mqqapi://card/show_pslcard?src_type=internal&version=1&uin="
        + QqGroupNumber + "&card_type=group&source=qrcode";

    internal UpdateAvailableForm(UpdateRelease release, Action<string>? openUrl = null)
    {
        Text = UiLanguage.IsEnglish ? "Update available" : "发现新版本";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 360);
        MinimumSize = new Size(580, 360);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font(UiMetrics.FontFamily, 10);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new AntdUI.Label
        {
            Dock = DockStyle.Fill, AutoSize = true, TextMultiLine = true,
            Text = UiLanguage.IsEnglish
                ? $"New version: {release.TagName} (current: {AppVersion.Current.ToString(3)})\nUpdating is optional. Download from GitHub or QQ group {QqGroupNumber}."
                : $"发现新版本 {release.TagName}，当前版本 {AppVersion.Current.ToString(3)}\n可前往 GitHub 下载，或加入 QQ 群 {QqGroupNumber} 获取。更新可稍后进行。"
        }, 0, 0);
        content.Controls.Add(new TextBox
        {
            Name = "UpdateReleaseNotes", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Margin = new Padding(0, 12, 0, 12),
            Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
                ? (UiLanguage.IsEnglish ? "No release notes provided." : "此版本未提供更新说明。")
                : release.ReleaseNotes.ReplaceLineEndings(Environment.NewLine)
        }, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var download = new AntdUI.Button { Name = "DownloadUpdate", Text = UiLanguage.IsEnglish ? "GitHub download" : "GitHub 下载", AutoSize = true, Height = 40 };
        var group = new AntdUI.Button { Name = "GetUpdateFromQq", Text = UiLanguage.IsEnglish ? "Get via QQ group" : "QQ群获取", AutoSize = true, Height = 40 };
        var later = new AntdUI.Button { Name = "UpdateLater", Text = UiLanguage.IsEnglish ? "Later" : "稍后再说", AutoSize = true, Height = 40 };
        download.Click += (_, _) => Open(release.ReleaseUrl);
        group.Click += (_, _) => Open(QqGroupUrl);
        later.Click += (_, _) => Close();
        actions.Controls.AddRange([download, group, later]);
        content.Controls.Add(actions, 0, 2);
        Controls.Add(content);
        AntdWindowChrome.AddHeader(this);
        CancelButton = later;
        UiTheme.Apply(this);

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
}
