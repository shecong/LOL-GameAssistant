namespace LOL_GameAssistant.BaseViewForm
{
    partial class SettingForm
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
            gridPanel2 = new TableLayoutPanel();
            label1 = new AntdUI.Label();
            swi_open = new AntdUI.Switch();
            label2 = new AntdUI.Label();
            swi_gametrue = new AntdUI.Switch();
            label3 = new AntdUI.Label();
            swi_jyyx = new AntdUI.Switch();
            setting_select_jyx = new AntdUI.SelectMultiple();
            label4 = new AntdUI.Label();
            swi_xyx = new AntdUI.Switch();
            setting_select_xyx = new AntdUI.SelectMultiple();
            label5 = new AntdUI.Label();
            inputNumber1 = new AntdUI.InputNumber();
            label_resolution = new AntdUI.Label();
            select_resolution = new AntdUI.Select();
            label_tray = new AntdUI.Label();
            swi_tray = new AntdUI.Switch();
            label_auto_refresh = new AntdUI.Label();
            swi_auto_refresh = new AntdUI.Switch();
            flow_auto_refresh = new FlowLayoutPanel();
            label_refresh_interval = new AntdUI.Label();
            input_auto_refresh = new AntdUI.InputNumber();
            label_notify_end = new AntdUI.Label();
            swi_notify_end = new AntdUI.Switch();
            label_startup = new AntdUI.Label();
            swi_startup = new AntdUI.Switch();
            label_cache = new AntdUI.Label();
            label_cache_status = new AntdUI.Label();
            flow_ban_preview = new FlowLayoutPanel();
            flow_pick_preview = new FlowLayoutPanel();
            gridPanel2.SuspendLayout();
            flow_auto_refresh.SuspendLayout();
            SuspendLayout();
            // 
            // gridPanel2
            // 
            gridPanel2.ColumnCount = 3;
            gridPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            gridPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            gridPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            gridPanel2.Controls.Add(label1, 0, 0);
            gridPanel2.Controls.Add(swi_open, 1, 0);
            gridPanel2.Controls.Add(label2, 0, 1);
            gridPanel2.Controls.Add(swi_gametrue, 1, 1);
            gridPanel2.Controls.Add(label3, 0, 2);
            gridPanel2.Controls.Add(swi_jyyx, 1, 2);
            gridPanel2.Controls.Add(setting_select_jyx, 2, 2);
            gridPanel2.Controls.Add(label4, 0, 3);
            gridPanel2.Controls.Add(swi_xyx, 1, 3);
            gridPanel2.Controls.Add(setting_select_xyx, 2, 3);
            gridPanel2.Controls.Add(label5, 0, 4);
            gridPanel2.Controls.Add(inputNumber1, 1, 4);
            gridPanel2.Controls.Add(label_resolution, 0, 5);
            gridPanel2.Controls.Add(select_resolution, 1, 5);
            gridPanel2.Controls.Add(label_tray, 0, 6);
            gridPanel2.Controls.Add(swi_tray, 1, 6);
            gridPanel2.Controls.Add(label_auto_refresh, 0, 7);
            gridPanel2.Controls.Add(swi_auto_refresh, 1, 7);
            gridPanel2.Controls.Add(flow_auto_refresh, 2, 7);
            gridPanel2.Controls.Add(label_notify_end, 0, 8);
            gridPanel2.Controls.Add(swi_notify_end, 1, 8);
            gridPanel2.Controls.Add(label_startup, 0, 9);
            gridPanel2.Controls.Add(swi_startup, 1, 9);
            gridPanel2.Controls.Add(label_cache, 0, 10);
            gridPanel2.Controls.Add(label_cache_status, 1, 10);
            gridPanel2.SetColumnSpan(label_cache_status, 2);
            gridPanel2.Controls.Add(flow_ban_preview, 0, 11);
            gridPanel2.SetColumnSpan(flow_ban_preview, 3);
            gridPanel2.Controls.Add(flow_pick_preview, 0, 12);
            gridPanel2.SetColumnSpan(flow_pick_preview, 3);
            gridPanel2.Dock = DockStyle.Fill;
            gridPanel2.Location = new Point(0, 0);
            gridPanel2.Name = "gridPanel2";
            gridPanel2.Padding = new Padding(10);
            gridPanel2.RowCount = 13;
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            gridPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            gridPanel2.Size = new Size(828, 721);
            gridPanel2.TabIndex = 1;
            // 
            // label1
            // 
            label1.Dock = DockStyle.Fill;
            label1.Text = "自动匹配对局：";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_open
            // 
            swi_open.CheckedText = "开";
            swi_open.Dock = DockStyle.Left;
            swi_open.UnCheckedText = "关";
            swi_open.Width = 80;
            // 
            // label2
            // 
            label2.Dock = DockStyle.Fill;
            label2.Text = "自动接受对局：";
            label2.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_gametrue
            // 
            swi_gametrue.CheckedText = "开";
            swi_gametrue.Dock = DockStyle.Left;
            swi_gametrue.UnCheckedText = "关";
            swi_gametrue.Width = 80;
            // 
            // label3
            // 
            label3.Dock = DockStyle.Fill;
            label3.Text = "自动禁英雄：";
            label3.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_jyyx
            // 
            swi_jyyx.CheckedText = "开";
            swi_jyyx.Dock = DockStyle.Left;
            swi_jyyx.UnCheckedText = "关";
            swi_jyyx.Width = 80;
            // 
            // setting_select_jyx
            // 
            setting_select_jyx.AllowClear = true;
            setting_select_jyx.AutoHeight = true;
            setting_select_jyx.CheckMode = true;
            setting_select_jyx.Dock = DockStyle.Fill;
            setting_select_jyx.List = true;
            setting_select_jyx.MaxCount = 10;
            setting_select_jyx.Multiline = true;
            setting_select_jyx.Name = "setting_select_jyx";
            setting_select_jyx.PlaceholderText = "下拉选择英雄";
            // 
            // label4
            // 
            label4.Dock = DockStyle.Fill;
            label4.Text = "自动选英雄：";
            label4.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_xyx
            // 
            swi_xyx.CheckedText = "开";
            swi_xyx.Dock = DockStyle.Left;
            swi_xyx.UnCheckedText = "关";
            swi_xyx.Width = 80;
            // 
            // setting_select_xyx
            // 
            setting_select_xyx.AllowClear = true;
            setting_select_xyx.AutoHeight = true;
            setting_select_xyx.CheckMode = true;
            setting_select_xyx.Dock = DockStyle.Fill;
            setting_select_xyx.MaxCount = 10;
            setting_select_xyx.Multiline = true;
            setting_select_xyx.Name = "setting_select_xyx";
            setting_select_xyx.PlaceholderText = "下拉选择英雄";
            // 
            // label5
            // 
            label5.Dock = DockStyle.Fill;
            label5.Text = "自动禁用间隔(秒)：";
            label5.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // inputNumber1
            // 
            inputNumber1.Dock = DockStyle.Left;
            inputNumber1.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            inputNumber1.Value = new decimal(new int[] { 2, 0, 0, 0 });
            inputNumber1.Width = 120;
            // 
            // label_resolution
            // 
            label_resolution.Dock = DockStyle.Fill;
            label_resolution.Text = "分辨率：";
            label_resolution.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // select_resolution
            // 
            select_resolution.Dock = DockStyle.Left;
            select_resolution.DropDownArrow = true;
            select_resolution.Width = 160;
            select_resolution.SelectedIndexChanged += SelectResolutionChanged;
            // 
            // label_tray
            // 
            label_tray.Dock = DockStyle.Fill;
            label_tray.Text = "最小化到托盘：";
            label_tray.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_tray
            // 
            swi_tray.CheckedText = "开";
            swi_tray.Dock = DockStyle.Left;
            swi_tray.UnCheckedText = "关";
            swi_tray.Width = 80;
            // 
            // label_auto_refresh
            // 
            label_auto_refresh.Dock = DockStyle.Fill;
            label_auto_refresh.Text = "对局自动刷新：";
            label_auto_refresh.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_auto_refresh
            // 
            swi_auto_refresh.CheckedText = "开";
            swi_auto_refresh.Dock = DockStyle.Left;
            swi_auto_refresh.UnCheckedText = "关";
            swi_auto_refresh.Width = 80;
            // 
            // flow_auto_refresh
            // 
            flow_auto_refresh.Controls.Add(label_refresh_interval);
            flow_auto_refresh.Controls.Add(input_auto_refresh);
            flow_auto_refresh.Dock = DockStyle.Fill;
            flow_auto_refresh.Padding = new Padding(0, 6, 0, 0);
            // 
            // label_refresh_interval
            // 
            label_refresh_interval.Text = "刷新间隔(秒)：";
            label_refresh_interval.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // input_auto_refresh
            // 
            input_auto_refresh.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            input_auto_refresh.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
            input_auto_refresh.Value = new decimal(new int[] { 30, 0, 0, 0 });
            input_auto_refresh.Width = 110;
            // 
            // label_notify_end
            // 
            label_notify_end.Dock = DockStyle.Fill;
            label_notify_end.Text = "对局结束提醒：";
            label_notify_end.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_notify_end
            // 
            swi_notify_end.CheckedText = "开";
            swi_notify_end.Dock = DockStyle.Left;
            swi_notify_end.UnCheckedText = "关";
            swi_notify_end.Width = 80;
            // 
            // label_startup
            // 
            label_startup.Dock = DockStyle.Fill;
            label_startup.Text = "开机自动启动：";
            label_startup.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // swi_startup
            // 
            swi_startup.CheckedText = "开";
            swi_startup.Dock = DockStyle.Left;
            swi_startup.UnCheckedText = "关";
            swi_startup.Width = 80;
            // 
            // label_cache
            // 
            label_cache.Dock = DockStyle.Fill;
            label_cache.Text = "本地缓存：";
            label_cache.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // label_cache_status
            // 
            label_cache_status.Dock = DockStyle.Fill;
            label_cache_status.Text = "";
            label_cache_status.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // flow_ban_preview
            // 
            flow_ban_preview.AutoScroll = true;
            flow_ban_preview.BackColor = SystemColors.ControlLight;
            flow_ban_preview.BorderStyle = BorderStyle.FixedSingle;
            flow_ban_preview.Dock = DockStyle.Fill;
            // 
            // flow_pick_preview
            // 
            flow_pick_preview.AutoScroll = true;
            flow_pick_preview.BackColor = SystemColors.ControlLight;
            flow_pick_preview.BorderStyle = BorderStyle.FixedSingle;
            flow_pick_preview.Dock = DockStyle.Fill;
            // 
            // SettingForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(gridPanel2);
            DoubleBuffered = true;
            Name = "SettingForm";
            Size = new Size(828, 721);
            Load += SettingForm_Load;
            gridPanel2.ResumeLayout(false);
            flow_auto_refresh.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel gridPanel2;
        private AntdUI.Select select_resolution;
        private AntdUI.Label label_resolution;
        private AntdUI.Label label_cache;
        private AntdUI.Label label_cache_status;
        private AntdUI.Label label5;
        private AntdUI.SelectMultiple setting_select_jyx;
        private AntdUI.SelectMultiple setting_select_xyx;
        private AntdUI.Switch swi_xyx;
        private AntdUI.Switch swi_tray;
        private FlowLayoutPanel flow_ban_preview;
        private FlowLayoutPanel flow_pick_preview;
        private AntdUI.Label label_tray;
        private AntdUI.Switch swi_jyyx;
        private AntdUI.Switch swi_gametrue;
        private AntdUI.Switch swi_open;
        private AntdUI.Label label4;
        private AntdUI.Label label3;
        private AntdUI.Label label2;
        private AntdUI.Label label1;
        public AntdUI.InputNumber inputNumber1;
        private AntdUI.Label label_auto_refresh;
        private AntdUI.Switch swi_auto_refresh;
        private FlowLayoutPanel flow_auto_refresh;
        private AntdUI.Label label_refresh_interval;
        private AntdUI.InputNumber input_auto_refresh;
        private AntdUI.Label label_notify_end;
        private AntdUI.Switch swi_notify_end;
        private AntdUI.Label label_startup;
        private AntdUI.Switch swi_startup;
    }
}
