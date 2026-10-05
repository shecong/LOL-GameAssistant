using LOL_GameAssistant.BaseViewForm;

namespace LOL_GameAssistant.Helper;

/// <summary>头像/名称的单击复制与双击查战绩，等待双击间隔避免误触复制。</summary>
internal sealed class PlayerIdentityActions : IDisposable
{
    private readonly Func<string?> _identity;
    private readonly Action<string> _copy;
    private readonly Action<string> _query;
    private readonly System.Windows.Forms.Timer _clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private string? _pendingIdentity;

    /// <summary>初始化 PlayerIdentityActions 的实例状态，并保存传入的依赖或数据。</summary>
    public PlayerIdentityActions(Func<string?> identity, Func<Form?> owner,
        Action<string>? copy = null, Action<string>? query = null)
    {
        _identity = identity;
        _copy = copy ?? (id =>
        {
            try
            {
                Clipboard.SetText(id);
                if (owner() is { IsDisposed: false } form) UiMessage.success(form, "已复制玩家 ID");
            }
            catch { /* 剪贴板占用时不影响卡片操作。 */ }
        });
        _query = query ?? (id => { _ = BattleQueryForm.QueryPlayerAsync(id); });
        _clickTimer.Tick += (_, _) => CompleteSingleClick();
    }

    /// <summary>绑定目标控件或窗口的相关事件。</summary>
    public void Attach(Control control)
    {
        control.Cursor = Cursors.Hand;
        control.MouseDown += (_, args) => HandleMouseDown(args);
    }

    /// <summary>记录玩家身份区域的鼠标操作，区分单击和双击行为。</summary>
    internal void HandleMouseDown(MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        string? id = _identity();
        if (string.IsNullOrWhiteSpace(id)) return;
        _clickTimer.Stop();
        _pendingIdentity = null;
        if (args.Clicks >= 2) _query(id);
        else
        {
            _pendingIdentity = id;
            _clickTimer.Start();
        }
    }

    /// <summary>双击等待窗口结束后执行单击操作。</summary>
    internal void CompleteSingleClick()
    {
        _clickTimer.Stop();
        string? id = _pendingIdentity;
        _pendingIdentity = null;
        if (id != null) _copy(id);
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    public void Dispose()
    {
        _pendingIdentity = null;
        _clickTimer.Dispose();
    }
}
