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
            name = new Label();
            gamedate = new Label();
            kda = new Label();
            iswin = new Label();
            gametype = new Label();
            SuspendLayout();
            // 
            // name
            // 
            name.AutoSize = true;
            name.Location = new Point(63, 11);
            name.Name = "name";
            name.Size = new Size(44, 17);
            name.TabIndex = 1;
            name.Text = "英雄名";
            // 
            // gamedate
            // 
            gamedate.AutoSize = true;
            gamedate.Location = new Point(156, 11);
            gamedate.Name = "gamedate";
            gamedate.Size = new Size(56, 17);
            gamedate.TabIndex = 2;
            gamedate.Text = "游戏日期";
            // 
            // kda
            // 
            kda.AutoSize = true;
            kda.Location = new Point(255, 11);
            kda.Name = "kda";
            kda.Size = new Size(39, 17);
            kda.TabIndex = 3;
            kda.Text = "0/0/0";
            // 
            // iswin
            // 
            iswin.AutoSize = true;
            iswin.Location = new Point(300, 11);
            iswin.Name = "iswin";
            iswin.Size = new Size(55, 17);
            iswin.TabIndex = 4;
            iswin.Text = "win/loss";
            // 
            // gametype
            // 
            gametype.AutoSize = true;
            gametype.Location = new Point(3, 11);
            gametype.Name = "gametype";
            gametype.Size = new Size(44, 17);
            gametype.TabIndex = 5;
            gametype.Text = "英雄名";
            // 
            // LivePlayersForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(gametype);
            Controls.Add(iswin);
            Controls.Add(kda);
            Controls.Add(gamedate);
            Controls.Add(name);
            Name = "LivePlayersForm";
            Size = new Size(355, 35);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Label name;
        private Label gamedate;
        private Label kda;
        private Label iswin;
        private Label gametype;
    }
}
