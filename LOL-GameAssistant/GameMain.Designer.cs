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
            gridPanel1 = new AntdUI.GridPanel();
            tabPage2 = new AntdUI.TabPage();
            tabPage3 = new AntdUI.TabPage();
            tabPage4 = new AntdUI.TabPage();
            tabPage5 = new AntdUI.TabPage();
            HeadContent = new AntdUI.PageHeader();
            divider1 = new AntdUI.Divider();
            play_HeadIcon = new AntdUI.Avatar();
            play_name = new AntdUI.Label();
            play_number = new AntdUI.Label();
            play_QF = new AntdUI.Label();
            btn_back = new AntdUI.Button();
            refeash = new AntdUI.Button();
            PlayInfo = new AntdUI.Button();
            tabs1.SuspendLayout();
            tabPage1.SuspendLayout();
            gridPanel1.SuspendLayout();
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
            tabs1.Location = new Point(0, 35);
            tabs1.Name = "tabs1";
            tabs1.Pages.Add(tabPage1);
            tabs1.Pages.Add(tabPage2);
            tabs1.Pages.Add(tabPage3);
            tabs1.Pages.Add(tabPage4);
            tabs1.Pages.Add(tabPage5);
            tabs1.Size = new Size(815, 449);
            tabs1.Style = styleLine1;
            tabs1.TabIndex = 2;
            tabs1.Text = "tabs1";
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(gridPanel1);
            tabPage1.Dock = DockStyle.Fill;
            tabPage1.Location = new Point(0, 30);
            tabPage1.Name = "tabPage1";
            tabPage1.Size = new Size(815, 419);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "首页";
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(PlayInfo);
            gridPanel1.Controls.Add(refeash);
            gridPanel1.Controls.Add(btn_back);
            gridPanel1.Controls.Add(play_QF);
            gridPanel1.Controls.Add(play_number);
            gridPanel1.Controls.Add(play_name);
            gridPanel1.Controls.Add(play_HeadIcon);
            gridPanel1.Controls.Add(divider1);
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(815, 419);
            gridPanel1.Span = "";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // tabPage2
            // 
            tabPage2.Dock = DockStyle.Fill;
            tabPage2.Location = new Point(0, 30);
            tabPage2.Name = "tabPage2";
            tabPage2.Size = new Size(815, 419);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "对局";
            // 
            // tabPage3
            // 
            tabPage3.Dock = DockStyle.Fill;
            tabPage3.Location = new Point(0, 30);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(815, 419);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "战绩查询";
            // 
            // tabPage4
            // 
            tabPage4.Dock = DockStyle.Fill;
            tabPage4.Location = new Point(0, 30);
            tabPage4.Name = "tabPage4";
            tabPage4.Size = new Size(815, 419);
            tabPage4.TabIndex = 3;
            tabPage4.Text = "关于";
            // 
            // tabPage5
            // 
            tabPage5.Dock = DockStyle.Fill;
            tabPage5.Location = new Point(0, 30);
            tabPage5.Name = "tabPage5";
            tabPage5.Size = new Size(815, 419);
            tabPage5.TabIndex = 4;
            tabPage5.Text = "设置";
            // 
            // HeadContent
            // 
            HeadContent.Dock = DockStyle.Top;
            HeadContent.Location = new Point(0, 0);
            HeadContent.Name = "HeadContent";
            HeadContent.ShowButton = true;
            HeadContent.ShowIcon = true;
            HeadContent.Size = new Size(815, 29);
            HeadContent.TabIndex = 1;
            HeadContent.Text = "sc";
            // 
            // divider1
            // 
            divider1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            divider1.BadgeSvg = "BarChartOutlined";
            divider1.Location = new Point(3, 87);
            divider1.Name = "divider1";
            divider1.Size = new Size(808, 23);
            divider1.TabIndex = 0;
            divider1.Text = "战绩区";
            // 
            // play_HeadIcon
            // 
            play_HeadIcon.Location = new Point(12, 10);
            play_HeadIcon.Name = "play_HeadIcon";
            play_HeadIcon.Size = new Size(75, 67);
            play_HeadIcon.TabIndex = 1;
            play_HeadIcon.Text = "无";
            // 
            // play_name
            // 
            play_name.ColorExtend = "135,#FF0000,#00FF00";
            play_name.Location = new Point(93, 10);
            play_name.Name = "play_name";
            play_name.Size = new Size(180, 26);
            play_name.TabIndex = 2;
            play_name.Text = "召唤师名称";
            // 
            // play_number
            // 
            play_number.ColorExtend = "135,#FF0000,#00FF00";
            play_number.Location = new Point(93, 43);
            play_number.Name = "play_number";
            play_number.Size = new Size(78, 26);
            play_number.TabIndex = 3;
            play_number.Text = "召唤师编号";
            // 
            // play_QF
            // 
            play_QF.ColorExtend = "135,#FF0000,#00FF00";
            play_QF.Location = new Point(177, 43);
            play_QF.Name = "play_QF";
            play_QF.PrefixSvg = "RocketOutlined";
            play_QF.Size = new Size(96, 26);
            play_QF.SuffixSvg = "";
            play_QF.TabIndex = 4;
            play_QF.Text = "当前区服";
            // 
            // btn_back
            // 
            btn_back.Location = new Point(729, 3);
            btn_back.Name = "btn_back";
            btn_back.Size = new Size(82, 26);
            btn_back.TabIndex = 5;
            btn_back.Text = "重置";
            btn_back.Type = AntdUI.TTypeMini.Success;
            // 
            // refeash
            // 
            refeash.Location = new Point(729, 28);
            refeash.Name = "refeash";
            refeash.Size = new Size(82, 26);
            refeash.TabIndex = 6;
            refeash.Text = "刷新";
            refeash.Type = AntdUI.TTypeMini.Primary;
            // 
            // PlayInfo
            // 
            PlayInfo.Location = new Point(729, 56);
            PlayInfo.Name = "PlayInfo";
            PlayInfo.Size = new Size(82, 26);
            PlayInfo.TabIndex = 7;
            PlayInfo.Text = "详细信息";
            PlayInfo.Type = AntdUI.TTypeMini.Warn;
            // 
            // GameMain
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(815, 484);
            Controls.Add(HeadContent);
            Controls.Add(tabs1);
            HelpButton = true;
            Name = "GameMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Form1";
            Load += GameMain_Load;
            tabs1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            gridPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.Tabs tabs1;
        private AntdUI.TabPage tabPage1;
        private AntdUI.TabPage tabPage2;
        private AntdUI.TabPage tabPage3;
        private AntdUI.TabPage tabPage4;
        private AntdUI.TabPage tabPage5;
        private AntdUI.PageHeader HeadContent;
        private AntdUI.GridPanel gridPanel1;
        private AntdUI.Label play_name;
        private AntdUI.Avatar play_HeadIcon;
        private AntdUI.Divider divider1;
        private AntdUI.Label play_number;
        private AntdUI.Label play_QF;
        private AntdUI.Button btn_back;
        private AntdUI.Button PlayInfo;
        private AntdUI.Button refeash;
    }
}
