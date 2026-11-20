using Panel = AntdUI.Panel;
using ComboBox = AntdUI.Select;
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
            btnLoadFavorite = new AntdUI.Button();
            inpSearch = new AntdUI.Input();
            btnSearch = new AntdUI.Button();
            btnFavorite = new AntdUI.Button();
            btnExport = new AntdUI.Button();
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
            stackMatches = new LOL_GameAssistant.Helper.MatchScrollPanel();
            pagination = new AntdUI.Pagination();
            lblStatus = new AntdUI.Label();
            panelPlayer.SuspendLayout();
            panelPlayerInfo.SuspendLayout();
            panelRanked.SuspendLayout();
            panelContent.SuspendLayout();
            panelHistory.SuspendLayout();
            SuspendLayout();
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
            // panelPlayer
            // 
            panelPlayer.Controls.Add(panelPlayerInfo);
            panelPlayer.Controls.Add(panelRanked);
            panelPlayer.Controls.Add(avatarPlayer);
            panelPlayer.Dock = DockStyle.Top;
            panelPlayer.Location = new Point(0, 62);
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
            panelContent.Controls.Add(panelHistory);
            panelContent.Dock = DockStyle.Fill;
            panelContent.Location = new Point(0, 180);
            panelContent.Name = "panelContent";
            panelContent.Size = new Size(1510, 565);
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
            DoubleBuffered = true;
            Name = "BattleQueryForm";
            Size = new Size(1510, 781);
            panelPlayer.ResumeLayout(false);
            panelPlayerInfo.ResumeLayout(false);
            panelRanked.ResumeLayout(false);
            panelContent.ResumeLayout(false);
            panelHistory.ResumeLayout(false);
            ResumeLayout(false);
        }

        private AntdUI.Button btnLoadFavorite;
        private AntdUI.Input inpSearch;
        private AntdUI.Button btnSearch;
        private AntdUI.Button btnFavorite;
        private AntdUI.Button btnExport;
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
        private LOL_GameAssistant.Helper.MatchScrollPanel stackMatches;
        private AntdUI.Pagination pagination;
        private AntdUI.Label lblStatus;
    }
}
