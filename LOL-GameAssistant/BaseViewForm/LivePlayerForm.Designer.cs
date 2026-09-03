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
            headerPanel = new Panel();
            picProfile = new RoundPictureBox();
            lblName = new Label();
            lblSub = new Label();
            lblSummary = new Label();
            lblChampionNow = new Label();
            lblTeamTag = new AntdUI.Label();
            lblPremadeTag = new AntdUI.Label();
            btnCopy = new AntdUI.Button();
            picCurrent = new RoundPictureBox();
            panelMatches = new Panel();
            headerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picProfile).BeginInit();
            ((System.ComponentModel.ISupportInitialize)picCurrent).BeginInit();
            SuspendLayout();
            // 
            // headerPanel
            // 
            headerPanel.BackColor = Color.FromArgb(248, 250, 253);
            headerPanel.Controls.Add(picProfile);
            headerPanel.Controls.Add(lblName);
            headerPanel.Controls.Add(lblSub);
            headerPanel.Controls.Add(lblSummary);
            headerPanel.Controls.Add(lblChampionNow);
            headerPanel.Controls.Add(lblTeamTag);
            headerPanel.Controls.Add(lblPremadeTag);
            headerPanel.Controls.Add(btnCopy);
            headerPanel.Controls.Add(picCurrent);
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 58;
            // 
            // picProfile
            // 
            picProfile.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            picProfile.BorderColor = Color.FromArgb(220, 30, 136, 229);
            picProfile.BorderWidth = 2;
            picProfile.Location = new Point(10, 10);
            picProfile.Name = "picProfile";
            picProfile.Size = new Size(38, 38);
            picProfile.TabIndex = 0;
            picProfile.TabStop = false;
            // 
            // lblName
            // 
            lblName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblName.AutoEllipsis = true;
            lblName.BackColor = Color.Transparent;
            lblName.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblName.Location = new Point(56, 8);
            lblName.Name = "lblName";
            lblName.Size = new Size(240, 22);
            lblName.TabIndex = 1;
            lblName.Text = "玩家";
            // 
            // lblSub
            // 
            lblSub.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblSub.AutoEllipsis = true;
            lblSub.BackColor = Color.Transparent;
            lblSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSub.ForeColor = SystemColors.GrayText;
            lblSub.Location = new Point(56, 32);
            lblSub.Name = "lblSub";
            lblSub.Size = new Size(240, 20);
            lblSub.TabIndex = 2;
            lblSub.Text = "";
            // 
            // lblSummary
            // 
            lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblSummary.AutoSize = false;
            lblSummary.BackColor = Color.Transparent;
            lblSummary.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblSummary.ForeColor = Color.FromArgb(30, 136, 229);
            lblSummary.Location = new Point(300, 8);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new Size(130, 22);
            lblSummary.TabIndex = 3;
            lblSummary.Text = "";
            lblSummary.TextAlign = ContentAlignment.MiddleRight;
            // 
            // lblChampionNow
            // 
            lblChampionNow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblChampionNow.AutoEllipsis = true;
            lblChampionNow.BackColor = Color.Transparent;
            lblChampionNow.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblChampionNow.ForeColor = Color.FromArgb(30, 136, 229);
            lblChampionNow.Location = new Point(290, 32);
            lblChampionNow.Name = "lblChampionNow";
            lblChampionNow.Size = new Size(160, 20);
            lblChampionNow.TabIndex = 4;
            lblChampionNow.Text = "";
            lblChampionNow.Visible = false;
            // 
            // lblTeamTag
            // 
            lblTeamTag.AutoSize = false;
            lblTeamTag.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            lblTeamTag.Location = new Point(470, 32);
            lblTeamTag.Name = "lblTeamTag";
            lblTeamTag.Size = new Size(36, 20);
            lblTeamTag.TabIndex = 7;
            lblTeamTag.Text = "队友";
            lblTeamTag.TextAlign = ContentAlignment.MiddleCenter;
            lblTeamTag.Visible = false;
            // 
            // lblPremadeTag
            // 
            lblPremadeTag.AutoSize = false;
            lblPremadeTag.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            lblPremadeTag.Location = new Point(420, 32);
            lblPremadeTag.Name = "lblPremadeTag";
            lblPremadeTag.Size = new Size(46, 20);
            lblPremadeTag.TabIndex = 8;
            lblPremadeTag.Text = "开黑1";
            lblPremadeTag.TextAlign = ContentAlignment.MiddleCenter;
            lblPremadeTag.Visible = false;
            // 
            // btnCopy
            // 
            btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCopy.Location = new Point(470, 8);
            btnCopy.Name = "btnCopy";
            btnCopy.Size = new Size(60, 26);
            btnCopy.TabIndex = 5;
            btnCopy.Text = "复制ID";
            btnCopy.Click += BtnCopy_Click;
            // 
            // picCurrent
            // 
            picCurrent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            picCurrent.BorderColor = Color.FromArgb(180, 30, 136, 229);
            picCurrent.BorderWidth = 2;
            picCurrent.Location = new Point(438, 10);
            picCurrent.Name = "picCurrent";
            picCurrent.Size = new Size(28, 28);
            picCurrent.TabIndex = 6;
            picCurrent.TabStop = false;
            picCurrent.Visible = false;
            // 
            // panelMatches
            // 
            panelMatches.AutoScroll = true;
            panelMatches.BackColor = Color.FromArgb(248, 249, 251);
            panelMatches.Dock = DockStyle.Fill;
            panelMatches.Location = new Point(0, 58);
            panelMatches.Name = "panelMatches";
            panelMatches.Size = new Size(620, 412);
            panelMatches.TabIndex = 7;
            // 
            // LivePlayerForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(252, 252, 253);
            Controls.Add(panelMatches);
            Controls.Add(headerPanel);
            DoubleBuffered = true;
            Name = "LivePlayerForm";
            Size = new Size(620, 470);
            headerPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)picProfile).EndInit();
            ((System.ComponentModel.ISupportInitialize)picCurrent).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Panel headerPanel;
        private RoundPictureBox picProfile;
        private Label lblName;
        private Label lblSub;
        private Label lblSummary;
        private Label lblChampionNow;
        private AntdUI.Label lblTeamTag;
        private AntdUI.Label lblPremadeTag;
        private AntdUI.Button btnCopy;
        private RoundPictureBox picCurrent;
        private Panel panelMatches;
    }
}
