using Newtonsoft.Json.Linq;
using System.Diagnostics;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class AboutForm : UserControl
    {
        private readonly IUpdateReleaseService _updateReleaseService;

        public AboutForm() : this(AppCompositionRoot.UpdateReleaseService)
        {
        }

        /// <summary>关于页仅通过应用端口查询发布版本。</summary>
        internal AboutForm(IUpdateReleaseService updateReleaseService)
        {
            _updateReleaseService = updateReleaseService;
            InitializeComponent();
            this.Load += AboutForm_Load;
        }

        private void AboutForm_Load(object? sender, EventArgs e)
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null)
            {
                lblVersion.Text = $"版本: {ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }

        private void btn_opengithub_Click(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/shecong/LOL-GameAssistant");
        }

        private void btn_github_Click(object sender, EventArgs e)
        {
            OpenUrl("https://github.com/shecong/LOL-GameAssistant");
        }

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
                AntdUI.Message.error(ParentForm!, $"检查更新失败: {ex.Message}");
            }
            finally
            {
                btn_update.Enabled = true;
                btn_update.Text = "软件更新";
            }
        }

        private async Task CheckForUpdateAsync()
        {
            UpdateRelease? release = await _updateReleaseService.GetLatestAsync();
            if (release == null)
            {
                AntdUI.Message.warn(ParentForm!, "未获取到版本信息");
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

                AntdUI.Message.info(ParentForm!, msg);
                OpenUrl(release.ReleaseUrl);
            }
            else
            {
                AntdUI.Message.success(ParentForm!, $"当前已是最新版本 ({currentVer})");
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
