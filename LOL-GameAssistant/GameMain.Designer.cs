namespace LOL_GameAssistant
{
    partial class GameMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            AntdUI.Tabs.StyleLine styleLine1 = new AntdUI.Tabs.StyleLine();
            tabs1 = new AntdUI.Tabs();
            tabPage1 = new AntdUI.TabPage();
            tab0_grid1 = new AntdUI.GridPanel();
            tabPage2 = new AntdUI.TabPage();
            tab1_grid1 = new AntdUI.GridPanel();
            tabPage3 = new AntdUI.TabPage();
            tabPage4 = new AntdUI.TabPage();
            tab4_grid1 = new AntdUI.GridPanel();
            tabPage5 = new AntdUI.TabPage();
            tabPage6 = new AntdUI.TabPage();
            tab5_grid1 = new AntdUI.GridPanel();
            HeadContent = new AntdUI.PageHeader();
            dj_refresh = new AntdUI.Button();
            panel_main = new AntdUI.Panel();
            gameFlowPhaseName = new AntdUI.Label();
            grid_main = new AntdUI.GridPanel();
            tabs1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            tabPage4.SuspendLayout();
            tabPage6.SuspendLayout();
            HeadContent.SuspendLayout();
            panel_main.SuspendLayout();
            grid_main.SuspendLayout();
            SuspendLayout();
            // 
            // tabs1
            // 
            tabs1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tabs1.Controls.Add(tabPage1);
            tabs1.Controls.Add(tabPage2);
            tabs1.Controls.Add(tabPage3);
            tabs1.Controls.Add(tabPage4);
            tabs1.Controls.Add(tabPage5);
            tabs1.Controls.Add(tabPage6);
            grid_main.SetIndex(tabs1, 1);
            tabs1.Location = new Point(3, 3);
            tabs1.Name = "tabs1";
            tabs1.Pages.Add(tabPage1);
            tabs1.Pages.Add(tabPage2);
            tabs1.Pages.Add(tabPage3);
            tabs1.Pages.Add(tabPage4);
            tabs1.Pages.Add(tabPage5);
            tabs1.Pages.Add(tabPage6);
            tabs1.Size = new Size(1510, 811);
            tabs1.Style = styleLine1;
            tabs1.TabIndex = 2;
            tabs1.Text = "tabs1";
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(tab0_grid1);
            tabPage1.Dock = DockStyle.Fill;
            tabPage1.Location = new Point(0, 30);
            tabPage1.Name = "tabPage1";
            tabPage1.Size = new Size(1510, 781);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "首页";
            // 
            // tab0_grid1
            // 
            tab0_grid1.Dock = DockStyle.Fill;
            tab0_grid1.Location = new Point(0, 0);
            tab0_grid1.Name = "tab0_grid1";
            tab0_grid1.Size = new Size(1510, 781);
            tab0_grid1.Span = "100%";
            tab0_grid1.TabIndex = 0;
            tab0_grid1.Text = "gridPanel1";
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(tab1_grid1);
            tabPage2.Dock = DockStyle.Fill;
            tabPage2.Location = new Point(0, 30);
            tabPage2.Name = "tabPage2";
            tabPage2.Size = new Size(1510, 781);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "对局";
            // 
            // tab1_grid1
            // 
            tab1_grid1.Dock = DockStyle.Fill;
            tab1_grid1.Location = new Point(0, 0);
            tab1_grid1.Name = "tab1_grid1";
            tab1_grid1.Size = new Size(1510, 781);
            tab1_grid1.Span = "100%";
            tab1_grid1.TabIndex = 0;
            tab1_grid1.Text = "gridPanel1";
            // 
            // tabPage3
            // 
            tabPage3.Dock = DockStyle.Fill;
            tabPage3.Location = new Point(0, 30);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(1510, 781);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "战绩查询";
            // 
            // tabPage4
            // 
            tabPage4.Controls.Add(tab4_grid1);
            tabPage4.Dock = DockStyle.Fill;
            tabPage4.Location = new Point(0, 30);
            tabPage4.Name = "tabPage4";
            tabPage4.Size = new Size(1510, 781);
            tabPage4.TabIndex = 3;
            tabPage4.Text = "关于";
            // 
            // tab4_grid1
            // 
            tab4_grid1.Dock = DockStyle.Fill;
            tab4_grid1.Location = new Point(0, 0);
            tab4_grid1.Name = "tab4_grid1";
            tab4_grid1.Size = new Size(1510, 781);
            tab4_grid1.Span = "";
            tab4_grid1.TabIndex = 0;
            tab4_grid1.Text = "gridPanel1";
            // 
            // tabPage5
            // 
            tabPage5.Dock = DockStyle.Fill;
            tabPage5.Location = new Point(0, 30);
            tabPage5.Name = "tabPage5";
            tabPage5.Size = new Size(1510, 781);
            tabPage5.TabIndex = 4;
            tabPage5.Text = "设置";
            // 
            // tabPage6
            // 
            tabPage6.Controls.Add(tab5_grid1);
            tabPage6.Dock = DockStyle.Fill;
            tabPage6.Location = new Point(0, 30);
            tabPage6.Name = "tabPage6";
            tabPage6.Size = new Size(1510, 781);
            tabPage6.TabIndex = 5;
            tabPage6.Text = "日志";
            // 
            // tab5_grid1
            // 
            tab5_grid1.Dock = DockStyle.Fill;
            tab5_grid1.Location = new Point(0, 0);
            tab5_grid1.Name = "tab5_grid1";
            tab5_grid1.Size = new Size(1510, 781);
            tab5_grid1.Span = "100%";
            tab5_grid1.TabIndex = 1;
            tab5_grid1.Text = "gridPanel8";
            // 
            // HeadContent
            // 
            HeadContent.Controls.Add(dj_refresh);
            HeadContent.Dock = DockStyle.Top;
            HeadContent.Location = new Point(0, 0);
            HeadContent.Name = "HeadContent";
            HeadContent.ShowButton = true;
            HeadContent.ShowIcon = true;
            HeadContent.Size = new Size(1516, 29);
            HeadContent.TabIndex = 1;
            HeadContent.Text = "sc";
            // 
            // dj_refresh
            // 
            dj_refresh.Dock = DockStyle.Right;
            dj_refresh.Location = new Point(1297, 0);
            dj_refresh.Name = "dj_refresh";
            dj_refresh.Size = new Size(75, 29);
            dj_refresh.TabIndex = 0;
            dj_refresh.Text = "刷新";
            dj_refresh.Click += dj_refresh_Click;
            // 
            // panel_main
            // 
            panel_main.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel_main.BackColor = SystemColors.Control;
            panel_main.Controls.Add(gameFlowPhaseName);
            grid_main.SetIndex(panel_main, 2);
            panel_main.Location = new Point(3, 820);
            panel_main.Name = "panel_main";
            panel_main.Size = new Size(1510, 37);
            panel_main.TabIndex = 3;
            panel_main.Text = "panel1";
            // 
            // gameFlowPhaseName
            // 
            gameFlowPhaseName.ColorExtend = "135,#FF0000,#00FF00";
            gameFlowPhaseName.Dock = DockStyle.Right;
            gameFlowPhaseName.Location = new Point(1204, 0);
            gameFlowPhaseName.Name = "gameFlowPhaseName";
            gameFlowPhaseName.Prefix = "游戏状态：";
            gameFlowPhaseName.Size = new Size(306, 37);
            gameFlowPhaseName.TabIndex = 2;
            gameFlowPhaseName.Text = "";
            // 
            // grid_main
            // 
            grid_main.Controls.Add(tabs1);
            grid_main.Controls.Add(panel_main);
            grid_main.Dock = DockStyle.Fill;
            grid_main.Location = new Point(0, 29);
            grid_main.Name = "grid_main";
            grid_main.Size = new Size(1516, 860);
            grid_main.Span = "100%; 100%;-95% 5%";
            grid_main.TabIndex = 4;
            grid_main.Text = "gridPanel1";
            // 
            // GameMain
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            ClientSize = new Size(1516, 889);
            Controls.Add(grid_main);
            Controls.Add(HeadContent);
            HelpButton = true;
            Name = "GameMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Form1";
            Load += GameMain_Load;
            tabs1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage2.ResumeLayout(false);
            tabPage4.ResumeLayout(false);
            tabPage6.ResumeLayout(false);
            HeadContent.ResumeLayout(false);
            panel_main.ResumeLayout(false);
            grid_main.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Tabs tabs1;
        private AntdUI.TabPage tabPage2;
        private AntdUI.TabPage tabPage3;
        private AntdUI.TabPage tabPage4;
        private AntdUI.TabPage tabPage5;
        private AntdUI.PageHeader HeadContent;
        private AntdUI.Splitter splitter2;
        private AntdUI.StackPanel stack1;
        private AntdUI.StackPanel stack2;
        private AntdUI.TabPage tabPage6;
        private AntdUI.GridPanel tab5_grid1;
        private AntdUI.GridPanel tab1_grid1;
        private AntdUI.TabPage tabPage1;
        private AntdUI.GridPanel tab0_grid1;
        private AntdUI.GridPanel tab4_grid1;
        private AntdUI.Button dj_refresh;
        private AntdUI.Panel panel_main;
        private AntdUI.GridPanel grid_main;
        private AntdUI.Label gameFlowPhaseName;
    }
}
