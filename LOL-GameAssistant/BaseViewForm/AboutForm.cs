using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class AboutForm : UserControl
    {
        public AboutForm()
        {
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
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "LOL-GameAssistant");
            http.Timeout = TimeSpan.FromSeconds(10);

            string json = await http.GetStringAsync("https://api.github.com/repos/shecong/LOL-GameAssistant/releases/latest");
            var data = JObject.Parse(json);

            string? tagName = data["tag_name"]?.ToString();
            string? htmlUrl = data["html_url"]?.ToString();
            string? releaseBody = data["body"]?.ToString();

            if (string.IsNullOrEmpty(tagName))
            {
                AntdUI.Message.warn(ParentForm!, "未获取到版本信息");
                return;
            }

            string versionStr = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(versionStr, out var latestVer))
            {
                AntdUI.Message.warn(ParentForm!, $"无法解析版本号: {tagName}");
                return;
            }

            var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            if (currentVer != null && latestVer > currentVer)
            {
                string msg = $"发现新版本 {tagName}！\n\n"
                           + $"当前版本: {currentVer.Major}.{currentVer.Minor}.{currentVer.Build}\n"
                           + $"最新版本: {tagName}\n";

                if (!string.IsNullOrEmpty(releaseBody))
                {
                    msg += $"\n更新内容:\n{releaseBody}";
                }

                AntdUI.Message.info(ParentForm!, msg);
                OpenUrl(htmlUrl ?? "https://github.com/shecong/LOL-GameAssistant/releases");
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