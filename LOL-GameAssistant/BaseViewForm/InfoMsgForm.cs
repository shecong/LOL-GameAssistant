namespace LOL_GameAssistant.BaseViewForm
{
    using LOL_GameAssistant.Helper;

    /// <summary>消息区域：追加并清理运行中的状态提示。</summary>
    public partial class InfoMsgForm : UserControl, InfoMsgForm.IInfoMsgForm
    {
        internal const int MaximumMessages = 500;
        internal const int MaximumMessageLength = 4000;
        private readonly Queue<string> _messages = new();
        private DiagnosticEntry? _lastCoreDiagnostic;
        /// <summary>初始化 InfoMsgForm 的实例状态。</summary>
        public InfoMsgForm()
        {
            InitializeComponent();
            var copy = new AntdUI.Button { Name = "copyLogs", Text = "复制日志", Dock = DockStyle.Top, Height = 38 };
            copy.Click += (_, _) => CopyLogs();
            Controls.Add(copy);
            RuntimeDiagnostics.Reported += AppendCoreDiagnostic;
            HandleCreated += (_, _) =>
            {
                var latest = RuntimeDiagnostics.Snapshot().FirstOrDefault(entry => entry.Component == RuntimeDiagnostics.SkinCoreComponent);
                if (latest != null) AppendCoreDiagnostic(latest);
            };
            Disposed += (_, _) => RuntimeDiagnostics.Reported -= AppendCoreDiagnostic;
        }

        private void AppendCoreDiagnostic(DiagnosticEntry entry)
        {
            if (entry.Component != RuntimeDiagnostics.SkinCoreComponent || IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) { BeginInvoke(() => AppendCoreDiagnostic(entry)); return; }
                if (_lastCoreDiagnostic == entry) return;
                _lastCoreDiagnostic = entry;
                AddMsg($"{entry.Component} · {entry.Status}\n{entry.Detail}");
            }
            catch (InvalidOperationException) { }
        }

        internal string GetLogText() => string.Join(Environment.NewLine + Environment.NewLine, _messages);

        private void CopyLogs()
        {
            string text = GetLogText();
            if (string.IsNullOrEmpty(text))
            {
                if (FindForm() is Form owner) UiMessage.info(owner, "暂无日志，请先重现问题后再复制。");
                return;
            }
            try
            {
                Clipboard.SetText(text);
                if (FindForm() is Form owner) UiMessage.success(owner, "日志已复制，可以直接粘贴反馈。");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (FindForm() is Form owner) UiMessage.warn(owner, "剪贴板暂时被占用，请再次点击复制日志。");
            }
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
                    if (msg.Length > MaximumMessageLength) msg = msg[..MaximumMessageLength] + "…";
                    _messages.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
                    while (_messages.Count > MaximumMessages) _messages.Dequeue();
                    chat_msg.AddToBottom(new AntdUI.Chat.TextChatItem($"{msg}", Properties.Resources.下载, $"Info:{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}"));
                    while (chat_msg.Items.Count > MaximumMessages) chat_msg.Items.RemoveAt(0);
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
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() => 清空消息ToolStripMenuItem_Click(sender, e));
            }
            else
            {
                chat_msg.Items.Clear();
                    _messages.Clear();
            }
        }
    }
}
