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
            tabPage1 = new AntdUI.TabPage();
            gridPanel1 = new AntdUI.GridPanel();
            gridPanel7 = new AntdUI.GridPanel();
            gridPanel5 = new AntdUI.GridPanel();
            gridPanel6 = new AntdUI.GridPanel();
            play_name = new AntdUI.Label();
            play_QF = new AntdUI.Label();
            play_number = new AntdUI.Label();
            play_HeadIcon = new AntdUI.Avatar();
            gridPanel4 = new AntdUI.GridPanel();
            splitter1 = new AntdUI.Splitter();
            game_sjend = new AntdUI.Label();
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
            game_ycf = new AntdUI.Label();
            game_dqsd = new AntdUI.Label();
            game_jjscount = new AntdUI.Label();
            game_jjs = new AntdUI.Label();
            game_dws = new AntdUI.Label();
            divider4 = new AntdUI.Divider();
            divider3 = new AntdUI.Divider();
            divider2 = new AntdUI.Divider();
            game_pagin = new AntdUI.Pagination();
            stackPanel1 = new AntdUI.StackPanel();
            gridPanel3 = new AntdUI.GridPanel();
            inp_playname = new AntdUI.Input();
            game_count = new AntdUI.InputNumber();
            play_next = new AntdUI.Label();
            play_dj = new AntdUI.Label();
            play_jd = new AntdUI.Progress();
            btn_back = new AntdUI.Button();
            Search_Player = new AntdUI.Button();
            refeash = new AntdUI.Button();
            divider1 = new AntdUI.Divider();
            tabPage1.SuspendLayout();
            gridPanel1.SuspendLayout();
            gridPanel7.SuspendLayout();
            gridPanel5.SuspendLayout();
            gridPanel6.SuspendLayout();
            gridPanel4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
            splitter1.Panel1.SuspendLayout();
            splitter1.Panel2.SuspendLayout();
            splitter1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pic_lhp).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pic_dsp).BeginInit();
            gridPanel3.SuspendLayout();
            SuspendLayout();
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(gridPanel1);
            tabPage1.Dock = DockStyle.Fill;
            tabPage1.Location = new Point(0, 0);
            tabPage1.Name = "tabPage1";
            tabPage1.Size = new Size(1231, 840);
            tabPage1.TabIndex = 1;
            tabPage1.Text = "首页";
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(gridPanel7);
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(1231, 840);
            gridPanel1.Span = "100%";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // gridPanel7
            // 
            gridPanel7.Controls.Add(gridPanel5);
            gridPanel7.Controls.Add(gridPanel4);
            gridPanel7.Controls.Add(gridPanel3);
            gridPanel7.Controls.Add(divider1);
            gridPanel7.Dock = DockStyle.Fill;
            gridPanel7.Location = new Point(3, 3);
            gridPanel7.Name = "gridPanel7";
            gridPanel7.Size = new Size(1225, 834);
            gridPanel7.Span = "50% 50%;100%;100%;-180 5% 95%";
            gridPanel7.TabIndex = 16;
            gridPanel7.Text = "gridPanel7";
            // 
            // gridPanel5
            // 
            gridPanel5.Controls.Add(gridPanel6);
            gridPanel5.Controls.Add(play_HeadIcon);
            gridPanel7.SetIndex(gridPanel5, 1);
            gridPanel5.Location = new Point(3, 3);
            gridPanel5.Name = "gridPanel5";
            gridPanel5.Size = new Size(606, 174);
            gridPanel5.Span = "30% 70%";
            gridPanel5.TabIndex = 15;
            gridPanel5.Text = "gridPanel5";
            // 
            // gridPanel6
            // 
            gridPanel6.Controls.Add(play_name);
            gridPanel6.Controls.Add(play_QF);
            gridPanel6.Controls.Add(play_number);
            gridPanel6.Location = new Point(185, 3);
            gridPanel6.Name = "gridPanel6";
            gridPanel6.Size = new Size(418, 168);
            gridPanel6.Span = "90%;30% 30%";
            gridPanel6.TabIndex = 16;
            gridPanel6.Text = "gridPanel6";
            // 
            // play_name
            // 
            play_name.ColorExtend = "135,#FF0000,#00FF00";
            play_name.Font = new Font("Microsoft YaHei UI", 12F);
            gridPanel6.SetIndex(play_name, 1);
            play_name.Location = new Point(3, 3);
            play_name.Name = "play_name";
            play_name.Prefix = "名称：";
            play_name.Size = new Size(370, 78);
            play_name.TabIndex = 2;
            play_name.Text = "名称";
            // 
            // play_QF
            // 
            play_QF.ColorExtend = "135,#FF0000,#00FF00";
            play_QF.Font = new Font("Microsoft YaHei UI", 12F);
            gridPanel6.SetIndex(play_QF, 3);
            play_QF.Location = new Point(128, 87);
            play_QF.Name = "play_QF";
            play_QF.Prefix = "区服：";
            play_QF.PrefixSvg = "RocketOutlined";
            play_QF.Size = new Size(119, 78);
            play_QF.SuffixSvg = "";
            play_QF.TabIndex = 4;
            play_QF.Text = "区服";
            // 
            // play_number
            // 
            play_number.ColorExtend = "135,#FF0000,#00FF00";
            play_number.Font = new Font("Microsoft YaHei UI", 12F);
            gridPanel6.SetIndex(play_number, 2);
            play_number.Location = new Point(3, 87);
            play_number.Name = "play_number";
            play_number.Prefix = "编号：";
            play_number.Size = new Size(119, 78);
            play_number.TabIndex = 3;
            play_number.Text = "编号";
            // 
            // play_HeadIcon
            // 
            gridPanel5.SetIndex(play_HeadIcon, 1);
            play_HeadIcon.Location = new Point(3, 3);
            play_HeadIcon.Name = "play_HeadIcon";
            play_HeadIcon.Size = new Size(176, 168);
            play_HeadIcon.TabIndex = 1;
            play_HeadIcon.Text = "无";
            // 
            // gridPanel4
            // 
            gridPanel4.Controls.Add(splitter1);
            gridPanel7.SetIndex(gridPanel4, 4);
            gridPanel4.Location = new Point(3, 216);
            gridPanel4.Name = "gridPanel4";
            gridPanel4.Size = new Size(1219, 615);
            gridPanel4.Span = "100%";
            gridPanel4.TabIndex = 14;
            gridPanel4.Text = "gridPanel4";
            // 
            // splitter1
            // 
            splitter1.Location = new Point(3, 3);
            splitter1.Name = "splitter1";
            // 
            // splitter1.Panel1
            // 
            splitter1.Panel1.Controls.Add(game_sjend);
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
            splitter1.Panel1.Controls.Add(game_ycf);
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
            splitter1.Panel2.Controls.Add(game_pagin);
            splitter1.Panel2.Controls.Add(stackPanel1);
            splitter1.Size = new Size(1213, 609);
            splitter1.SplitterDistance = 400;
            splitter1.TabIndex = 11;
            // 
            // game_sjend
            // 
            game_sjend.ColorExtend = "135,#FF0000,#00FF00";
            game_sjend.Font = new Font("Microsoft YaHei UI", 12F);
            game_sjend.Location = new Point(7, 476);
            game_sjend.Name = "game_sjend";
            game_sjend.Prefix = "赛季结束日期：";
            game_sjend.Size = new Size(296, 41);
            game_sjend.TabIndex = 7;
            game_sjend.Text = "未知";
            // 
            // game_lhp_loss
            // 
            game_lhp_loss.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_loss.Font = new Font("Microsoft YaHei UI", 12F);
            game_lhp_loss.Location = new Point(234, 298);
            game_lhp_loss.Name = "game_lhp_loss";
            game_lhp_loss.Prefix = "loss：";
            game_lhp_loss.Size = new Size(130, 39);
            game_lhp_loss.TabIndex = 18;
            game_lhp_loss.Text = "未知";
            // 
            // game_lhp_win
            // 
            game_lhp_win.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_win.Font = new Font("Microsoft YaHei UI", 12F);
            game_lhp_win.Location = new Point(234, 250);
            game_lhp_win.Name = "game_lhp_win";
            game_lhp_win.Prefix = "win：";
            game_lhp_win.Size = new Size(130, 42);
            game_lhp_win.TabIndex = 17;
            game_lhp_win.Text = "未知";
            // 
            // game_lhp_sl
            // 
            game_lhp_sl.ColorExtend = "135,#FF0000,#00FF00";
            game_lhp_sl.Font = new Font("Microsoft YaHei UI", 12F);
            game_lhp_sl.Location = new Point(234, 197);
            game_lhp_sl.Name = "game_lhp_sl";
            game_lhp_sl.Prefix = "胜率：";
            game_lhp_sl.Size = new Size(130, 47);
            game_lhp_sl.Suffix = "%";
            game_lhp_sl.TabIndex = 16;
            game_lhp_sl.Text = "未知";
            // 
            // game_dsp_loss
            // 
            game_dsp_loss.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_loss.Font = new Font("Microsoft YaHei UI", 12F);
            game_dsp_loss.Location = new Point(234, 123);
            game_dsp_loss.Name = "game_dsp_loss";
            game_dsp_loss.Prefix = "loss：";
            game_dsp_loss.Size = new Size(130, 39);
            game_dsp_loss.TabIndex = 15;
            game_dsp_loss.Text = "未知";
            // 
            // game_dsp_win
            // 
            game_dsp_win.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_win.Font = new Font("Microsoft YaHei UI", 12F);
            game_dsp_win.Location = new Point(234, 74);
            game_dsp_win.Name = "game_dsp_win";
            game_dsp_win.Prefix = "win：";
            game_dsp_win.Size = new Size(130, 43);
            game_dsp_win.TabIndex = 14;
            game_dsp_win.Text = "未知";
            // 
            // game_dsp_sl
            // 
            game_dsp_sl.ColorExtend = "135,#FF0000,#00FF00";
            game_dsp_sl.Font = new Font("Microsoft YaHei UI", 12F);
            game_dsp_sl.Location = new Point(234, 26);
            game_dsp_sl.Name = "game_dsp_sl";
            game_dsp_sl.Prefix = "胜率：";
            game_dsp_sl.Size = new Size(130, 42);
            game_dsp_sl.Suffix = "%";
            game_dsp_sl.TabIndex = 13;
            game_dsp_sl.Text = "未知";
            // 
            // game_lhpT
            // 
            game_lhpT.ColorExtend = "135,#FF0000,#00FF00";
            game_lhpT.Highlight = false;
            game_lhpT.Location = new Point(144, 197);
            game_lhpT.Name = "game_lhpT";
            game_lhpT.Size = new Size(44, 127);
            game_lhpT.TabIndex = 12;
            game_lhpT.Text = "无\r\n段\r\n位\r\n";
            game_lhpT.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // game_dspT
            // 
            game_dspT.ColorExtend = "135,#FF0000,#00FF00";
            game_dspT.Highlight = false;
            game_dspT.Location = new Point(144, 26);
            game_dspT.Name = "game_dspT";
            game_dspT.Size = new Size(44, 120);
            game_dspT.TabIndex = 11;
            game_dspT.Text = "无\r\n段\r\n位\r\n";
            game_dspT.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // pic_lhp
            // 
            pic_lhp.Image = Properties.Resources.unranked;
            pic_lhp.Location = new Point(8, 197);
            pic_lhp.Name = "pic_lhp";
            pic_lhp.Size = new Size(130, 127);
            pic_lhp.SizeMode = PictureBoxSizeMode.StretchImage;
            pic_lhp.TabIndex = 10;
            pic_lhp.TabStop = false;
            // 
            // pic_dsp
            // 
            pic_dsp.Image = Properties.Resources.unranked;
            pic_dsp.Location = new Point(7, 26);
            pic_dsp.Name = "pic_dsp";
            pic_dsp.Size = new Size(131, 136);
            pic_dsp.SizeMode = PictureBoxSizeMode.StretchImage;
            pic_dsp.TabIndex = 9;
            pic_dsp.TabStop = false;
            // 
            // game_ycf
            // 
            game_ycf.ColorExtend = "135,#FF0000,#00FF00";
            game_ycf.Font = new Font("Microsoft YaHei UI", 12F);
            game_ycf.Location = new Point(210, 476);
            game_ycf.Name = "game_ycf";
            game_ycf.Prefix = "隐藏分评分：";
            game_ycf.Size = new Size(135, 41);
            game_ycf.TabIndex = 8;
            game_ycf.Text = "未知";
            // 
            // game_dqsd
            // 
            game_dqsd.ColorExtend = "135,#FF0000,#00FF00";
            game_dqsd.Font = new Font("Microsoft YaHei UI", 12F);
            game_dqsd.Location = new Point(210, 421);
            game_dqsd.Name = "game_dqsd";
            game_dqsd.Prefix = "当前胜点：";
            game_dqsd.Size = new Size(154, 49);
            game_dqsd.TabIndex = 6;
            game_dqsd.Text = "未知";
            // 
            // game_jjscount
            // 
            game_jjscount.ColorExtend = "135,#FF0000,#00FF00";
            game_jjscount.Font = new Font("Microsoft YaHei UI", 12F);
            game_jjscount.Location = new Point(7, 419);
            game_jjscount.Name = "game_jjscount";
            game_jjscount.Prefix = "晋级需赢场次：";
            game_jjscount.Size = new Size(197, 53);
            game_jjscount.TabIndex = 5;
            game_jjscount.Text = "未知";
            // 
            // game_jjs
            // 
            game_jjs.ColorExtend = "135,#FF0000,#00FF00";
            game_jjs.Font = new Font("Microsoft YaHei UI", 12F);
            game_jjs.Location = new Point(210, 362);
            game_jjs.Name = "game_jjs";
            game_jjs.Prefix = "晋级赛：";
            game_jjs.Size = new Size(154, 53);
            game_jjs.TabIndex = 4;
            game_jjs.Text = "未知";
            // 
            // game_dws
            // 
            game_dws.ColorExtend = "135,#FF0000,#00FF00";
            game_dws.Font = new Font("Microsoft YaHei UI", 12F);
            game_dws.Location = new Point(8, 362);
            game_dws.Name = "game_dws";
            game_dws.Prefix = "定位赛：";
            game_dws.Size = new Size(196, 51);
            game_dws.TabIndex = 3;
            game_dws.Text = "未知";
            // 
            // divider4
            // 
            divider4.ColorSplit = Color.FromArgb(255, 235, 59);
            divider4.Location = new Point(10, 333);
            divider4.Name = "divider4";
            divider4.Orientation = AntdUI.TOrientation.Left;
            divider4.Size = new Size(354, 23);
            divider4.TabIndex = 2;
            divider4.Text = "赛季数据";
            // 
            // divider3
            // 
            divider3.Location = new Point(7, 168);
            divider3.Name = "divider3";
            divider3.Orientation = AntdUI.TOrientation.Left;
            divider3.Size = new Size(357, 23);
            divider3.TabIndex = 1;
            divider3.Text = "灵活排位";
            // 
            // divider2
            // 
            divider2.ColorSplit = Color.FromArgb(255, 235, 59);
            divider2.Location = new Point(7, 3);
            divider2.Name = "divider2";
            divider2.Orientation = AntdUI.TOrientation.Left;
            divider2.Size = new Size(390, 23);
            divider2.TabIndex = 0;
            divider2.Text = "单双排";
            // 
            // game_pagin
            // 
            game_pagin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            game_pagin.Location = new Point(3, 5);
            game_pagin.Name = "game_pagin";
            game_pagin.Size = new Size(803, 28);
            game_pagin.TabIndex = 12;
            game_pagin.ValueChanged += game_pagin_ValueChanged;
            // 
            // stackPanel1
            // 
            stackPanel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            stackPanel1.AutoScroll = true;
            stackPanel1.Location = new Point(3, 39);
            stackPanel1.Name = "stackPanel1";
            stackPanel1.Size = new Size(803, 567);
            stackPanel1.TabIndex = 11;
            stackPanel1.Text = "stackPanel1";
            stackPanel1.Vertical = true;
            // 
            // gridPanel3
            // 
            gridPanel3.Controls.Add(inp_playname);
            gridPanel3.Controls.Add(game_count);
            gridPanel3.Controls.Add(play_next);
            gridPanel3.Controls.Add(play_dj);
            gridPanel3.Controls.Add(play_jd);
            gridPanel3.Controls.Add(btn_back);
            gridPanel3.Controls.Add(Search_Player);
            gridPanel3.Controls.Add(refeash);
            gridPanel3.Location = new Point(615, 3);
            gridPanel3.Name = "gridPanel3";
            gridPanel3.Size = new Size(606, 174);
            gridPanel3.Span = "20% 60% 20%;60% 20% 20%;80% 20%";
            gridPanel3.TabIndex = 13;
            gridPanel3.Text = "gridPanel3";
            // 
            // inp_playname
            // 
            gridPanel3.SetIndex(inp_playname, 2);
            inp_playname.Location = new Point(124, 3);
            inp_playname.Name = "inp_playname";
            inp_playname.PlaceholderText = "玩家名或者编号";
            inp_playname.Size = new Size(358, 52);
            inp_playname.TabIndex = 13;
            inp_playname.Text = "e684b22a-5f9a-5ca1-b7d5-5e1a8e4b705b";
            // 
            // game_count
            // 
            gridPanel3.SetIndex(game_count, 4);
            game_count.Location = new Point(367, 61);
            game_count.Name = "game_count";
            game_count.PlaceholderText = "查询条数";
            game_count.Size = new Size(115, 52);
            game_count.TabIndex = 12;
            game_count.Text = "5";
            game_count.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // play_next
            // 
            play_next.ColorExtend = "135,#FF0000,#00FF00";
            gridPanel3.SetIndex(play_next, 3);
            play_next.Location = new Point(3, 61);
            play_next.Name = "play_next";
            play_next.Prefix = "当前经验：";
            play_next.Size = new Size(358, 52);
            play_next.TabIndex = 10;
            play_next.Text = "";
            // 
            // play_dj
            // 
            play_dj.ColorExtend = "135,#FF0000,#00FF00";
            gridPanel3.SetIndex(play_dj, 1);
            play_dj.Location = new Point(3, 3);
            play_dj.Name = "play_dj";
            play_dj.Prefix = "当前等级：";
            play_dj.Size = new Size(115, 52);
            play_dj.TabIndex = 9;
            play_dj.Text = "";
            // 
            // play_jd
            // 
            gridPanel3.SetIndex(play_jd, 6);
            play_jd.Location = new Point(3, 119);
            play_jd.Name = "play_jd";
            play_jd.ShowTextDot = 2;
            play_jd.Size = new Size(479, 52);
            play_jd.TabIndex = 8;
            play_jd.Text = "";
            play_jd.TextUnit = "%(升级进度)";
            // 
            // btn_back
            // 
            gridPanel3.SetIndex(btn_back, 8);
            btn_back.Location = new Point(488, 119);
            btn_back.Name = "btn_back";
            btn_back.Size = new Size(115, 52);
            btn_back.TabIndex = 5;
            btn_back.Text = "重置";
            btn_back.Type = AntdUI.TTypeMini.Success;
            btn_back.Click += btn_back_Click;
            // 
            // Search_Player
            // 
            gridPanel3.SetIndex(Search_Player, 3);
            Search_Player.Location = new Point(488, 3);
            Search_Player.Name = "Search_Player";
            Search_Player.Size = new Size(115, 52);
            Search_Player.TabIndex = 7;
            Search_Player.Text = "搜索";
            Search_Player.Type = AntdUI.TTypeMini.Warn;
            Search_Player.Click += PlayInfo_Click;
            // 
            // refeash
            // 
            gridPanel3.SetIndex(refeash, 5);
            refeash.Location = new Point(488, 61);
            refeash.Name = "refeash";
            refeash.Size = new Size(115, 52);
            refeash.TabIndex = 6;
            refeash.Text = "刷新";
            refeash.Type = AntdUI.TTypeMini.Primary;
            refeash.Click += refeash_Click;
            // 
            // divider1
            // 
            divider1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            divider1.BadgeSvg = "BarChartOutlined";
            divider1.ColorSplit = Color.FromArgb(255, 235, 59);
            gridPanel7.SetIndex(divider1, 4);
            divider1.Location = new Point(3, 183);
            divider1.Name = "divider1";
            divider1.Size = new Size(1219, 27);
            divider1.TabIndex = 0;
            divider1.Text = "战绩区";
            // 
            // HomeForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tabPage1);
            DoubleBuffered = true;
            Name = "HomeForm";
            Size = new Size(1231, 840);
            Load += HomeForm_Load;
            tabPage1.ResumeLayout(false);
            gridPanel1.ResumeLayout(false);
            gridPanel7.ResumeLayout(false);
            gridPanel5.ResumeLayout(false);
            gridPanel6.ResumeLayout(false);
            gridPanel4.ResumeLayout(false);
            splitter1.Panel1.ResumeLayout(false);
            splitter1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
            splitter1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pic_lhp).EndInit();
            ((System.ComponentModel.ISupportInitialize)pic_dsp).EndInit();
            gridPanel3.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.TabPage tabPage1;
        private AntdUI.GridPanel gridPanel1;
        private AntdUI.GridPanel gridPanel7;
        private AntdUI.GridPanel gridPanel5;
        private AntdUI.GridPanel gridPanel6;
        private AntdUI.Label play_name;
        private AntdUI.Label play_QF;
        private AntdUI.Label play_number;
        private AntdUI.Avatar play_HeadIcon;
        private AntdUI.GridPanel gridPanel4;
        private AntdUI.Splitter splitter1;
        private AntdUI.Label game_sjend;
        private AntdUI.Label game_lhp_loss;
        private AntdUI.Label game_lhp_win;
        private AntdUI.Label game_lhp_sl;
        private AntdUI.Label game_dsp_loss;
        private AntdUI.Label game_dsp_win;
        private AntdUI.Label game_dsp_sl;
        private AntdUI.Label game_lhpT;
        private AntdUI.Label game_dspT;
        private PictureBox pic_lhp;
        private PictureBox pic_dsp;
        private AntdUI.Label game_ycf;
        private AntdUI.Label game_dqsd;
        private AntdUI.Label game_jjscount;
        private AntdUI.Label game_jjs;
        private AntdUI.Label game_dws;
        private AntdUI.Divider divider4;
        private AntdUI.Divider divider3;
        private AntdUI.Divider divider2;
        private AntdUI.Pagination game_pagin;
        private AntdUI.StackPanel stackPanel1;
        private AntdUI.GridPanel gridPanel3;
        private AntdUI.InputNumber game_count;
        private AntdUI.Label play_next;
        private AntdUI.Label play_dj;
        private AntdUI.Progress play_jd;
        private AntdUI.Button btn_back;
        private AntdUI.Button Search_Player;
        private AntdUI.Button refeash;
        private AntdUI.Divider divider1;
        private AntdUI.Input inp_playname;
    }
}
