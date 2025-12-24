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
            btn_opengithub = new AntdUI.Button();
            btn_github = new AntdUI.Button();
            btn_update = new AntdUI.Button();
            alert1 = new AntdUI.Alert();
            gridPanel1 = new AntdUI.GridPanel();
            gridPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // label1
            // 
            label1.Font = new Font("Microsoft YaHei UI", 21.75F, FontStyle.Bold, GraphicsUnit.Point, 134);
            gridPanel1.SetIndex(label1, 1);
            label1.Location = new Point(3, 3);
            label1.Name = "label1";
            label1.Size = new Size(617, 56);
            label1.TabIndex = 0;
            label1.Text = "关于";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // btn_opengithub
            // 
            gridPanel1.SetIndex(btn_opengithub, 3);
            btn_opengithub.Location = new Point(3, 500);
            btn_opengithub.Name = "btn_opengithub";
            btn_opengithub.Size = new Size(200, 118);
            btn_opengithub.TabIndex = 1;
            btn_opengithub.Text = "开源项目";
            btn_opengithub.Type = AntdUI.TTypeMini.Success;
            btn_opengithub.Click += btn_opengithub_Click;
            // 
            // btn_github
            // 
            gridPanel1.SetIndex(btn_github, 4);
            btn_github.Location = new Point(209, 500);
            btn_github.Name = "btn_github";
            btn_github.Size = new Size(200, 118);
            btn_github.TabIndex = 2;
            btn_github.Text = "给项目点个赞";
            btn_github.Type = AntdUI.TTypeMini.Primary;
            // 
            // btn_update
            // 
            gridPanel1.SetIndex(btn_update, 5);
            btn_update.Location = new Point(414, 500);
            btn_update.Name = "btn_update";
            btn_update.Size = new Size(200, 118);
            btn_update.TabIndex = 3;
            btn_update.Text = "软件更新";
            btn_update.Type = AntdUI.TTypeMini.Warn;
            btn_update.Click += btn_update_Click;
            // 
            // alert1
            // 
            alert1.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point, 134);
            gridPanel1.SetIndex(alert1, 2);
            alert1.Location = new Point(3, 65);
            alert1.Loop = true;
            alert1.Name = "alert1";
            alert1.Size = new Size(617, 429);
            alert1.TabIndex = 4;
            alert1.Text = "  一款好用英雄联盟助手，支持查看战绩，对局记录，自动匹配对局等功能，后续功能持续增加中，敬请期待！";
            alert1.TextAlign = ContentAlignment.TopLeft;
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(label1);
            gridPanel1.Controls.Add(btn_update);
            gridPanel1.Controls.Add(alert1);
            gridPanel1.Controls.Add(btn_github);
            gridPanel1.Controls.Add(btn_opengithub);
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(623, 621);
            gridPanel1.Span = "100%;100%;33% 33% 33%;-10% 70% 20%";
            gridPanel1.TabIndex = 5;
            gridPanel1.Text = "gridPanel1";
            // 
            // AboutForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(gridPanel1);
            DoubleBuffered = true;
            Name = "AboutForm";
            Size = new Size(623, 621);
            gridPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Label label1;
        private AntdUI.Button btn_opengithub;
        private AntdUI.Button btn_github;
        private AntdUI.Button btn_update;
        private AntdUI.Alert alert1;
        private AntdUI.GridPanel gridPanel1;
    }
}
