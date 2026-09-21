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
            try
            {
                // 这个方法经常被后台线程和异常处理器调用，
                // 句柄还没建立时 Invoke 会抛"在创建窗口句柄之前，不能在控件上调用 Invoke"，
                // 抛出去会把异常处理器本身带崩，所以直接丢弃。
                if (IsDisposed || !IsHandleCreated) return;

                if (chat_msg.InvokeRequired)
                {
                    chat_msg.BeginInvoke(new Action<string>(AddMsg), msg);
                }
                else
                {
                    chat_msg.AddToBottom(new AntdUI.Chat.TextChatItem($"{msg}", Properties.Resources.下载, $"Info:{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}"));
                }
            }
            catch
            {
                // 日志窗口不可用不应影响主流程
            }
        }

        private void 清空消息ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //清空消息
            if (chat_msg.InvokeRequired)
            {
                this.Invoke(new Action<object, EventArgs>(清空消息ToolStripMenuItem_Click));
            }
            else
            {
                chat_msg.Items.Clear();
            }
        }
    }
}