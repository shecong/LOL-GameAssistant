namespace LOL_GameAssistant.BaseViewForm
{
    partial class AboutForm
    {
        private System.ComponentModel.IContainer components = new System.ComponentModel.Container();
        private AntdUI.Label lblVersion = null!;
        private AntdUI.Button btn_opengithub = null!;
        private AntdUI.Button btn_github = null!;
        private AntdUI.Button btn_update = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;
            Name = "AboutForm";
            Size = new Size(900, 640);
            lblVersion = new AntdUI.Label { AutoSize = true, Text = "版本", Padding = new Padding(0, 8, 20, 0) };
            btn_opengithub = new AntdUI.Button { Text = "开源项目", AutoSize = true, Height = 36 };
            btn_github = new AntdUI.Button { Text = "给项目点个赞", AutoSize = true, Height = 36, Type = AntdUI.TTypeMini.Primary };
            btn_update = new AntdUI.Button { Text = "软件更新", AutoSize = true, Height = 36 };
            btn_opengithub.Click += btn_opengithub_Click;
            btn_github.Click += btn_github_Click;
            btn_update.Click += btn_update_Click;
        }
    }
}
