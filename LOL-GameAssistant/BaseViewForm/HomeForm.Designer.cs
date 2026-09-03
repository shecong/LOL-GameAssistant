namespace LOL_GameAssistant.BaseViewForm
{
    partial class HomeForm
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
            searchCard = new GradientPanel();
            flowSearch = new FlowLayoutPanel();
            inp_playname = new AntdUI.Input();
            Search_Player = new AntdUI.Button();
            refeash = new AntdUI.Button();
            btn_back = new AntdUI.Button();
            playerCard = new GradientPanel();
            lblPlayerCardTitle = new Label();
            play_HeadIcon = new RoundPictureBox();
            play_name = new AntdUI.Label();
            play_number = new AntdUI.Label();
            lblLevelCap = new Label();
            play_dj = new AntdUI.Label();
            lblXpCap = new Label();
            play_next = new AntdUI.Label();
            play_jd = new AntdUI.Progress();
            play_QF = new AntdUI.Label();
            rankedCard = new GradientPanel();
            lblRankedCardTitle = new Label();
            bottomStats = new FlowLayoutPanel();
            lblQueueHint = new Label();
            lblDwsCap = new Label();
            game_dws = new AntdUI.Label();
            lblJjsCap = new Label();
            game_jjs = new AntdUI.Label();
            lblJjsCountCap = new Label();
            game_jjscount = new AntdUI.Label();
            lblDqsdCap = new Label();
            game_dqsd = new AntdUI.Label();
            lblYcfCap = new Label();
            game_ycf = new AntdUI.Label();
            lblSjendCap = new Label();
            game_sjend = new AntdUI.Label();
            rankGrid = new TableLayoutPanel();
            soloPanel = new GradientPanel();
            pic_dsp = new PictureBox();
            game_dspT = new AntdUI.Label();
            game_dsp_sl = new AntdUI.Label();
            game_dsp_win = new AntdUI.Label();
            game_dsp_loss = new AntdUI.Label();
            game_dsp_lp = new AntdUI.Label();
            game_dsp_highest = new AntdUI.Label();
            flexPanel = new GradientPanel();
            pic_lhp = new PictureBox();
            game_lhpT = new AntdUI.Label();
            game_lhp_sl = new AntdUI.Label();
            game_lhp_win = new AntdUI.Label();
            game_lhp_loss = new AntdUI.Label();
            game_lhp_lp = new AntdUI.Label();
            game_lhp_highest = new AntdUI.Label();
            historyCard = new GradientPanel();
            historyHeader = new FlowLayoutPanel();
            lblHistoryTitle = new Label();
            lblPageSizeCap = new Label();
            game_count = new AntdUI.InputNumber();
            game_pagin = new AntdUI.Pagination();
            stackPanel1 = new FlowLayoutPanel();
            rootGrid.SuspendLayout();
            searchCard.SuspendLayout();
            flowSearch.SuspendLayout();
            playerCard.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)play_HeadIcon).BeginInit();
            rankedCard.SuspendLayout();
            bottomStats.SuspendLayout();
            rankGrid.SuspendLayout();
            soloPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pic_dsp).BeginInit();
            flexPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pic_lhp).BeginInit();
            historyCard.SuspendLayout();
            historyHeader.SuspendLayout();
            SuspendLayout();
            // 
            // rootGrid
            // 
            rootGrid.ColumnCount = 2;
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            rootGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            rootGrid.Controls.Add(searchCard, 0, 0);
            rootGrid.SetColumnSpan(searchCard, 2);
            rootGrid.Controls.Add(playerCard, 0, 1);
            rootGrid.Controls.Add(rankedCard, 1, 1);
            rootGrid.Controls.Add(historyCard, 0, 2);
            rootGrid.SetColumnSpan(historyCard, 2);
            rootGrid.Dock = DockStyle.Fill;
            rootGrid.Location = new Point(0, 0);
            rootGrid.Name = "rootGrid";
            rootGrid.Padding = new Padding(10);
            rootGrid.RowCount = 3;
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 224F));
            rootGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootGrid.Size = new Size(1331, 830);
            rootGrid.TabIndex = 0;
            // 
            // searchCard
            // 
            searchCard.Controls.Add(flowSearch);
            searchCard.CornerRadius = 12;
            searchCard.Dock = DockStyle.Fill;
            searchCard.DrawBorder = true;
            searchCard.BorderColor = Color.FromArgb(30, 30, 136, 229);
            searchCard.EndColor = Color.FromArgb(255, 255, 255);
            searchCard.Location = new Point(10, 10);
            searchCard.Margin = new Padding(0, 0, 0, 10);
            searchCard.Name = "searchCard";
            searchCard.Padding = new Padding(10);
            searchCard.StartColor = Color.FromArgb(245, 250, 255);
            searchCard.Size = new Size(1311, 48);
            searchCard.TabIndex = 0;
            // 
            // flowSearch
            // 
            flowSearch.Controls.Add(inp_playname);
            flowSearch.Controls.Add(Search_Player);
            flowSearch.Controls.Add(refeash);
            flowSearch.Controls.Add(btn_back);
            flowSearch.Dock = DockStyle.Fill;
            flowSearch.Padding = new Padding(4, 4, 0, 0);
            // 
            // inp_playname
            // 
            inp_playname.Location = new Point(4, 4);
            inp_playname.Margin = new Padding(0, 0, 10, 0);
            inp_playname.Name = "inp_playname";
            inp_playname.PlaceholderText = "输入 puuid 或 名称#TAG 查询玩家";
            inp_playname.Size = new Size(520, 34);
            inp_playname.TabIndex = 0;
            // 
            // Search_Player
            // 
            Search_Player.Location = new Point(534, 4);
            Search_Player.Margin = new Padding(0, 0, 8, 0);
            Search_Player.Name = "Search_Player";
            Search_Player.Size = new Size(90, 34);
            Search_Player.TabIndex = 1;
            Search_Player.Text = "查询玩家";
            Search_Player.Type = AntdUI.TTypeMini.Primary;
            Search_Player.Click += PlayInfo_Click;
            // 
            // refeash
            // 
            refeash.Location = new Point(632, 4);
            refeash.Margin = new Padding(0, 0, 8, 0);
            refeash.Name = "refeash";
            refeash.Size = new Size(80, 34);
            refeash.TabIndex = 2;
            refeash.Text = "刷新";
            refeash.Click += refeash_Click;
            // 
            // btn_back
            // 
            btn_back.Location = new Point(720, 4);
            btn_back.Margin = new Padding(0, 0, 8, 0);
            btn_back.Name = "btn_back";
            btn_back.Size = new Size(110, 34);
            btn_back.TabIndex = 3;
            btn_back.Text = "返回当前玩家";
            btn_back.Click += btn_back_Click;
            // 
            // playerCard
            // 
            playerCard.Controls.Add(lblPlayerCardTitle);
            playerCard.Controls.Add(play_HeadIcon);
            playerCard.Controls.Add(play_name);
            playerCard.Controls.Add(play_number);
            playerCard.Controls.Add(lblLevelCap);
            playerCard.Controls.Add(play_dj);
            playerCard.Controls.Add(lblXpCap);
            playerCard.Controls.Add(play_next);
            playerCard.Controls.Add(play_jd);
            playerCard.Controls.Add(play_QF);
            playerCard.CornerRadius = 12;
            playerCard.Dock = DockStyle.Fill;
            playerCard.DrawBorder = true;
            playerCard.BorderColor = Color.FromArgb(24, 0, 0, 0);
            playerCard.EndColor = Color.FromArgb(255, 255, 255);
            playerCard.Location = new Point(10, 68);
            playerCard.Margin = new Padding(0, 0, 8, 10);
            playerCard.Name = "playerCard";
            playerCard.Padding = new Padding(14);
            playerCard.StartColor = Color.FromArgb(252, 253, 255);
            playerCard.Size = new Size(437, 206);
            playerCard.TabIndex = 1;
            // 
            // lblPlayerCardTitle
            // 
            lblPlayerCardTitle.AutoSize = true;
            lblPlayerCardTitle.BackColor = Color.Transparent;
            lblPlayerCardTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblPlayerCardTitle.ForeColor = Color.FromArgb(60, 60, 60);
            lblPlayerCardTitle.Location = new Point(14, 8);
            lblPlayerCardTitle.Name = "lblPlayerCardTitle";
            lblPlayerCardTitle.Size = new Size(60, 18);
            lblPlayerCardTitle.TabIndex = 0;
            lblPlayerCardTitle.Text = "玩家信息";
            // 
            // play_HeadIcon
            // 
            play_HeadIcon.BorderColor = Color.FromArgb(220, 30, 136, 229);
            play_HeadIcon.BorderWidth = 2;
            play_HeadIcon.Location = new Point(14, 38);
            play_HeadIcon.Name = "play_HeadIcon";
            play_HeadIcon.Size = new Size(84, 84);
            play_HeadIcon.TabIndex = 1;
            play_HeadIcon.TabStop = false;
            // 
            // play_name
            // 
            play_name.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            play_name.Location = new Point(110, 38);
            play_name.Name = "play_name";
            play_name.Size = new Size(300, 30);
            play_name.TabIndex = 2;
            play_name.Text = "未知玩家";
            // 
            // play_number
            // 
            play_number.Font = new Font("Microsoft YaHei UI", 9.5F);
            play_number.ForeColor = SystemColors.GrayText;
            play_number.Location = new Point(110, 72);
            play_number.Name = "play_number";
            play_number.Size = new Size(200, 22);
            play_number.TabIndex = 3;
            play_number.Text = "";
            // 
            // lblLevelCap
            // 
            lblLevelCap.AutoSize = true;
            lblLevelCap.BackColor = Color.Transparent;
            lblLevelCap.Font = new Font("Microsoft YaHei UI", 9F);
            lblLevelCap.ForeColor = SystemColors.GrayText;
            lblLevelCap.Location = new Point(110, 100);
            lblLevelCap.Name = "lblLevelCap";
            lblLevelCap.Size = new Size(38, 16);
            lblLevelCap.TabIndex = 4;
            lblLevelCap.Text = "等级:";
            // 
            // play_dj
            // 
            play_dj.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            play_dj.ForeColor = Color.FromArgb(30, 136, 229);
            play_dj.Location = new Point(150, 98);
            play_dj.Name = "play_dj";
            play_dj.Size = new Size(50, 22);
            play_dj.TabIndex = 5;
            play_dj.Text = "-";
            // 
            // lblXpCap
            // 
            lblXpCap.AutoSize = true;
            lblXpCap.BackColor = Color.Transparent;
            lblXpCap.Font = new Font("Microsoft YaHei UI", 9F);
            lblXpCap.ForeColor = SystemColors.GrayText;
            lblXpCap.Location = new Point(210, 100);
            lblXpCap.Name = "lblXpCap";
            lblXpCap.Size = new Size(62, 16);
            lblXpCap.TabIndex = 6;
            lblXpCap.Text = "升级还需:";
            // 
            // play_next
            // 
            play_next.Font = new Font("Microsoft YaHei UI", 9.5F);
            play_next.ForeColor = Color.FromArgb(120, 80, 20);
            play_next.Location = new Point(274, 98);
            play_next.Name = "play_next";
            play_next.Size = new Size(130, 22);
            play_next.TabIndex = 7;
            play_next.Text = "-";
            // 
            // play_jd
            // 
            play_jd.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            play_jd.Location = new Point(14, 148);
            play_jd.Name = "play_jd";
            play_jd.Size = new Size(409, 30);
            play_jd.TabIndex = 8;
            play_jd.Text = "";
            // 
            // play_QF
            // 
            play_QF.Location = new Point(280, 178);
            play_QF.Name = "play_QF";
            play_QF.Size = new Size(60, 20);
            play_QF.TabIndex = 9;
            play_QF.Visible = false;
            // 
            // rankedCard
            // 
            rankedCard.Controls.Add(rankGrid);
            rankedCard.Controls.Add(bottomStats);
            rankedCard.Controls.Add(lblRankedCardTitle);
            rankedCard.CornerRadius = 12;
            rankedCard.Dock = DockStyle.Fill;
            rankedCard.DrawBorder = true;
            rankedCard.BorderColor = Color.FromArgb(24, 0, 0, 0);
            rankedCard.EndColor = Color.FromArgb(255, 255, 255);
            rankedCard.Location = new Point(455, 68);
            rankedCard.Margin = new Padding(0, 0, 0, 10);
            rankedCard.Name = "rankedCard";
            rankedCard.Padding = new Padding(14);
            rankedCard.StartColor = Color.FromArgb(252, 253, 255);
            rankedCard.Size = new Size(866, 206);
            rankedCard.TabIndex = 2;
            // 
            // lblRankedCardTitle
            // 
            lblRankedCardTitle.AutoSize = true;
            lblRankedCardTitle.BackColor = Color.Transparent;
            lblRankedCardTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblRankedCardTitle.ForeColor = Color.FromArgb(60, 60, 60);
            lblRankedCardTitle.Location = new Point(14, 8);
            lblRankedCardTitle.Name = "lblRankedCardTitle";
            lblRankedCardTitle.Size = new Size(60, 18);
            lblRankedCardTitle.TabIndex = 0;
            lblRankedCardTitle.Text = "排位信息";
            // 
            // bottomStats
            // 
            bottomStats.Controls.Add(lblQueueHint);
            bottomStats.Controls.Add(lblDwsCap);
            bottomStats.Controls.Add(game_dws);
            bottomStats.Controls.Add(lblJjsCap);
            bottomStats.Controls.Add(game_jjs);
            bottomStats.Controls.Add(lblJjsCountCap);
            bottomStats.Controls.Add(game_jjscount);
            bottomStats.Controls.Add(lblDqsdCap);
            bottomStats.Controls.Add(game_dqsd);
            bottomStats.Controls.Add(lblYcfCap);
            bottomStats.Controls.Add(game_ycf);
            bottomStats.Controls.Add(lblSjendCap);
            bottomStats.Controls.Add(game_sjend);
            bottomStats.Dock = DockStyle.Bottom;
            bottomStats.Height = 34;
            bottomStats.Padding = new Padding(0, 6, 0, 0);
            // 
            // lblQueueHint
            // 
            lblQueueHint.AutoSize = true;
            lblQueueHint.BackColor = Color.Transparent;
            lblQueueHint.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            lblQueueHint.ForeColor = Color.FromArgb(30, 136, 229);
            lblQueueHint.Margin = new Padding(0, 5, 6, 0);
            lblQueueHint.Text = "单双排:";
            // 
            // lblDwsCap
            // 
            lblDwsCap.AutoSize = true;
            lblDwsCap.BackColor = Color.Transparent;
            lblDwsCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDwsCap.ForeColor = SystemColors.GrayText;
            lblDwsCap.Margin = new Padding(0, 5, 2, 0);
            lblDwsCap.Text = "定级赛:";
            // 
            // game_dws
            // 
            game_dws.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_dws.Margin = new Padding(0, 3, 10, 0);
            game_dws.Size = new Size(80, 20);
            game_dws.Text = "-";
            // 
            // lblJjsCap
            // 
            lblJjsCap.AutoSize = true;
            lblJjsCap.BackColor = Color.Transparent;
            lblJjsCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblJjsCap.ForeColor = SystemColors.GrayText;
            lblJjsCap.Margin = new Padding(0, 5, 2, 0);
            lblJjsCap.Text = "晋级赛:";
            // 
            // game_jjs
            // 
            game_jjs.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_jjs.Margin = new Padding(0, 3, 10, 0);
            game_jjs.Size = new Size(210, 20);
            game_jjs.Text = "-";
            // 
            // lblJjsCountCap
            // 
            lblJjsCountCap.AutoSize = true;
            lblJjsCountCap.BackColor = Color.Transparent;
            lblJjsCountCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblJjsCountCap.ForeColor = SystemColors.GrayText;
            lblJjsCountCap.Margin = new Padding(0, 5, 2, 0);
            lblJjsCountCap.Text = "场数:";
            // 
            // game_jjscount
            // 
            game_jjscount.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_jjscount.Margin = new Padding(0, 3, 10, 0);
            game_jjscount.Size = new Size(70, 20);
            game_jjscount.Text = "-";
            // 
            // lblDqsdCap
            // 
            lblDqsdCap.AutoSize = true;
            lblDqsdCap.BackColor = Color.Transparent;
            lblDqsdCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDqsdCap.ForeColor = SystemColors.GrayText;
            lblDqsdCap.Margin = new Padding(0, 5, 2, 0);
            lblDqsdCap.Text = "胜点:";
            // 
            // game_dqsd
            // 
            game_dqsd.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_dqsd.Margin = new Padding(0, 3, 10, 0);
            game_dqsd.Size = new Size(70, 20);
            game_dqsd.Text = "-";
            // 
            // lblYcfCap
            // 
            lblYcfCap.AutoSize = true;
            lblYcfCap.BackColor = Color.Transparent;
            lblYcfCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblYcfCap.ForeColor = SystemColors.GrayText;
            lblYcfCap.Margin = new Padding(0, 5, 2, 0);
            lblYcfCap.Text = "隐藏分:";
            // 
            // game_ycf
            // 
            game_ycf.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_ycf.Margin = new Padding(0, 3, 10, 0);
            game_ycf.Size = new Size(110, 20);
            game_ycf.Text = "-";
            // 
            // lblSjendCap
            // 
            lblSjendCap.AutoSize = true;
            lblSjendCap.BackColor = Color.Transparent;
            lblSjendCap.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSjendCap.ForeColor = SystemColors.GrayText;
            lblSjendCap.Margin = new Padding(0, 5, 2, 0);
            lblSjendCap.Text = "赛季结束:";
            // 
            // game_sjend
            // 
            game_sjend.Font = new Font("Microsoft YaHei UI", 8.5F);
            game_sjend.Margin = new Padding(0, 3, 4, 0);
            game_sjend.Size = new Size(100, 20);
            game_sjend.Text = "-";
            // 
            // rankGrid
            // 
            rankGrid.ColumnCount = 2;
            rankGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rankGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rankGrid.Controls.Add(soloPanel, 0, 0);
            rankGrid.Controls.Add(flexPanel, 1, 0);
            rankGrid.Dock = DockStyle.Fill;
            rankGrid.Location = new Point(14, 34);
            rankGrid.Name = "rankGrid";
            rankGrid.RowCount = 1;
            rankGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rankGrid.Size = new Size(838, 158);
            rankGrid.TabIndex = 1;
            // 
            // soloPanel
            // 
            soloPanel.Controls.Add(pic_dsp);
            soloPanel.Controls.Add(game_dspT);
            soloPanel.Controls.Add(game_dsp_sl);
            soloPanel.Controls.Add(game_dsp_win);
            soloPanel.Controls.Add(game_dsp_loss);
            soloPanel.Controls.Add(game_dsp_lp);
            soloPanel.Controls.Add(game_dsp_highest);
            soloPanel.CornerRadius = 10;
            soloPanel.Dock = DockStyle.Fill;
            soloPanel.DrawBorder = true;
            soloPanel.BorderColor = Color.FromArgb(40, 30, 136, 229);
            soloPanel.EndColor = Color.FromArgb(245, 250, 255);
            soloPanel.Location = new Point(0, 0);
            soloPanel.Margin = new Padding(0, 0, 6, 0);
            soloPanel.Name = "soloPanel";
            soloPanel.StartColor = Color.FromArgb(255, 255, 255);
            soloPanel.Size = new Size(413, 158);
            soloPanel.TabIndex = 0;
            // 
            // pic_dsp
            // 
            pic_dsp.Location = new Point(14, 18);
            pic_dsp.Name = "pic_dsp";
            pic_dsp.Size = new Size(56, 56);
            pic_dsp.SizeMode = PictureBoxSizeMode.Zoom;
            pic_dsp.TabIndex = 0;
            pic_dsp.TabStop = false;
            // 
            // game_dspT
            // 
            game_dspT.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            game_dspT.ForeColor = Color.FromArgb(30, 136, 229);
            game_dspT.Location = new Point(80, 16);
            game_dspT.Name = "game_dspT";
            game_dspT.Size = new Size(180, 26);
            game_dspT.TabIndex = 1;
            game_dspT.Text = "单双排";
            // 
            // game_dsp_sl
            // 
            game_dsp_sl.Font = new Font("Microsoft YaHei UI", 9F);
            game_dsp_sl.ForeColor = SystemColors.GrayText;
            game_dsp_sl.Location = new Point(80, 44);
            game_dsp_sl.Name = "game_dsp_sl";
            game_dsp_sl.Size = new Size(180, 20);
            game_dsp_sl.TabIndex = 2;
            game_dsp_sl.Text = "胜率 -";
            // 
            // game_dsp_win
            // 
            game_dsp_win.Font = new Font("Microsoft YaHei UI", 9F);
            game_dsp_win.ForeColor = Color.FromArgb(46, 125, 50);
            game_dsp_win.Location = new Point(16, 90);
            game_dsp_win.Name = "game_dsp_win";
            game_dsp_win.Size = new Size(120, 22);
            game_dsp_win.TabIndex = 3;
            game_dsp_win.Text = "胜场 -";
            // 
            // game_dsp_loss
            // 
            game_dsp_loss.Font = new Font("Microsoft YaHei UI", 9F);
            game_dsp_loss.ForeColor = Color.FromArgb(198, 40, 40);
            game_dsp_loss.Location = new Point(150, 90);
            game_dsp_loss.Name = "game_dsp_loss";
            game_dsp_loss.Size = new Size(120, 22);
            game_dsp_loss.TabIndex = 4;
            game_dsp_loss.Text = "负场 -";
            // 
            // game_dsp_lp
            // 
            game_dsp_lp.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            game_dsp_lp.ForeColor = Color.FromArgb(255, 152, 0);
            game_dsp_lp.Location = new Point(16, 118);
            game_dsp_lp.Name = "game_dsp_lp";
            game_dsp_lp.Size = new Size(120, 22);
            game_dsp_lp.TabIndex = 5;
            game_dsp_lp.Text = "LP -";
            // 
            // game_dsp_highest
            // 
            game_dsp_highest.Font = new Font("Microsoft YaHei UI", 9F);
            game_dsp_highest.ForeColor = SystemColors.GrayText;
            game_dsp_highest.Location = new Point(150, 118);
            game_dsp_highest.Name = "game_dsp_highest";
            game_dsp_highest.Size = new Size(220, 22);
            game_dsp_highest.TabIndex = 6;
            game_dsp_highest.Text = "最高 -";
            // 
            // flexPanel
            // 
            flexPanel.Controls.Add(pic_lhp);
            flexPanel.Controls.Add(game_lhpT);
            flexPanel.Controls.Add(game_lhp_sl);
            flexPanel.Controls.Add(game_lhp_win);
            flexPanel.Controls.Add(game_lhp_loss);
            flexPanel.Controls.Add(game_lhp_lp);
            flexPanel.Controls.Add(game_lhp_highest);
            flexPanel.CornerRadius = 10;
            flexPanel.Dock = DockStyle.Fill;
            flexPanel.DrawBorder = true;
            flexPanel.BorderColor = Color.FromArgb(40, 211, 47, 47);
            flexPanel.EndColor = Color.FromArgb(255, 247, 245);
            flexPanel.Location = new Point(425, 0);
            flexPanel.Margin = new Padding(6, 0, 0, 0);
            flexPanel.Name = "flexPanel";
            flexPanel.StartColor = Color.FromArgb(255, 255, 255);
            flexPanel.Size = new Size(413, 158);
            flexPanel.TabIndex = 1;
            // 
            // pic_lhp
            // 
            pic_lhp.Location = new Point(14, 18);
            pic_lhp.Name = "pic_lhp";
            pic_lhp.Size = new Size(56, 56);
            pic_lhp.SizeMode = PictureBoxSizeMode.Zoom;
            pic_lhp.TabIndex = 0;
            pic_lhp.TabStop = false;
            // 
            // game_lhpT
            // 
            game_lhpT.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            game_lhpT.ForeColor = Color.FromArgb(211, 47, 47);
            game_lhpT.Location = new Point(80, 16);
            game_lhpT.Name = "game_lhpT";
            game_lhpT.Size = new Size(180, 26);
            game_lhpT.TabIndex = 1;
            game_lhpT.Text = "灵活组排";
            // 
            // game_lhp_sl
            // 
            game_lhp_sl.Font = new Font("Microsoft YaHei UI", 9F);
            game_lhp_sl.ForeColor = SystemColors.GrayText;
            game_lhp_sl.Location = new Point(80, 44);
            game_lhp_sl.Name = "game_lhp_sl";
            game_lhp_sl.Size = new Size(180, 20);
            game_lhp_sl.TabIndex = 2;
            game_lhp_sl.Text = "胜率 -";
            // 
            // game_lhp_win
            // 
            game_lhp_win.Font = new Font("Microsoft YaHei UI", 9F);
            game_lhp_win.ForeColor = Color.FromArgb(46, 125, 50);
            game_lhp_win.Location = new Point(16, 90);
            game_lhp_win.Name = "game_lhp_win";
            game_lhp_win.Size = new Size(120, 22);
            game_lhp_win.TabIndex = 3;
            game_lhp_win.Text = "胜场 -";
            // 
            // game_lhp_loss
            // 
            game_lhp_loss.Font = new Font("Microsoft YaHei UI", 9F);
            game_lhp_loss.ForeColor = Color.FromArgb(198, 40, 40);
            game_lhp_loss.Location = new Point(150, 90);
            game_lhp_loss.Name = "game_lhp_loss";
            game_lhp_loss.Size = new Size(120, 22);
            game_lhp_loss.TabIndex = 4;
            game_lhp_loss.Text = "负场 -";
            // 
            // game_lhp_lp
            // 
            game_lhp_lp.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            game_lhp_lp.ForeColor = Color.FromArgb(255, 152, 0);
            game_lhp_lp.Location = new Point(16, 118);
            game_lhp_lp.Name = "game_lhp_lp";
            game_lhp_lp.Size = new Size(120, 22);
            game_lhp_lp.TabIndex = 5;
            game_lhp_lp.Text = "LP -";
            // 
            // game_lhp_highest
            // 
            game_lhp_highest.Font = new Font("Microsoft YaHei UI", 9F);
            game_lhp_highest.ForeColor = SystemColors.GrayText;
            game_lhp_highest.Location = new Point(150, 118);
            game_lhp_highest.Name = "game_lhp_highest";
            game_lhp_highest.Size = new Size(220, 22);
            game_lhp_highest.TabIndex = 6;
            game_lhp_highest.Text = "最高 -";
            // 
            // historyCard
            // 
            historyCard.Controls.Add(stackPanel1);
            historyCard.Controls.Add(historyHeader);
            historyCard.CornerRadius = 12;
            historyCard.Dock = DockStyle.Fill;
            historyCard.DrawBorder = true;
            historyCard.BorderColor = Color.FromArgb(24, 0, 0, 0);
            historyCard.EndColor = Color.FromArgb(255, 255, 255);
            historyCard.Location = new Point(10, 302);
            historyCard.Margin = new Padding(0, 0, 0, 0);
            historyCard.Name = "historyCard";
            historyCard.Padding = new Padding(14, 8, 14, 10);
            historyCard.StartColor = Color.FromArgb(252, 253, 255);
            historyCard.Size = new Size(1311, 518);
            historyCard.TabIndex = 3;
            // 
            // historyHeader
            // 
            historyHeader.Controls.Add(lblHistoryTitle);
            historyHeader.Controls.Add(lblPageSizeCap);
            historyHeader.Controls.Add(game_count);
            historyHeader.Controls.Add(game_pagin);
            historyHeader.Dock = DockStyle.Top;
            historyHeader.Height = 40;
            historyHeader.Padding = new Padding(0, 4, 0, 0);
            // 
            // lblHistoryTitle
            // 
            lblHistoryTitle.AutoSize = true;
            lblHistoryTitle.BackColor = Color.Transparent;
            lblHistoryTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblHistoryTitle.ForeColor = Color.FromArgb(60, 60, 60);
            lblHistoryTitle.Margin = new Padding(0, 6, 20, 0);
            lblHistoryTitle.Text = "近期战绩";
            // 
            // lblPageSizeCap
            // 
            lblPageSizeCap.AutoSize = true;
            lblPageSizeCap.BackColor = Color.Transparent;
            lblPageSizeCap.Font = new Font("Microsoft YaHei UI", 9F);
            lblPageSizeCap.ForeColor = SystemColors.GrayText;
            lblPageSizeCap.Margin = new Padding(0, 9, 4, 0);
            lblPageSizeCap.Text = "每页";
            // 
            // game_count
            // 
            game_count.Location = new Point(0, 4);
            game_count.Margin = new Padding(0, 0, 20, 0);
            game_count.Name = "game_count";
            game_count.Size = new Size(70, 30);
            game_count.TabIndex = 1;
            game_count.Text = "10";
            game_count.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // game_pagin
            // 
            game_pagin.Location = new Point(190, 4);
            game_pagin.Margin = new Padding(20, 0, 0, 0);
            game_pagin.Name = "game_pagin";
            game_pagin.Size = new Size(520, 30);
            game_pagin.TabIndex = 2;
            game_pagin.ValueChanged += game_pagin_ValueChanged;
            game_pagin.Click += game_pagin_Click;
            // 
            // stackPanel1
            // 
            stackPanel1.AutoScroll = true;
            stackPanel1.Dock = DockStyle.Fill;
            stackPanel1.FlowDirection = FlowDirection.LeftToRight;
            stackPanel1.Location = new Point(14, 48);
            stackPanel1.Name = "stackPanel1";
            stackPanel1.Padding = new Padding(0, 0, 12, 0);
            stackPanel1.Size = new Size(1283, 460);
            stackPanel1.TabIndex = 0;
            stackPanel1.WrapContents = true;
            // 
            // HomeForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(240, 243, 248);
            Controls.Add(rootGrid);
            DoubleBuffered = true;
            Name = "HomeForm";
            Size = new Size(1331, 830);
            Load += HomeForm_Load;
            rootGrid.ResumeLayout(false);
            searchCard.ResumeLayout(false);
            flowSearch.ResumeLayout(false);
            playerCard.ResumeLayout(false);
            playerCard.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)play_HeadIcon).EndInit();
            rankedCard.ResumeLayout(false);
            rankedCard.PerformLayout();
            bottomStats.ResumeLayout(false);
            bottomStats.PerformLayout();
            rankGrid.ResumeLayout(false);
            soloPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pic_dsp).EndInit();
            flexPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pic_lhp).EndInit();
            historyCard.ResumeLayout(false);
            historyHeader.ResumeLayout(false);
            historyHeader.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel rootGrid;
        private GradientPanel searchCard;
        private FlowLayoutPanel flowSearch;
        private AntdUI.Input inp_playname;
        private AntdUI.Button Search_Player;
        private AntdUI.Button refeash;
        private AntdUI.Button btn_back;
        private GradientPanel playerCard;
        private Label lblPlayerCardTitle;
        private RoundPictureBox play_HeadIcon;
        private AntdUI.Label play_name;
        private AntdUI.Label play_number;
        private Label lblLevelCap;
        private AntdUI.Label play_dj;
        private Label lblXpCap;
        private AntdUI.Label play_next;
        private AntdUI.Progress play_jd;
        private AntdUI.Label play_QF;
        private GradientPanel rankedCard;
        private Label lblRankedCardTitle;
        private FlowLayoutPanel bottomStats;
        private Label lblQueueHint;
        private Label lblDwsCap;
        private AntdUI.Label game_dws;
        private Label lblJjsCap;
        private AntdUI.Label game_jjs;
        private Label lblJjsCountCap;
        private AntdUI.Label game_jjscount;
        private Label lblDqsdCap;
        private AntdUI.Label game_dqsd;
        private Label lblYcfCap;
        private AntdUI.Label game_ycf;
        private Label lblSjendCap;
        private AntdUI.Label game_sjend;
        private TableLayoutPanel rankGrid;
        private GradientPanel soloPanel;
        private PictureBox pic_dsp;
        private AntdUI.Label game_dspT;
        private AntdUI.Label game_dsp_sl;
        private AntdUI.Label game_dsp_win;
        private AntdUI.Label game_dsp_loss;
        private AntdUI.Label game_dsp_lp;
        private AntdUI.Label game_dsp_highest;
        private GradientPanel flexPanel;
        private PictureBox pic_lhp;
        private AntdUI.Label game_lhpT;
        private AntdUI.Label game_lhp_sl;
        private AntdUI.Label game_lhp_win;
        private AntdUI.Label game_lhp_loss;
        private AntdUI.Label game_lhp_lp;
        private AntdUI.Label game_lhp_highest;
        private GradientPanel historyCard;
        private FlowLayoutPanel historyHeader;
        private Label lblHistoryTitle;
        private Label lblPageSizeCap;
        private AntdUI.InputNumber game_count;
        private AntdUI.Pagination game_pagin;
        private FlowLayoutPanel stackPanel1;
    }
}
