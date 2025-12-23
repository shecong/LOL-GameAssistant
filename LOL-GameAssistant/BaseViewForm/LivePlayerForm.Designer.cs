namespace LOL_GameAssistant.BaseViewForm
{
    partial class LivePlayerForm
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
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            SuspendLayout();
            // 
            // gridPanel1
            // 
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(588, 237);
            gridPanel1.Span = "50% 50%;50% 50%;50% 50%;50% 50%;50% 50%";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // LivePlayerForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(gridPanel1);
            Name = "LivePlayerForm";
            Size = new Size(588, 237);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.GridPanel gridPanel1;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
    }
}
