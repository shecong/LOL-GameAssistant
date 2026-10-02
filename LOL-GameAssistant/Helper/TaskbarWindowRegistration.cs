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

    private void HandleCreated(object? sender, EventArgs e)
    {
        AssignHandle(_form.Handle);
        Synchronize();
    }

    private void HandleDestroyed(object? sender, EventArgs e)
    {
        Remove();
        ReleaseHandle();
    }

    private void VisibilityChanged(object? sender, EventArgs e) => Synchronize();
    private void FormDisposed(object? sender, EventArgs e) => Dispose();

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

    private void Remove()
    {
        if (_registeredHandle == 0) return;
        _update(_registeredHandle, false);
        _registeredHandle = 0;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != TaskbarCreatedMessage || _disposed) return;
        // Explorer 重启时原来的登记已经丢失；等 Shell 完成当前消息再重建按钮。
        _registeredHandle = 0;
        if (_form.IsHandleCreated) _form.BeginInvoke(new Action(Synchronize));
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
    }
}
