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
            AntdUI.Tabs.StyleLine styleLine2 = new AntdUI.Tabs.StyleLine();
            tabs1 = new AntdUI.Tabs();
            tabPage1 = new AntdUI.TabPage();
            gridPanel1 = new AntdUI.GridPanel();
            splitter1 = new AntdUI.Splitter();
            game_lhp_loss = new AntdUI.Label();
            game_lhp_win = new AntdUI.Label();
            game_lhp_sl = new AntdUI.Label();
            game_dsp_loss = new AntdUI.Label();
            game_dsp_win = new AntdUI.Label();
            game_dsp_sl = new AntdUI.Label();
            game_lhpT = new AntdUI.Label();
            game_dspT = new AntdUI.Label();
            pic_lhp = new PictureBox();
            pic_dsp = new PictureBox();
            label1 = new AntdUI.Label();
            game_sjend = new AntdUI.Label();
            game_dqsd = new AntdUI.Label();
            game_jjscount = new AntdUI.Label();
            game_jjs = new AntdUI.Label();
            game_dws = new AntdUI.Label();
            divider4 = new AntdUI.Divider();
            divider3 = new AntdUI.Divider();
            divider2 = new AntdUI.Divider();
            stackPanel1 = new AntdUI.StackPanel();
            play_next = new AntdUI.Label();
            play_dj = new AntdUI.Label();
            play_jd = new AntdUI.Progress();
            PlayInfo = new AntdUI.Button();
            refeash = new AntdUI.Button();
            btn_back = new AntdUI.Button();
            play_QF = new AntdUI.Label();
            play_number = new AntdUI.Label();
            play_name = new AntdUI.Label();
            play_HeadIcon = new AntdUI.Avatar();
            divider1 = new AntdUI.Divider();
            tabPage2 = new AntdUI.TabPage();
            tabPage3 = new AntdUI.TabPage();
            tabPage4 = new AntdUI.TabPage();
            tabPage5 = new AntdUI.TabPage();
            HeadContent = new AntdUI.PageHeader();
            tabs1.SuspendLayout();
            tabPage1.SuspendLayout();
            gridPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
            splitter1.Panel1.SuspendLayout();
            splitter1.Panel2.SuspendLayout();
            splitter1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pic_lhp).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pic_dsp).BeginInit();
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
            tabs1.Size = new Size(815, 478);
            tabs1.Style = styleLine2;
            tabs1.TabIndex = 2;
            tabs1.Text = "tabs1";
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(gridPanel1);
            tabPage1.Dock = DockStyle.Fill;
            tabPage1.Location = new Point(0, 30);
            tabPage1.Name = "tabPage1";
            tabPage1.Size = new Size(815, 448);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "首页";
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(splitter1);
            gridPanel1.Controls.Add(play_next);
            gridPanel1.Controls.Add(play_dj);
            gridPanel1.Controls.Add(play_jd);
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
            gridPanel1.Size = new Size(815, 448);
            gridPanel1.Span = "";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // splitter1
            // 
            splitter1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            splitter1.Location = new Point(5, 113);
            splitter1.Name = "splitter1";
            // 
            // splitter1.Panel1
            // 
            splitter1.Panel1.Controls.Add(game_lhp_loss);
            splitter1.Panel1.Controls.Add(game_lhp_win);
            splitter1.Panel1.Controls.Add(game_lhp_sl);
            splitter1.Panel1.Controls.Add(game_dsp_loss);
            splitter1.Panel1.Controls.Add(game_dsp_win);
            splitter1.Panel1.Controls.Add(game_dsp_sl);
            splitter1.Panel1.Controls.Add(game_lhpT);
            splitter1.Panel1.Controls.Add(game_dspT);
            splitter1.Panel1.Controls.Add(pic_lhp);
            splitter1.Panel1.Controls.Add(pic_dsp);
            splitter1.Panel1.Controls.Add(label1);
            splitter1.Panel1.Controls.Add(game_sjend);
            splitter1.Panel1.Controls.Add(game_dqsd);
            splitter1.Panel1.Controls.Add(game_jjscount);
            splitter1.Panel1.Controls.Add(game_jjs);
            splitter1.Panel1.Controls.Add(game_dws);
            splitter1.Panel1.Controls.Add(divider4);
            splitter1.Panel1.Controls.Add(divider3);
            splitter1.Panel1.Controls.Add(divider2);
            // 
            // splitter1.Panel2
            // 
            splitter1.Panel2.Controls.Add(stackPanel1);
            splitter1.Size = new Size(805, 332);
            splitter1.SplitterDistance = 267;
            splitter1.TabIndex = 11;
            // 
            // game_lhp_loss
            // 
            game_lhp_loss.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_loss.Location = new Point(159, 201);
            game_lhp_loss.Name = "game_lhp_loss";
            game_lhp_loss.Prefix = "loss：";
            game_lhp_loss.Size = new Size(101, 23);
            game_lhp_loss.TabIndex = 18;
            game_lhp_loss.Text = "未知";
            // 
            // game_lhp_win
            // 
            game_lhp_win.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_win.Location = new Point(159, 172);
            game_lhp_win.Name = "game_lhp_win";
            game_lhp_win.Prefix = "win：";
            game_lhp_win.Size = new Size(101, 23);
            game_lhp_win.TabIndex = 17;
            game_lhp_win.Text = "未知";
            // 
            // game_lhp_sl
            // 
            game_lhp_sl.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_sl.Location = new Point(159, 146);
            game_lhp_sl.Name = "game_lhp_sl";
            game_lhp_sl.Prefix = "胜率：";
            game_lhp_sl.Size = new Size(101, 23);
            game_lhp_sl.TabIndex = 16;
            game_lhp_sl.Text = "未知";
            // 
            // game_dsp_loss
            // 
            game_dsp_loss.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_loss.Location = new Point(159, 77);
            game_dsp_loss.Name = "game_dsp_loss";
            game_dsp_loss.Prefix = "loss：";
            game_dsp_loss.Size = new Size(101, 23);
            game_dsp_loss.TabIndex = 15;
            game_dsp_loss.Text = "未知";
            // 
            // game_dsp_win
            // 
            game_dsp_win.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_win.Location = new Point(159, 48);
            game_dsp_win.Name = "game_dsp_win";
            game_dsp_win.Prefix = "win：";
            game_dsp_win.Size = new Size(101, 23);
            game_dsp_win.TabIndex = 14;
            game_dsp_win.Text = "未知";
            // 
            // game_dsp_sl
            // 
            game_dsp_sl.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_sl.Location = new Point(159, 22);
            game_dsp_sl.Name = "game_dsp_sl";
            game_dsp_sl.Prefix = "胜率：";
            game_dsp_sl.Size = new Size(101, 23);
            game_dsp_sl.TabIndex = 13;
            game_dsp_sl.Text = "未知";
            // 
            // game_lhpT
            // 
            game_lhpT.ColorExtend = "135,#FF0000,#00FF00";
            game_lhpT.Highlight = false;
            game_lhpT.Location = new Point(108, 146);
            game_lhpT.Name = "game_lhpT";
            game_lhpT.Size = new Size(33, 67);
            game_lhpT.TabIndex = 12;
            game_lhpT.Text = "无\r\n段\r\n位\r\n";
            game_lhpT.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // game_dspT
            // 
            game_dspT.ColorExtend = "135,#FF0000,#00FF00";
            game_dspT.Highlight = false;
            game_dspT.Location = new Point(105, 26);
            game_dspT.Name = "game_dspT";
            game_dspT.Size = new Size(33, 67);
            game_dspT.TabIndex = 11;
            game_dspT.Text = "无\r\n段\r\n位\r\n";
            game_dspT.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // pic_lhp
            // 
            pic_lhp.Image = Properties.Resources._09;
            pic_lhp.Location = new Point(10, 138);
            pic_lhp.Name = "pic_lhp";
            pic_lhp.Size = new Size(89, 86);
            pic_lhp.SizeMode = PictureBoxSizeMode.StretchImage;
            pic_lhp.TabIndex = 10;
            pic_lhp.TabStop = false;
            // 
            // pic_dsp
            // 
            pic_dsp.Image = Properties.Resources._01;
            pic_dsp.Location = new Point(8, 26);
            pic_dsp.Name = "pic_dsp";
            pic_dsp.Size = new Size(91, 84);
            pic_dsp.SizeMode = PictureBoxSizeMode.Zoom;
            pic_dsp.TabIndex = 9;
            pic_dsp.TabStop = false;
            // 
            // label1
            // 
            label1.ColorExtend = "135,#FF0000,#00FF00";
            label1.Location = new Point(137, 304);
            label1.Name = "label1";
            label1.Prefix = "隐藏分评分：";
            label1.Size = new Size(123, 25);
            label1.TabIndex = 8;
            label1.Text = "未知";
            // 
            // game_sjend
            // 
            game_sjend.ColorExtend = "135,#FF0000,#00FF00";
            game_sjend.Location = new Point(4, 304);
            game_sjend.Name = "game_sjend";
            game_sjend.Prefix = "赛季结束日期：";
            game_sjend.Size = new Size(123, 25);
            game_sjend.TabIndex = 7;
            game_sjend.Text = "未知";
            // 
            // game_dqsd
            // 
            game_dqsd.ColorExtend = "135,#FF0000,#00FF00";
            game_dqsd.Location = new Point(137, 280);
            game_dqsd.Name = "game_dqsd";
            game_dqsd.Prefix = "当前胜点：";
            game_dqsd.Size = new Size(123, 25);
            game_dqsd.TabIndex = 6;
            game_dqsd.Text = "未知";
            // 
            // game_jjscount
            // 
            game_jjscount.ColorExtend = "135,#FF0000,#00FF00";
            game_jjscount.Location = new Point(4, 280);
            game_jjscount.Name = "game_jjscount";
            game_jjscount.Prefix = "晋级需赢场次：";
            game_jjscount.Size = new Size(127, 25);
            game_jjscount.TabIndex = 5;
            game_jjscount.Text = "未知";
            // 
            // game_jjs
            // 
            game_jjs.ColorExtend = "135,#FF0000,#00FF00";
            game_jjs.Location = new Point(138, 249);
            game_jjs.Name = "game_jjs";
            game_jjs.Prefix = "晋级赛：";
            game_jjs.Size = new Size(123, 25);
            game_jjs.TabIndex = 4;
            game_jjs.Text = "未知";
            // 
            // game_dws
            // 
            game_dws.ColorExtend = "135,#FF0000,#00FF00";
            game_dws.Location = new Point(4, 249);
            game_dws.Name = "game_dws";
            game_dws.Prefix = "定位赛：";
            game_dws.Size = new Size(127, 25);
            game_dws.TabIndex = 3;
            game_dws.Text = "未知";
            // 
            // divider4
            // 
            divider4.Location = new Point(6, 220);
            divider4.Name = "divider4";
            divider4.Orientation = AntdUI.TOrientation.Left;
            divider4.Size = new Size(257, 23);
            divider4.TabIndex = 2;
            divider4.Text = "赛季数据";
            // 
            // divider3
            // 
            divider3.Location = new Point(7, 116);
            divider3.Name = "divider3";
            divider3.Orientation = AntdUI.TOrientation.Left;
            divider3.Size = new Size(257, 23);
            divider3.TabIndex = 1;
            divider3.Text = "灵活排位";
            // 
            // divider2
            // 
            divider2.Location = new Point(7, 3);
            divider2.Name = "divider2";
            divider2.Orientation = AntdUI.TOrientation.Left;
            divider2.Size = new Size(257, 23);
            divider2.TabIndex = 0;
            divider2.Text = "单双排";
            // 
            // stackPanel1
            // 
            stackPanel1.Dock = DockStyle.Fill;
            stackPanel1.Location = new Point(0, 0);
            stackPanel1.Name = "stackPanel1";
            stackPanel1.Size = new Size(534, 332);
            stackPanel1.TabIndex = 11;
            stackPanel1.Text = "stackPanel1";
            // 
            // play_next
            // 
            play_next.ColorExtend = "135,#FF0000,#00FF00";
            play_next.Location = new Point(342, 35);
            play_next.Name = "play_next";
            play_next.Prefix = "当前经验：";
            play_next.Size = new Size(180, 26);
            play_next.TabIndex = 10;
            play_next.Text = "";
            // 
            // play_dj
            // 
            play_dj.ColorExtend = "135,#FF0000,#00FF00";
            play_dj.Location = new Point(342, 3);
            play_dj.Name = "play_dj";
            play_dj.Prefix = "当前等级：";
            play_dj.Size = new Size(180, 26);
            play_dj.TabIndex = 9;
            play_dj.Text = "";
            // 
            // play_jd
            // 
            play_jd.Location = new Point(342, 60);
            play_jd.Name = "play_jd";
            play_jd.ShowTextDot = 2;
            play_jd.Size = new Size(353, 23);
            play_jd.TabIndex = 8;
            play_jd.Text = "";
            play_jd.TextUnit = "%(升级进度)";
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
            // refeash
            // 
            refeash.Location = new Point(729, 28);
            refeash.Name = "refeash";
            refeash.Size = new Size(82, 26);
            refeash.TabIndex = 6;
            refeash.Text = "刷新";
            refeash.Type = AntdUI.TTypeMini.Primary;
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
            // play_QF
            // 
            play_QF.ColorExtend = "135,#FF0000,#00FF00";
            play_QF.Location = new Point(222, 43);
            play_QF.Name = "play_QF";
            play_QF.Prefix = "区服：";
            play_QF.PrefixSvg = "RocketOutlined";
            play_QF.Size = new Size(96, 26);
            play_QF.SuffixSvg = "";
            play_QF.TabIndex = 4;
            play_QF.Text = "区服";
            // 
            // play_number
            // 
            play_number.ColorExtend = "135,#FF0000,#00FF00";
            play_number.Location = new Point(93, 43);
            play_number.Name = "play_number";
            play_number.Prefix = "编号：";
            play_number.Size = new Size(123, 26);
            play_number.TabIndex = 3;
            play_number.Text = "编号";
            // 
            // play_name
            // 
            play_name.ColorExtend = "135,#FF0000,#00FF00";
            play_name.Location = new Point(93, 10);
            play_name.Name = "play_name";
            play_name.Prefix = "名称：";
            play_name.Size = new Size(225, 26);
            play_name.TabIndex = 2;
            play_name.Text = "名称";
            // 
            // play_HeadIcon
            // 
            play_HeadIcon.Location = new Point(6, 10);
            play_HeadIcon.Name = "play_HeadIcon";
            play_HeadIcon.Size = new Size(81, 85);
            play_HeadIcon.TabIndex = 1;
            play_HeadIcon.Text = "无";
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
            // tabPage2
            // 
            tabPage2.Dock = DockStyle.Fill;
            tabPage2.Location = new Point(0, 30);
            tabPage2.Name = "tabPage2";
            tabPage2.Size = new Size(815, 448);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "对局";
            // 
            // tabPage3
            // 
            tabPage3.Dock = DockStyle.Fill;
            tabPage3.Location = new Point(0, 30);
            tabPage3.Name = "tabPage3";
            tabPage3.Size = new Size(815, 448);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "战绩查询";
            // 
            // tabPage4
            // 
            tabPage4.Dock = DockStyle.Fill;
            tabPage4.Location = new Point(0, 30);
            tabPage4.Name = "tabPage4";
            tabPage4.Size = new Size(815, 448);
            tabPage4.TabIndex = 3;
            tabPage4.Text = "关于";
            // 
            // tabPage5
            // 
            tabPage5.Dock = DockStyle.Fill;
            tabPage5.Location = new Point(0, 30);
            tabPage5.Name = "tabPage5";
            tabPage5.Size = new Size(815, 448);
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
            // GameMain
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(815, 513);
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
            splitter1.Panel1.ResumeLayout(false);
            splitter1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
            splitter1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pic_lhp).EndInit();
            ((System.ComponentModel.ISupportInitialize)pic_dsp).EndInit();
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
        private AntdUI.Label play_next;
        private AntdUI.Label play_dj;
        private AntdUI.Progress play_jd;
        private AntdUI.StackPanel stackPanel1;
        private AntdUI.Splitter splitter1;
        private AntdUI.Divider divider2;
        private AntdUI.Divider divider4;
        private AntdUI.Divider divider3;
        private AntdUI.Label game_jjs;
        private AntdUI.Label game_dws;
        private AntdUI.Label game_jjscount;
        private AntdUI.Label game_dqsd;
        private AntdUI.Label game_sjend;
        private PictureBox pic_lhp;
        private PictureBox pic_dsp;
        private AntdUI.Label label1;
        private AntdUI.Label game_dspT;
        private AntdUI.Label game_lhpT;
        private AntdUI.Label game_dsp_sl;
        private AntdUI.Label game_dsp_loss;
        private AntdUI.Label game_dsp_win;
        private AntdUI.Label game_lhp_loss;
        private AntdUI.Label game_lhp_win;
        private AntdUI.Label game_lhp_sl;
    }
}
