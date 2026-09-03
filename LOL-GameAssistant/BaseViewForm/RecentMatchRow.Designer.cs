namespace LOL_GameAssistant.BaseViewForm
{
    partial class RecentMatchRow
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            picChampion = new PictureBox();
            lblResult = new AntdUI.Label();
            lblChampion = new AntdUI.Label();
            lblMode = new AntdUI.Label();
            lblDate = new AntdUI.Label();
            lblKda = new AntdUI.Label();
            lblDuration = new AntdUI.Label();
            ((System.ComponentModel.ISupportInitialize)picChampion).BeginInit();
            SuspendLayout();
            // 
            // picChampion
            // 
            picChampion.Location = new Point(5, 3);
            picChampion.Name = "picChampion";
            picChampion.Size = new Size(34, 34);
            picChampion.SizeMode = PictureBoxSizeMode.Zoom;
            picChampion.TabIndex = 0;
            picChampion.TabStop = false;
            // 
            // lblResult
            // 
            lblResult.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblResult.Location = new Point(45, 10);
            lblResult.Name = "lblResult";
            lblResult.Size = new Size(36, 20);
            lblResult.TabIndex = 1;
            lblResult.Text = "胜负";
            // 
            // lblChampion
            // 
            lblChampion.Font = new Font("Microsoft YaHei UI", 9F);
            lblChampion.Location = new Point(87, 10);
            lblChampion.Name = "lblChampion";
            lblChampion.Size = new Size(90, 20);
            lblChampion.TabIndex = 2;
            lblChampion.Text = "英雄";
            // 
            // lblMode
            // 
            lblMode.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblMode.ForeColor = SystemColors.GrayText;
            lblMode.Location = new Point(183, 10);
            lblMode.Name = "lblMode";
            lblMode.Size = new Size(160, 20);
            lblMode.TabIndex = 3;
            lblMode.Text = "模式";
            // 
            // lblDate
            // 
            lblDate.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDate.ForeColor = SystemColors.GrayText;
            lblDate.Location = new Point(279, 10);
            lblDate.Name = "lblDate";
            lblDate.Size = new Size(80, 20);
            lblDate.TabIndex = 4;
            lblDate.Text = "日期";
            // 
            // lblKda
            // 
            lblKda.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblKda.Location = new Point(365, 10);
            lblKda.Name = "lblKda";
            lblKda.Size = new Size(90, 20);
            lblKda.TabIndex = 5;
            lblKda.Text = "KDA";
            // 
            // lblDuration
            // 
            lblDuration.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDuration.ForeColor = SystemColors.GrayText;
            lblDuration.Location = new Point(460, 10);
            lblDuration.Name = "lblDuration";
            lblDuration.Size = new Size(90, 20);
            lblDuration.TabIndex = 6;
            lblDuration.Text = "时长";
            // 
            // RecentMatchRow
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(250, 250, 250);
            Controls.Add(lblDuration);
            Controls.Add(lblKda);
            Controls.Add(lblDate);
            Controls.Add(lblMode);
            Controls.Add(lblChampion);
            Controls.Add(lblResult);
            Controls.Add(picChampion);
            DoubleBuffered = true;
            Name = "RecentMatchRow";
            Size = new Size(620, 40);
            ((System.ComponentModel.ISupportInitialize)picChampion).EndInit();
            ResumeLayout(false);
        }

        private PictureBox picChampion;
        private AntdUI.Label lblResult;
        private AntdUI.Label lblChampion;
        private AntdUI.Label lblMode;
        private AntdUI.Label lblDate;
        private AntdUI.Label lblKda;
        private AntdUI.Label lblDuration;
    }
}
