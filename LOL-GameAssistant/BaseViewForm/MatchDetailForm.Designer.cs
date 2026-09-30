namespace LOL_GameAssistant.BaseViewForm
{
    partial class MatchDetailForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
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
            lblTitle = new AntdUI.Label();
            btnClose = new AntdUI.Button();
            playersGrid = new TableLayoutPanel();
            allyPanel = new AntdUI.Panel();
            lblAllyHeader = new AntdUI.Label();
            flowAlly = new FlowLayoutPanel();
            enemyPanel = new AntdUI.Panel();
            lblEnemyHeader = new AntdUI.Label();
            flowEnemy = new FlowLayoutPanel();
            bottomPanel = new AntdUI.Panel();
            lblItems = new AntdUI.Label();
            lblStats = new AntdUI.Label();
            lblKda = new AntdUI.Label();
            lblChampion = new AntdUI.Label();
            playersGrid.SuspendLayout();
            allyPanel.SuspendLayout();
            enemyPanel.SuspendLayout();
            bottomPanel.SuspendLayout();
            SuspendLayout();
            // 
            // lblTitle
            // 
            lblTitle.BackColor = Color.FromArgb(245, 247, 250);
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTitle.Height = 44;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnClose
            // 
            btnClose.Location = new Point(910, 8);
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(64, 28);
            btnClose.TabIndex = 0;
            btnClose.Text = "关闭";
            btnClose.Click += btnClose_Click;
            // 
            // playersGrid
            // 
            playersGrid.ColumnCount = 2;
            playersGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            playersGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            playersGrid.Controls.Add(allyPanel, 0, 0);
            playersGrid.Controls.Add(enemyPanel, 1, 0);
            playersGrid.Dock = DockStyle.Fill;
            playersGrid.Location = new Point(0, 44);
            playersGrid.Name = "playersGrid";
            playersGrid.Padding = new Padding(10);
            playersGrid.RowCount = 1;
            playersGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            playersGrid.Size = new Size(1000, 480);
            playersGrid.TabIndex = 1;
            // 
            // allyPanel
            // 
            allyPanel.Controls.Add(flowAlly);
            allyPanel.Controls.Add(lblAllyHeader);
            allyPanel.Dock = DockStyle.Fill;
            allyPanel.Padding = new Padding(0, 0, 6, 0);
            // 
            // lblAllyHeader
            // 
            lblAllyHeader.BackColor = Color.Transparent;
            lblAllyHeader.Dock = DockStyle.Top;
            lblAllyHeader.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblAllyHeader.ForeColor = Color.FromArgb(30, 136, 229);
            lblAllyHeader.Height = 28;
            lblAllyHeader.Text = "我方";
            lblAllyHeader.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // flowAlly
            // 
            flowAlly.AutoScroll = true;
            flowAlly.Dock = DockStyle.Fill;
            flowAlly.FlowDirection = FlowDirection.TopDown;
            flowAlly.Padding = new Padding(0, 4, 0, 0);
            flowAlly.WrapContents = false;
            // 
            // enemyPanel
            // 
            enemyPanel.Controls.Add(flowEnemy);
            enemyPanel.Controls.Add(lblEnemyHeader);
            enemyPanel.Dock = DockStyle.Fill;
            enemyPanel.Padding = new Padding(6, 0, 0, 0);
            // 
            // lblEnemyHeader
            // 
            lblEnemyHeader.BackColor = Color.Transparent;
            lblEnemyHeader.Dock = DockStyle.Top;
            lblEnemyHeader.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblEnemyHeader.ForeColor = Color.FromArgb(211, 47, 47);
            lblEnemyHeader.Height = 28;
            lblEnemyHeader.Text = "敌方";
            lblEnemyHeader.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // flowEnemy
            // 
            flowEnemy.AutoScroll = true;
            flowEnemy.Dock = DockStyle.Fill;
            flowEnemy.FlowDirection = FlowDirection.TopDown;
            flowEnemy.Padding = new Padding(0, 4, 0, 0);
            flowEnemy.WrapContents = false;
            // 
            // bottomPanel
            // 
            bottomPanel.Controls.Add(lblItems);
            bottomPanel.Controls.Add(lblStats);
            bottomPanel.Controls.Add(lblKda);
            bottomPanel.Controls.Add(lblChampion);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 116;
            bottomPanel.Padding = new Padding(12, 8, 12, 8);
            // 
            // lblItems
            // 
            lblItems.BackColor = Color.Transparent;
            lblItems.Dock = DockStyle.Top;
            lblItems.Font = new Font("Microsoft YaHei UI", 9F);
            lblItems.Height = 24;
            // 
            // lblStats
            // 
            lblStats.BackColor = Color.Transparent;
            lblStats.Dock = DockStyle.Top;
            lblStats.Font = new Font("Microsoft YaHei UI", 9F);
            lblStats.Height = 52;
            // 
            // lblKda
            // 
            lblKda.BackColor = Color.Transparent;
            lblKda.Dock = DockStyle.Top;
            lblKda.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            lblKda.Height = 22;
            // 
            // lblChampion
            // 
            lblChampion.BackColor = Color.Transparent;
            lblChampion.Dock = DockStyle.Top;
            lblChampion.Font = new Font("Microsoft YaHei UI", 9.5F);
            lblChampion.Height = 22;
            // 
            // MatchDetailForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(1000, 640);
            Controls.Add(playersGrid);
            Controls.Add(bottomPanel);
            Controls.Add(lblTitle);
            Controls.Add(btnClose);
            DoubleBuffered = true;
            Name = "MatchDetailForm";
            StartPosition = FormStartPosition.CenterParent;
            playersGrid.ResumeLayout(false);
            allyPanel.ResumeLayout(false);
            enemyPanel.ResumeLayout(false);
            bottomPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        private AntdUI.Label lblTitle;
        private AntdUI.Button btnClose;
        private TableLayoutPanel playersGrid;
        private AntdUI.Panel allyPanel;
        private AntdUI.Label lblAllyHeader;
        private FlowLayoutPanel flowAlly;
        private AntdUI.Panel enemyPanel;
        private AntdUI.Label lblEnemyHeader;
        private FlowLayoutPanel flowEnemy;
        private AntdUI.Panel bottomPanel;
        private AntdUI.Label lblItems;
        private AntdUI.Label lblStats;
        private AntdUI.Label lblKda;
        private AntdUI.Label lblChampion;
    }
}
