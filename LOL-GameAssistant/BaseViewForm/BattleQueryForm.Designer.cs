namespace LOL_GameAssistant.BaseViewForm
{
    partial class BattleQueryForm
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
            panelSearch = new FlowLayoutPanel();
            cboFavorites = new ComboBox();
            btnLoadFavorite = new AntdUI.Button();
            inpSearch = new AntdUI.Input();
            btnSearch = new AntdUI.Button();
            btnFavorite = new AntdUI.Button();
            btnExport = new AntdUI.Button();
            btnViewRecord = new AntdUI.Button();
            btnViewStats = new AntdUI.Button();
            lblPageSize = new Label();
            cboPageSize = new ComboBox();
            panelPlayer = new Panel();
            avatarPlayer = new AntdUI.Avatar();
            panelPlayerInfo = new Panel();
            lblPlayerName = new AntdUI.Label();
            lblPlayerTag = new AntdUI.Label();
            lblPlayerLevel = new AntdUI.Label();
            panelRanked = new Panel();
            lblSoloTitle = new AntdUI.Label();
            lblSoloStats = new AntdUI.Label();
            lblFlexTitle = new AntdUI.Label();
            lblFlexStats = new AntdUI.Label();
            panelContent = new Panel();
            panelHistory = new Panel();
            stackMatches = new Panel();
            pagination = new AntdUI.Pagination();
            panelStats = new Panel();
            lblStatus = new AntdUI.Label();
            panelSearch.SuspendLayout();
            panelPlayer.SuspendLayout();
            panelPlayerInfo.SuspendLayout();
            panelRanked.SuspendLayout();
            panelContent.SuspendLayout();
            panelHistory.SuspendLayout();
            SuspendLayout();
            // 
            // panelSearch
            // 
            panelSearch.Controls.Add(cboFavorites);
            panelSearch.Controls.Add(btnLoadFavorite);
            panelSearch.Controls.Add(inpSearch);
            panelSearch.Controls.Add(btnSearch);
            panelSearch.Controls.Add(btnFavorite);
            panelSearch.Controls.Add(btnExport);
            panelSearch.Controls.Add(btnViewRecord);
            panelSearch.Controls.Add(btnViewStats);
            panelSearch.Controls.Add(lblPageSize);
            panelSearch.Controls.Add(cboPageSize);
            panelSearch.Dock = DockStyle.Top;
            panelSearch.Location = new Point(0, 0);
            panelSearch.Name = "panelSearch";
            panelSearch.Padding = new Padding(10, 10, 10, 8);
            panelSearch.Size = new Size(1510, 56);
            panelSearch.TabIndex = 0;
            // 
            // cboFavorites
            // 
            cboFavorites.DropDownStyle = ComboBoxStyle.DropDownList;
            cboFavorites.FormattingEnabled = true;
            cboFavorites.Location = new Point(10, 12);
            cboFavorites.Margin = new Padding(0, 2, 6, 2);
            cboFavorites.Name = "cboFavorites";
            cboFavorites.Size = new Size(240, 25);
            cboFavorites.TabIndex = 0;
            // 
            // btnLoadFavorite
            // 
            btnLoadFavorite.AutoSize = true;
            btnLoadFavorite.Location = new Point(256, 11);
            btnLoadFavorite.Margin = new Padding(0, 1, 6, 1);
            btnLoadFavorite.Name = "btnLoadFavorite";
            btnLoadFavorite.Size = new Size(64, 34);
            btnLoadFavorite.TabIndex = 1;
            btnLoadFavorite.Text = "加载";
            btnLoadFavorite.Click += BtnLoadFavorite_Click;
            // 
            // inpSearch
            // 
            inpSearch.Location = new Point(326, 11);
            inpSearch.Margin = new Padding(0, 1, 6, 1);
            inpSearch.Name = "inpSearch";
            inpSearch.PlaceholderText = "输入 puuid 或 名称#TAG 查询战绩";
            inpSearch.Size = new Size(420, 34);
            inpSearch.TabIndex = 2;
            inpSearch.KeyPress += InpSearch_KeyPress;
            // 
            // btnSearch
            // 
            btnSearch.Location = new Point(752, 11);
            btnSearch.Margin = new Padding(0, 1, 6, 1);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(80, 34);
            btnSearch.TabIndex = 3;
            btnSearch.Text = "查询";
            btnSearch.Type = AntdUI.TTypeMini.Primary;
            btnSearch.Click += BtnSearch_Click;
            // 
            // btnFavorite
            // 
            btnFavorite.Location = new Point(838, 11);
            btnFavorite.Margin = new Padding(0, 1, 6, 1);
            btnFavorite.Name = "btnFavorite";
            btnFavorite.Size = new Size(80, 34);
            btnFavorite.TabIndex = 4;
            btnFavorite.Text = "☆ 收藏";
            btnFavorite.Click += BtnFavorite_Click;
            // 
            // btnExport
            // 
            btnExport.Location = new Point(924, 11);
            btnExport.Margin = new Padding(0, 1, 6, 1);
            btnExport.Name = "btnExport";
            btnExport.Size = new Size(90, 34);
            btnExport.TabIndex = 5;
            btnExport.Text = "导出战绩";
            btnExport.Click += BtnExport_Click;
            // 
            // btnViewRecord
            // 
            btnViewRecord.Location = new Point(1020, 11);
            btnViewRecord.Margin = new Padding(0, 1, 6, 1);
            btnViewRecord.Name = "btnViewRecord";
            btnViewRecord.Size = new Size(70, 34);
            btnViewRecord.TabIndex = 6;
            btnViewRecord.Text = "记录";
            btnViewRecord.Type = AntdUI.TTypeMini.Primary;
            btnViewRecord.Click += BtnViewRecord_Click;
            // 
            // btnViewStats
            // 
            btnViewStats.Location = new Point(1096, 11);
            btnViewStats.Margin = new Padding(0, 1, 6, 1);
            btnViewStats.Name = "btnViewStats";
            btnViewStats.Size = new Size(70, 34);
            btnViewStats.TabIndex = 7;
            btnViewStats.Text = "统计";
            btnViewStats.Click += BtnViewStats_Click;
            // 
            // lblPageSize
            // 
            lblPageSize.AutoSize = true;
            lblPageSize.Location = new Point(1172, 18);
            lblPageSize.Margin = new Padding(6, 6, 2, 0);
            lblPageSize.Name = "lblPageSize";
            lblPageSize.Size = new Size(32, 17);
            lblPageSize.TabIndex = 8;
            lblPageSize.Text = "每页";
            // 
            // cboPageSize
            // 
            cboPageSize.DropDownStyle = ComboBoxStyle.DropDownList;
            cboPageSize.FormattingEnabled = true;
            cboPageSize.Items.AddRange(new object[] { 10, 20, 50 });
            cboPageSize.Location = new Point(1206, 14);
            cboPageSize.Margin = new Padding(0, 2, 6, 0);
            cboPageSize.Name = "cboPageSize";
            cboPageSize.Size = new Size(58, 25);
            cboPageSize.TabIndex = 9;
            cboPageSize.SelectedIndexChanged += CboPageSize_SelectedIndexChanged;
            cboPageSize.SelectedIndex = 0;
            // 
            // panelPlayer
            // 
            panelPlayer.Controls.Add(panelPlayerInfo);
            panelPlayer.Controls.Add(panelRanked);
            panelPlayer.Controls.Add(avatarPlayer);
            panelPlayer.Dock = DockStyle.Top;
            panelPlayer.Location = new Point(0, 56);
            panelPlayer.Name = "panelPlayer";
            panelPlayer.Padding = new Padding(8);
            panelPlayer.Size = new Size(1510, 118);
            panelPlayer.TabIndex = 1;
            panelPlayer.Visible = false;
            // 
            // avatarPlayer
            // 
            avatarPlayer.Dock = DockStyle.Left;
            avatarPlayer.Location = new Point(8, 8);
            avatarPlayer.Name = "avatarPlayer";
            avatarPlayer.Size = new Size(96, 102);
            avatarPlayer.TabIndex = 0;
            avatarPlayer.Text = "无";
            avatarPlayer.Visible = false;
            // 
            // panelRanked
            // 
            panelRanked.Controls.Add(lblFlexStats);
            panelRanked.Controls.Add(lblFlexTitle);
            panelRanked.Controls.Add(lblSoloStats);
            panelRanked.Controls.Add(lblSoloTitle);
            panelRanked.Dock = DockStyle.Right;
            panelRanked.Location = new Point(1030, 8);
            panelRanked.Name = "panelRanked";
            panelRanked.Size = new Size(472, 102);
            panelRanked.TabIndex = 2;
            // 
            // lblSoloTitle
            // 
            lblSoloTitle.Dock = DockStyle.Top;
            lblSoloTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblSoloTitle.ForeColor = SystemColors.Highlight;
            lblSoloTitle.Height = 26;
            lblSoloTitle.Text = "";
            lblSoloTitle.Visible = false;
            // 
            // lblSoloStats
            // 
            lblSoloStats.Dock = DockStyle.Top;
            lblSoloStats.Font = new Font("Microsoft YaHei UI", 9F);
            lblSoloStats.Height = 24;
            lblSoloStats.Text = "";
            lblSoloStats.Visible = false;
            // 
            // lblFlexTitle
            // 
            lblFlexTitle.Dock = DockStyle.Top;
            lblFlexTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblFlexTitle.ForeColor = SystemColors.Highlight;
            lblFlexTitle.Height = 26;
            lblFlexTitle.Text = "";
            lblFlexTitle.Visible = false;
            // 
            // lblFlexStats
            // 
            lblFlexStats.Dock = DockStyle.Top;
            lblFlexStats.Font = new Font("Microsoft YaHei UI", 9F);
            lblFlexStats.Height = 24;
            lblFlexStats.Text = "";
            lblFlexStats.Visible = false;
            // 
            // panelPlayerInfo
            // 
            panelPlayerInfo.Controls.Add(lblPlayerLevel);
            panelPlayerInfo.Controls.Add(lblPlayerTag);
            panelPlayerInfo.Controls.Add(lblPlayerName);
            panelPlayerInfo.Dock = DockStyle.Fill;
            panelPlayerInfo.Location = new Point(104, 8);
            panelPlayerInfo.Name = "panelPlayerInfo";
            panelPlayerInfo.Size = new Size(926, 102);
            panelPlayerInfo.TabIndex = 1;
            // 
            // lblPlayerName
            // 
            lblPlayerName.Dock = DockStyle.Top;
            lblPlayerName.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            lblPlayerName.Height = 40;
            lblPlayerName.Text = "";
            lblPlayerName.Visible = false;
            // 
            // lblPlayerTag
            // 
            lblPlayerTag.Dock = DockStyle.Top;
            lblPlayerTag.Font = new Font("Microsoft YaHei UI", 10F);
            lblPlayerTag.Height = 28;
            lblPlayerTag.Text = "";
            lblPlayerTag.Visible = false;
            // 
            // lblPlayerLevel
            // 
            lblPlayerLevel.Dock = DockStyle.Top;
            lblPlayerLevel.Font = new Font("Microsoft YaHei UI", 10F);
            lblPlayerLevel.Height = 28;
            lblPlayerLevel.Text = "";
            lblPlayerLevel.Visible = false;
            // 
            // panelContent
            // 
            panelContent.Controls.Add(panelStats);
            panelContent.Controls.Add(panelHistory);
            panelContent.Dock = DockStyle.Fill;
            panelContent.Location = new Point(0, 174);
            panelContent.Name = "panelContent";
            panelContent.Size = new Size(1510, 571);
            panelContent.TabIndex = 2;
            // 
            // panelHistory
            // 
            panelHistory.Controls.Add(pagination);
            panelHistory.Controls.Add(stackMatches);
            panelHistory.Dock = DockStyle.Fill;
            panelHistory.Location = new Point(0, 0);
            panelHistory.Name = "panelHistory";
            panelHistory.Size = new Size(1510, 571);
            panelHistory.TabIndex = 0;
            // 
            // stackMatches
            // 
            stackMatches.AutoScroll = true;
            stackMatches.Dock = DockStyle.Fill;
            stackMatches.Location = new Point(0, 0);
            stackMatches.Name = "stackMatches";
            stackMatches.Size = new Size(1510, 531);
            stackMatches.TabIndex = 0;
            // 
            // pagination
            // 
            pagination.Dock = DockStyle.Bottom;
            pagination.Location = new Point(0, 531);
            pagination.Name = "pagination";
            pagination.Size = new Size(1510, 40);
            pagination.TabIndex = 1;
            pagination.Visible = false;
            pagination.ValueChanged += Pagination_ValueChanged;
            // 
            // panelStats
            // 
            panelStats.Dock = DockStyle.Fill;
            panelStats.Location = new Point(0, 0);
            panelStats.Name = "panelStats";
            panelStats.Size = new Size(1510, 571);
            panelStats.TabIndex = 1;
            panelStats.Visible = false;
            // 
            // lblStatus
            // 
            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Italic);
            lblStatus.ForeColor = SystemColors.GrayText;
            lblStatus.Height = 36;
            lblStatus.Text = "输入 puuid 或 名称#TAG 开始查询";
            // 
            // BattleQueryForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(lblStatus);
            Controls.Add(panelContent);
            Controls.Add(panelPlayer);
            Controls.Add(panelSearch);
            DoubleBuffered = true;
            Name = "BattleQueryForm";
            Size = new Size(1510, 781);
            panelSearch.ResumeLayout(false);
            panelPlayer.ResumeLayout(false);
            panelPlayerInfo.ResumeLayout(false);
            panelRanked.ResumeLayout(false);
            panelContent.ResumeLayout(false);
            panelHistory.ResumeLayout(false);
            ResumeLayout(false);
        }

        private FlowLayoutPanel panelSearch;
        private ComboBox cboFavorites;
        private AntdUI.Button btnLoadFavorite;
        private AntdUI.Input inpSearch;
        private AntdUI.Button btnSearch;
        private AntdUI.Button btnFavorite;
        private AntdUI.Button btnExport;
        private AntdUI.Button btnViewRecord;
        private AntdUI.Button btnViewStats;
        private Label lblPageSize;
        private ComboBox cboPageSize;
        private Panel panelPlayer;
        private AntdUI.Avatar avatarPlayer;
        private Panel panelPlayerInfo;
        private AntdUI.Label lblPlayerName;
        private AntdUI.Label lblPlayerTag;
        private AntdUI.Label lblPlayerLevel;
        private Panel panelRanked;
        private AntdUI.Label lblSoloTitle;
        private AntdUI.Label lblSoloStats;
        private AntdUI.Label lblFlexTitle;
        private AntdUI.Label lblFlexStats;
        private Panel panelContent;
        private Panel panelHistory;
        private Panel stackMatches;
        private AntdUI.Pagination pagination;
        private Panel panelStats;
        private AntdUI.Label lblStatus;
    }
}
