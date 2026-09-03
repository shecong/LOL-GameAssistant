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
            rootGrid = new TableLayoutPanel();
            infoBar = new GradientPanel();
            pulseDot = new PulseDot();
            lblGameInfo = new Label();
            column1 = new Panel();
            panelTeam1 = new FlowLayoutPanel();
            headerTeam1 = new GradientPanel();
            lblTeamTitle1 = new Label();
            column2 = new Panel();
            panelTeam2 = new FlowLayoutPanel();
            headerTeam2 = new GradientPanel();
            lblTeamTitle2 = new Label();
            rootGrid.SuspendLayout();
            infoBar.SuspendLayout();
            column1.SuspendLayout();
            headerTeam1.SuspendLayout();
            column2.SuspendLayout();
            headerTeam2.SuspendLayout();
            SuspendLayout();
            // 
            // rootGrid
            // 
            rootGrid.ColumnCount = 2;
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rootGrid.Controls.Add(infoBar, 0, 0);
            rootGrid.Controls.Add(column1, 0, 1);
            rootGrid.Controls.Add(column2, 1, 1);
            rootGrid.Dock = DockStyle.Fill;
            rootGrid.Location = new Point(0, 0);
            rootGrid.Name = "rootGrid";
            rootGrid.RowCount = 2;
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootGrid.Size = new Size(1331, 830);
            rootGrid.TabIndex = 0;
            // 
            // infoBar
            // 
            infoBar.BackColor = Color.Transparent;
            rootGrid.SetColumnSpan(infoBar, 2);
            infoBar.Controls.Add(pulseDot);
            infoBar.Controls.Add(lblGameInfo);
            infoBar.Dock = DockStyle.Fill;
            infoBar.Location = new Point(8, 8);
            infoBar.Margin = new Padding(8, 8, 8, 4);
            infoBar.Name = "infoBar";
            infoBar.Size = new Size(1315, 28);
            infoBar.TabIndex = 0;
            // 
            // pulseDot
            // 
            pulseDot.Dock = DockStyle.Left;
            pulseDot.Location = new Point(0, 0);
            pulseDot.Name = "pulseDot";
            pulseDot.Size = new Size(18, 28);
            pulseDot.TabIndex = 1;
            // 
            // lblGameInfo
            // 
            lblGameInfo.AutoSize = true;
            lblGameInfo.BackColor = Color.Transparent;
            lblGameInfo.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblGameInfo.ForeColor = Color.FromArgb(66, 66, 66);
            lblGameInfo.Location = new Point(34, 5);
            lblGameInfo.Name = "lblGameInfo";
            lblGameInfo.Size = new Size(93, 19);
            lblGameInfo.TabIndex = 0;
            lblGameInfo.Text = "暂无对局信息";
            // 
            // column1
            // 
            column1.Controls.Add(panelTeam1);
            column1.Controls.Add(headerTeam1);
            column1.Dock = DockStyle.Fill;
            column1.Location = new Point(3, 43);
            column1.Name = "column1";
            column1.Padding = new Padding(8, 4, 8, 8);
            column1.Size = new Size(659, 784);
            column1.TabIndex = 0;
            // 
            // panelTeam1
            // 
            panelTeam1.AutoScroll = true;
            panelTeam1.BackColor = Color.FromArgb(246, 248, 251);
            panelTeam1.Dock = DockStyle.Fill;
            panelTeam1.Location = new Point(8, 38);
            panelTeam1.Name = "panelTeam1";
            panelTeam1.Padding = new Padding(4);
            panelTeam1.Size = new Size(643, 738);
            panelTeam1.TabIndex = 0;
            // 
            // headerTeam1
            // 
            headerTeam1.BackColor = Color.Transparent;
            headerTeam1.Controls.Add(lblTeamTitle1);
            headerTeam1.Dock = DockStyle.Top;
            headerTeam1.Location = new Point(8, 4);
            headerTeam1.Name = "headerTeam1";
            headerTeam1.Padding = new Padding(10, 0, 10, 0);
            headerTeam1.Size = new Size(643, 34);
            headerTeam1.TabIndex = 1;
            // 
            // lblTeamTitle1
            // 
            lblTeamTitle1.AutoSize = true;
            lblTeamTitle1.BackColor = Color.Transparent;
            lblTeamTitle1.Dock = DockStyle.Fill;
            lblTeamTitle1.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTeamTitle1.ForeColor = Color.White;
            lblTeamTitle1.Location = new Point(10, 0);
            lblTeamTitle1.Name = "lblTeamTitle1";
            lblTeamTitle1.Size = new Size(37, 19);
            lblTeamTitle1.TabIndex = 0;
            lblTeamTitle1.Text = "蓝方";
            lblTeamTitle1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // column2
            // 
            column2.Controls.Add(panelTeam2);
            column2.Controls.Add(headerTeam2);
            column2.Dock = DockStyle.Fill;
            column2.Location = new Point(668, 43);
            column2.Name = "column2";
            column2.Padding = new Padding(8, 4, 8, 8);
            column2.Size = new Size(660, 784);
            column2.TabIndex = 1;
            // 
            // panelTeam2
            // 
            panelTeam2.AutoScroll = true;
            panelTeam2.BackColor = Color.FromArgb(246, 248, 251);
            panelTeam2.Dock = DockStyle.Fill;
            panelTeam2.Location = new Point(8, 38);
            panelTeam2.Name = "panelTeam2";
            panelTeam2.Padding = new Padding(4);
            panelTeam2.Size = new Size(644, 738);
            panelTeam2.TabIndex = 1;
            // 
            // headerTeam2
            // 
            headerTeam2.BackColor = Color.Transparent;
            headerTeam2.Controls.Add(lblTeamTitle2);
            headerTeam2.Dock = DockStyle.Top;
            headerTeam2.Location = new Point(8, 4);
            headerTeam2.Name = "headerTeam2";
            headerTeam2.Padding = new Padding(10, 0, 10, 0);
            headerTeam2.Size = new Size(644, 34);
            headerTeam2.TabIndex = 1;
            // 
            // lblTeamTitle2
            // 
            lblTeamTitle2.AutoSize = true;
            lblTeamTitle2.BackColor = Color.Transparent;
            lblTeamTitle2.Dock = DockStyle.Fill;
            lblTeamTitle2.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTeamTitle2.ForeColor = Color.White;
            lblTeamTitle2.Location = new Point(10, 0);
            lblTeamTitle2.Name = "lblTeamTitle2";
            lblTeamTitle2.Size = new Size(37, 19);
            lblTeamTitle2.TabIndex = 0;
            lblTeamTitle2.Text = "红方";
            lblTeamTitle2.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // LiveGameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(240, 243, 248);
            Controls.Add(rootGrid);
            DoubleBuffered = true;
            Name = "LiveGameForm";
            Size = new Size(1331, 830);
            rootGrid.ResumeLayout(false);
            infoBar.ResumeLayout(false);
            infoBar.PerformLayout();
            column1.ResumeLayout(false);
            headerTeam1.ResumeLayout(false);
            headerTeam1.PerformLayout();
            column2.ResumeLayout(false);
            headerTeam2.ResumeLayout(false);
            headerTeam2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel rootGrid;
        private GradientPanel infoBar;
        private PulseDot pulseDot;
        private Label lblGameInfo;
        private Panel column1;
        private GradientPanel headerTeam1;
        private Label lblTeamTitle1;
        private FlowLayoutPanel panelTeam1;
        private Panel column2;
        private GradientPanel headerTeam2;
        private Label lblTeamTitle2;
        private FlowLayoutPanel panelTeam2;
    }
}
