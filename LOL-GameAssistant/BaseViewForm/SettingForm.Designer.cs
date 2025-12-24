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
            gridPanel2 = new AntdUI.GridPanel();
            inputNumber1 = new AntdUI.InputNumber();
            label5 = new AntdUI.Label();
            setting_select_jyx = new AntdUI.SelectMultiple();
            setting_select_xyx = new AntdUI.SelectMultiple();
            swi_xyx = new AntdUI.Switch();
            swi_jyyx = new AntdUI.Switch();
            swi_gametrue = new AntdUI.Switch();
            swi_open = new AntdUI.Switch();
            label4 = new AntdUI.Label();
            label3 = new AntdUI.Label();
            label2 = new AntdUI.Label();
            label1 = new AntdUI.Label();
            gridPanel2.SuspendLayout();
            SuspendLayout();
            // 
            // gridPanel2
            // 
            gridPanel2.Controls.Add(inputNumber1);
            gridPanel2.Controls.Add(label5);
            gridPanel2.Controls.Add(setting_select_jyx);
            gridPanel2.Controls.Add(setting_select_xyx);
            gridPanel2.Controls.Add(swi_xyx);
            gridPanel2.Controls.Add(swi_jyyx);
            gridPanel2.Controls.Add(swi_gametrue);
            gridPanel2.Controls.Add(swi_open);
            gridPanel2.Controls.Add(label4);
            gridPanel2.Controls.Add(label3);
            gridPanel2.Controls.Add(label2);
            gridPanel2.Controls.Add(label1);
            gridPanel2.Dock = DockStyle.Fill;
            gridPanel2.Location = new Point(0, 0);
            gridPanel2.Name = "gridPanel2";
            gridPanel2.Size = new Size(828, 721);
            gridPanel2.Span = "15% 15%;15% 15%;15% 15% 50%;15% 15% 50%;15% 15%;\r\n-50  50 50 50 50";
            gridPanel2.TabIndex = 1;
            gridPanel2.Text = "  ";
            // 
            // inputNumber1
            // 
            gridPanel2.SetIndex(inputNumber1, 12);
            inputNumber1.Location = new Point(127, 203);
            inputNumber1.Name = "inputNumber1";
            inputNumber1.Size = new Size(118, 44);
            inputNumber1.TabIndex = 13;
            inputNumber1.Text = "2";
            inputNumber1.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // label5
            // 
            gridPanel2.SetIndex(label5, 11);
            label5.Location = new Point(3, 203);
            label5.Name = "label5";
            label5.Size = new Size(118, 44);
            label5.TabIndex = 12;
            label5.Text = "定时检查时间(秒)";
            // 
            // setting_select_jyx
            // 
            setting_select_jyx.AllowClear = true;
            setting_select_jyx.AllowDrop = true;
            setting_select_jyx.AutoHeight = true;
            setting_select_jyx.CheckMode = true;
            setting_select_jyx.DropDownArrow = true;
            setting_select_jyx.Empty = true;
            gridPanel2.SetIndex(setting_select_jyx, 7);
            setting_select_jyx.List = true;
            setting_select_jyx.Location = new Point(251, 103);
            setting_select_jyx.MaxCount = 10;
            setting_select_jyx.MinimumSize = new Size(0, 50);
            setting_select_jyx.Multiline = true;
            setting_select_jyx.Name = "setting_select_jyx";
            setting_select_jyx.PlaceholderText = "下拉选择英雄";
            setting_select_jyx.Size = new Size(408, 50);
            setting_select_jyx.TabIndex = 11;
            // 
            // setting_select_xyx
            // 
            setting_select_xyx.AllowClear = true;
            setting_select_xyx.AutoHeight = true;
            setting_select_xyx.CheckMode = true;
            gridPanel2.SetIndex(setting_select_xyx, 10);
            setting_select_xyx.Location = new Point(251, 153);
            setting_select_xyx.MaxCount = 10;
            setting_select_xyx.MinimumSize = new Size(0, 50);
            setting_select_xyx.Multiline = true;
            setting_select_xyx.Name = "setting_select_xyx";
            setting_select_xyx.PlaceholderText = "下拉选择英雄";
            setting_select_xyx.Size = new Size(408, 50);
            setting_select_xyx.TabIndex = 10;
            // 
            // swi_xyx
            // 
            swi_xyx.CheckedText = "true";
            gridPanel2.SetIndex(swi_xyx, 9);
            swi_xyx.Location = new Point(127, 153);
            swi_xyx.Name = "swi_xyx";
            swi_xyx.Size = new Size(118, 44);
            swi_xyx.TabIndex = 8;
            swi_xyx.Text = "switch4";
            // 
            // swi_jyyx
            // 
            swi_jyyx.CheckedText = "true";
            gridPanel2.SetIndex(swi_jyyx, 6);
            swi_jyyx.Location = new Point(127, 103);
            swi_jyyx.Name = "swi_jyyx";
            swi_jyyx.Size = new Size(118, 44);
            swi_jyyx.TabIndex = 7;
            swi_jyyx.Text = "switch3";
            // 
            // swi_gametrue
            // 
            swi_gametrue.CheckedText = "true";
            gridPanel2.SetIndex(swi_gametrue, 4);
            swi_gametrue.Location = new Point(127, 53);
            swi_gametrue.Name = "swi_gametrue";
            swi_gametrue.Size = new Size(118, 44);
            swi_gametrue.TabIndex = 6;
            swi_gametrue.Text = "switch2";
            // 
            // swi_open
            // 
            swi_open.CheckedText = "true";
            gridPanel2.SetIndex(swi_open, 2);
            swi_open.Location = new Point(127, 3);
            swi_open.Name = "swi_open";
            swi_open.Size = new Size(118, 44);
            swi_open.TabIndex = 5;
            swi_open.Text = "switch1";
            // 
            // label4
            // 
            gridPanel2.SetIndex(label4, 8);
            label4.Location = new Point(3, 153);
            label4.Name = "label4";
            label4.Size = new Size(118, 44);
            label4.TabIndex = 3;
            label4.Text = "自动选英雄：";
            // 
            // label3
            // 
            gridPanel2.SetIndex(label3, 5);
            label3.Location = new Point(3, 103);
            label3.Name = "label3";
            label3.Size = new Size(118, 44);
            label3.TabIndex = 2;
            label3.Text = "自动禁英雄：";
            // 
            // label2
            // 
            gridPanel2.SetIndex(label2, 3);
            label2.Location = new Point(3, 53);
            label2.Name = "label2";
            label2.Size = new Size(118, 44);
            label2.TabIndex = 1;
            label2.Text = "自动接受对局：";
            // 
            // label1
            // 
            gridPanel2.SetIndex(label1, 1);
            label1.Location = new Point(3, 3);
            label1.Name = "label1";
            label1.Size = new Size(118, 44);
            label1.TabIndex = 0;
            label1.Text = "自动匹配对局：";
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
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.GridPanel gridPanel2;
        private AntdUI.Label label5;
        private AntdUI.SelectMultiple setting_select_jyx;
        private AntdUI.SelectMultiple setting_select_xyx;
        private AntdUI.Switch swi_xyx;
        private AntdUI.Switch swi_jyyx;
        private AntdUI.Switch swi_gametrue;
        private AntdUI.Switch swi_open;
        private AntdUI.Label label4;
        private AntdUI.Label label3;
        private AntdUI.Label label2;
        private AntdUI.Label label1;
        public AntdUI.InputNumber inputNumber1;
    }
}
