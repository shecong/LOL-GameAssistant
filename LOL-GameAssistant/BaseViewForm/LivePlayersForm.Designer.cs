namespace LOL_GameAssistant.BaseViewForm
{
    partial class LivePlayersForm
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
            gridPanel1 = new AntdUI.GridPanel();
            gametype = new AntdUI.Label();
            name = new AntdUI.Label();
            gamedate = new AntdUI.Label();
            kda = new AntdUI.Label();
            iswin = new AntdUI.Label();
            gridPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(iswin);
            gridPanel1.Controls.Add(kda);
            gridPanel1.Controls.Add(gamedate);
            gridPanel1.Controls.Add(name);
            gridPanel1.Controls.Add(gametype);
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(372, 26);
            gridPanel1.Span = "19% 26% 26% 18% 10%";
            gridPanel1.TabIndex = 6;
            gridPanel1.Text = "gridPanel1";
            // 
            // gametype
            // 
            gridPanel1.SetIndex(gametype, 1);
            gametype.Location = new Point(3, 3);
            gametype.Name = "gametype";
            gametype.Size = new Size(65, 20);
            gametype.TabIndex = 0;
            gametype.Text = "模式";
            // 
            // name
            // 
            gridPanel1.SetIndex(name, 2);
            name.Location = new Point(74, 3);
            name.Name = "name";
            name.Size = new Size(91, 20);
            name.TabIndex = 1;
            name.Text = "英雄名";
            // 
            // gamedate
            // 
            gridPanel1.SetIndex(gamedate, 3);
            gamedate.Location = new Point(170, 3);
            gamedate.Name = "gamedate";
            gamedate.Size = new Size(91, 20);
            gamedate.TabIndex = 2;
            gamedate.Text = "日期";
            // 
            // kda
            // 
            gridPanel1.SetIndex(kda, 4);
            kda.Location = new Point(267, 3);
            kda.Name = "kda";
            kda.Size = new Size(61, 20);
            kda.TabIndex = 3;
            kda.Text = "kda";
            // 
            // iswin
            // 
            gridPanel1.SetIndex(iswin, 5);
            iswin.Location = new Point(334, 3);
            iswin.Name = "iswin";
            iswin.Size = new Size(31, 20);
            iswin.TabIndex = 4;
            iswin.Text = "win/loss";
            // 
            // LivePlayersForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ActiveBorder;
            Controls.Add(gridPanel1);
            DoubleBuffered = true;
            Name = "LivePlayersForm";
            Size = new Size(372, 26);
            gridPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private AntdUI.GridPanel gridPanel1;
        private AntdUI.Label iswin;
        private AntdUI.Label kda;
        private AntdUI.Label gamedate;
        private AntdUI.Label name;
        private AntdUI.Label gametype;
    }
}
