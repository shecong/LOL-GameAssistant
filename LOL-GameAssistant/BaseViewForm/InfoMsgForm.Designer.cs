namespace LOL_GameAssistant.BaseViewForm
{
    partial class InfoMsgForm
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
            gridPanel1 = new AntdUI.GridPanel();
            chat_msg = new AntdUI.Chat.ChatList();
            gridPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // gridPanel1
            // 
            gridPanel1.Controls.Add(chat_msg);
            gridPanel1.Dock = DockStyle.Fill;
            gridPanel1.Location = new Point(0, 0);
            gridPanel1.Name = "gridPanel1";
            gridPanel1.Size = new Size(1153, 808);
            gridPanel1.Span = "100%";
            gridPanel1.TabIndex = 0;
            gridPanel1.Text = "gridPanel1";
            // 
            // chat_msg
            // 
            chat_msg.Location = new Point(3, 3);
            chat_msg.Name = "chat_msg";
            chat_msg.Size = new Size(1147, 802);
            chat_msg.TabIndex = 0;
            chat_msg.Text = "chatList1";
            // 
            // InfoMsgForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(gridPanel1);
            Name = "InfoMsgForm";
            Size = new Size(1153, 808);
            gridPanel1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private AntdUI.GridPanel gridPanel1;
        private AntdUI.Chat.ChatList chat_msg;
    }
}
