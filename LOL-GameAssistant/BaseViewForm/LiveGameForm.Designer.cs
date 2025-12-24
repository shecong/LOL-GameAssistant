namespace LOL_GameAssistant.BaseViewForm
{
    partial class LiveGameForm
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
            splitter1 = new AntdUI.Splitter();
            gridPanel1 = new AntdUI.GridPanel();
            gridPanel2 = new AntdUI.GridPanel();
            ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
            splitter1.Panel1.SuspendLayout();
            splitter1.Panel2.SuspendLayout();
            splitter1.SuspendLayout();
            SuspendLayout();
            // 
            // splitter1
            // 
            splitter1.Dock = DockStyle.Fill;
            splitter1.Location = new Point(0, 0);
            splitter1.Name = "splitter1";
            // 
            // splitter1.Panel1
            // 
            splitter1.Panel1.Controls.Add(gridPanel1);
            // 
            // splitter1.Panel2
            // 
            splitter1.Panel2.Controls.Add(gridPanel2);
            splitter1.Size = new Size(1331, 830);
            splitter1.SplitterDistance = 630;
            splitter1.TabIndex = 0;
            // 
            // gridPanel1
            // 
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(630, 830);
            gridPanel1.Span = "100%; 100%; 100%; 100%; 100%; -20% 20%  20% 20% 20% ";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // gridPanel2
            // 
            gridPanel2.Dock = DockStyle.Fill;
            gridPanel2.Location = new Point(0, 0);
            gridPanel2.Name = "gridPanel2";
            gridPanel2.Size = new Size(697, 830);
            gridPanel2.Span = "100%; 100%; 100%; 100%; 100%; -20% 20%  20% 20% 20% ";
            gridPanel2.TabIndex = 1;
            gridPanel2.Text = "gridPanel2";
            // 
            // LiveGameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(splitter1);
            DoubleBuffered = true;
            Name = "LiveGameForm";
            Size = new Size(1331, 830);
            splitter1.Panel1.ResumeLayout(false);
            splitter1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
            splitter1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Splitter splitter1;
        private AntdUI.GridPanel gridPanel1;
        private AntdUI.GridPanel gridPanel2;
    }
}
