using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>在无边框主窗口显示后向 Shell 登记任务栏按钮，并跟随窗口句柄和可见性。</summary>
internal sealed class TaskbarWindowRegistration : NativeWindow, IDisposable
{
    internal static readonly int TaskbarCreatedMessage = (int)RegisterWindowMessage("TaskbarCreated");
    private readonly Form _form;
    private readonly Func<nint, bool, int> _update;
    private nint _registeredHandle;
    private bool _disposed;

    /// <summary>初始化 TaskbarWindowRegistration 的实例状态，并保存传入的依赖或数据。</summary>
    public TaskbarWindowRegistration(Form form, Func<nint, bool, int>? update = null)
    {
        _form = form;
        _update = update ?? UpdateShell;
        form.HandleCreated += HandleCreated;
        form.HandleDestroyed += HandleDestroyed;
        form.VisibleChanged += VisibilityChanged;
        form.Shown += VisibilityChanged;
        form.Disposed += FormDisposed;
        if (form.IsHandleCreated) AssignHandle(form.Handle);
        Synchronize();
    }

    /// <summary>绑定新窗口句柄并恢复与该句柄关联的状态。</summary>
    private void HandleCreated(object? sender, EventArgs e)
    {
        AssignHandle(_form.Handle);
        Synchronize();
    }

    /// <summary>解除已销毁句柄的绑定，避免继续向旧句柄发送消息。</summary>
    private void HandleDestroyed(object? sender, EventArgs e)
    {
        Remove();
        ReleaseHandle();
    }

    /// <summary>窗口可见性变化后同步任务栏登记状态。</summary>
    private void VisibilityChanged(object? sender, EventArgs e) => Synchronize();
    /// <summary>窗体释放后结束对应的资源管理流程。</summary>
    private void FormDisposed(object? sender, EventArgs e) => Dispose();

    /// <summary>使任务栏登记与当前窗口句柄、可见性及配置一致。</summary>
    internal void Synchronize()
    {
        if (_disposed) return;
        if (!_form.IsHandleCreated || !_form.Visible || !_form.ShowInTaskbar)
        {
            Remove();
            return;
        }
        nint handle = _form.Handle;
        if (_registeredHandle == handle) return;
        Remove();
        if (_update(handle, true) >= 0) _registeredHandle = handle;
    }

    /// <summary>移除当前已登记的任务栏窗口。</summary>
    private void Remove()
    {
        if (_registeredHandle == 0) return;
        _update(_registeredHandle, false);
        _registeredHandle = 0;
    }

    /// <summary>处理当前窗口关注的原生消息，其余消息继续交给基类。</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != TaskbarCreatedMessage || _disposed) return;
        // Explorer 重启时原来的登记已经丢失；等 Shell 完成当前消息再重建按钮。
        _registeredHandle = 0;
        if (_form.IsHandleCreated) _form.BeginInvoke(new Action(Synchronize));
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Remove();
        _disposed = true;
        _form.HandleCreated -= HandleCreated;
        _form.HandleDestroyed -= HandleDestroyed;
        _form.VisibleChanged -= VisibilityChanged;
        _form.Shown -= VisibilityChanged;
        _form.Disposed -= FormDisposed;
        ReleaseHandle();
    }

    /// <summary>通过 Shell 接口添加或移除任务栏窗口按钮。</summary>
    internal static int UpdateShell(nint handle, bool visible)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), throwOnError: true)!);
            var taskbar = (ITaskbarList)instance!;
            taskbar.HrInit();
            if (visible) taskbar.AddTab(handle);
            else taskbar.DeleteTab(handle);
            return 0;
        }
        catch (COMException ex)
        {
            RuntimeDiagnostics.Report("任务栏", visible ? "登记窗口失败" : "移除窗口失败", ex.Message);
            return ex.HResult;
        }
        finally
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>注册跨进程可识别的窗口消息。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    /// <summary>用于管理任务栏窗口按钮的 Windows COM 接口。</summary>
    [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        /// <summary>初始化 Windows 任务栏 COM 接口。</summary>
        void HrInit();
        /// <summary>向 Windows 任务栏登记窗口按钮。</summary>
        void AddTab(nint hwnd);
        /// <summary>从 Windows 任务栏移除窗口按钮。</summary>
        void DeleteTab(nint hwnd);
        /// <summary>通知 Shell 激活指定的任务栏窗口。</summary>
        void ActivateTab(nint hwnd);
        /// <summary>设置 Shell 当前活动窗口的备用标识。</summary>
        void SetActiveAlt(nint hwnd);
    }
}
