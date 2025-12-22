using AntdUI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class InfoMsgForm : UserControl, InfoMsgForm.IInfoMsgForm
    {
        public InfoMsgForm()
        {
            InitializeComponent();
        }

        public interface IInfoMsgForm
        {
            void AddMsg(string msg);
        }

        /// <summary>
        /// 通用添加方法
        /// </summary>
        /// <param name="msg"></param>
        public void AddMsg(string msg)
        {
            if (chat_msg.InvokeRequired)
            {
                this.Invoke(new Action<string>(AddMsg), msg);
            }
            else
            {
                chat_msg.AddToBottom(new AntdUI.Chat.TextChatItem($"{msg}", Properties.Resources.下载, $"Info:{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}"));
            }
        }
    }
}