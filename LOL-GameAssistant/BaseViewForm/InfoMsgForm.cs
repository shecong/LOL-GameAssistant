namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>消息区域：追加并清理运行中的状态提示。</summary>
    public partial class InfoMsgForm : UserControl, InfoMsgForm.IInfoMsgForm
    {
        /// <summary>初始化 InfoMsgForm 的实例状态。</summary>
        public InfoMsgForm()
        {
            InitializeComponent();
        }

        /// <summary>供业务调用方追加界面消息的接口。</summary>
        public interface IInfoMsgForm
        {
            /// <summary>向消息区域追加一条信息。</summary>
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

        /// <summary>响应清空菜单操作，移除当前显示的消息。</summary>
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