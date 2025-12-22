namespace LOL_GameAssistant.BaseViewForm
{
    partial class AboutForm
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new AntdUI.Label();
            button1 = new AntdUI.Button();
            btn_github = new AntdUI.Button();
            btn_update = new AntdUI.Button();
            alert1 = new AntdUI.Alert();
            SuspendLayout();
            // 
            // label1
            // 
            label1.Font = new Font("Microsoft YaHei UI", 21.75F, FontStyle.Bold, GraphicsUnit.Point, 134);
            label1.Location = new Point(256, 16);
            label1.Name = "label1";
            label1.Size = new Size(94, 41);
            label1.TabIndex = 0;
            label1.Text = "关于";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // button1
            // 
            button1.Location = new Point(91, 550);
            button1.Name = "button1";
            button1.Size = new Size(121, 47);
            button1.TabIndex = 1;
            button1.Text = "开源项目";
            button1.Type = AntdUI.TTypeMini.Success;
            // 
            // btn_github
            // 
            btn_github.Location = new Point(242, 550);
            btn_github.Name = "btn_github";
            btn_github.Size = new Size(121, 47);
            btn_github.TabIndex = 2;
            btn_github.Text = "给项目点个赞";
            btn_github.Type = AntdUI.TTypeMini.Primary;
            // 
            // btn_update
            // 
            btn_update.Location = new Point(409, 550);
            btn_update.Name = "btn_update";
            btn_update.Size = new Size(121, 47);
            btn_update.TabIndex = 3;
            btn_update.Text = "软件更新";
            btn_update.Type = AntdUI.TTypeMini.Warn;
            // 
            // alert1
            // 
            alert1.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point, 134);
            alert1.Location = new Point(37, 63);
            alert1.Name = "alert1";
            alert1.Size = new Size(543, 466);
            alert1.TabIndex = 4;
            alert1.Text = "  一款好用英雄联盟助手，";
            alert1.TextAlign = ContentAlignment.TopLeft;
            // 
            // AboutForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(alert1);
            Controls.Add(btn_update);
            Controls.Add(btn_github);
            Controls.Add(button1);
            Controls.Add(label1);
            Name = "AboutForm";
            Size = new Size(623, 621);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Label label1;
        private AntdUI.Button button1;
        private AntdUI.Button btn_github;
        private AntdUI.Button btn_update;
        private AntdUI.Alert alert1;
    }
}
