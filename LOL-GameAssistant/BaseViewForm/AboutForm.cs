using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Helper;
using System.Diagnostics;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>关于页：展示项目说明、版本更新和外部数据来源。</summary>
    public partial class AboutForm : UserControl, IThemeAware
    {
        private readonly IUpdateReleaseService _updateReleaseService;
        private readonly Action<string> _openUrl;

        /// <summary>初始化 AboutForm 的实例状态。</summary>
        public AboutForm() : this(AppCompositionRoot.UpdateReleaseService)
        {
        }

        /// <summary>关于页仅通过应用端口查询发布版本。</summary>
        internal AboutForm(IUpdateReleaseService updateReleaseService, Action<string>? openUrl = null)
        {
            _updateReleaseService = updateReleaseService;
            _openUrl = openUrl ?? LaunchUrl;
            InitializeComponent();
            BuildAboutUi();
            this.Load += AboutForm_Load;
        }

        /// <summary>加载关于页所需的版本和展示信息。</summary>
        private void AboutForm_Load(object? sender, EventArgs e)
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null)
            {
                lblVersion.Text = $"版本: {ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }

        /// <summary>响应项目链接按钮，打开对应项目页面。</summary>
        private void btn_opengithub_Click(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/shecong/LOL-GameAssistant");
        }

        /// <summary>响应 GitHub 按钮，打开对应链接。</summary>
        private void btn_github_Click(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/shecong/LOL-GameAssistant");
        }

        /// <summary>响应更新检查按钮，查询可用版本。</summary>
        private async void btn_update_Click(object sender, EventArgs e)
        {
            btn_update.Enabled = false;
            btn_update.Text = "检查中...";

            try
            {
                await CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                LOL_GameAssistant.Helper.UiMessage.error(ParentForm!, $"检查更新失败: {ex.Message}");
            }
            finally
            {
                btn_update.Enabled = true;
                btn_update.Text = "软件更新";
            }
        }

        /// <summary>查询发布版本并将比较结果展示给用户。</summary>
        private async Task CheckForUpdateAsync()
        {
            UpdateRelease? release = await _updateReleaseService.GetLatestAsync();
            if (release == null)
            {
                LOL_GameAssistant.Helper.UiMessage.warn(ParentForm!, "未获取到版本信息");
                return;
            }

            var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            if (currentVer != null && release.Version > currentVer)
            {
                string msg = $"发现新版本 {release.TagName}！\n\n"
                           + $"当前版本: {currentVer.Major}.{currentVer.Minor}.{currentVer.Build}\n"
                           + $"最新版本: {release.TagName}\n";

                if (!string.IsNullOrEmpty(release.ReleaseNotes))
                {
                    msg += $"\n更新内容:\n{release.ReleaseNotes}";
                }

                LOL_GameAssistant.Helper.UiMessage.info(ParentForm!, msg);
                OpenUrl(release.ReleaseUrl);
            }
            else
            {
                LOL_GameAssistant.Helper.UiMessage.success(ParentForm!, $"当前已是最新版本 ({currentVer})");
            }
        }

        /// <summary>打开用户选择的网页链接。</summary>
        private void OpenUrl(string url) => _openUrl(url);

        /// <summary>通过系统默认处理程序启动网页地址。</summary>
        private static void LaunchUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
