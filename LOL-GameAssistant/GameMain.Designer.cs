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
            tabPage2 = new AntdUI.TabPage();
            tabPage3 = new AntdUI.TabPage();
            tabPage4 = new AntdUI.TabPage();
            tabPage5 = new AntdUI.TabPage();
            HeadContent = new AntdUI.PageHeader();
            gridPanel1 = new AntdUI.GridPanel();
            tabs1.SuspendLayout();
            tabPage1.SuspendLayout();
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
            // gridPanel1
            // 
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(815, 419);
            gridPanel1.Span = "";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
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
            StartPosition = FormStartPosition.CenterParent;
            Text = "Form1";
            tabs1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
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
    }
}
