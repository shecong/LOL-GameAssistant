using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>普通弹窗使用 AntdUI 标题栏处理拖动、关闭和窗口按钮。</summary>
internal static class AntdWindowChrome
{
    private static readonly ConditionalWeakTable<AntdUI.Window, WindowRegistration> Registrations = new();

    /// <summary>配置无边框窗口及其绘制恢复、任务栏行为。</summary>
    internal static void Configure(AntdUI.Window window, bool fitWorkingArea = true)
    {
        window.FormBorderStyle = FormBorderStyle.None;
        ((Form)window).FormBorderStyle = FormBorderStyle.None;
        // 同一窗体只注册一次；重复配置不会叠加句柄事件，也不会重复安装原生消息处理。
        Registrations.GetValue(window, owner => new WindowRegistration(owner, fitWorkingArea));
    }

    /// <summary>为普通 AntdUI 弹窗添加支持拖动和窗口按钮的标题栏。</summary>
    internal static AntdUI.PageHeader AddHeader(AntdUI.Window window, bool fitWorkingArea = true)
    {
        Configure(window, fitWorkingArea);
        if (window.Controls.OfType<AntdUI.PageHeader>().FirstOrDefault() is { } existing) return existing;
        window.Icon ??= AppIcon.Shared;
        var header = new AntdUI.PageHeader
        {
            Text = window.Text, Dock = DockStyle.Top, Height = 42, ShowButton = true,
            MinimizeBox = window.MinimizeBox, MaximizeBox = window.MaximizeBox,
            EnableDoubleClickMaximize = window.MaximizeBox,
            UseSystemStyleColor = false, UseForeColorDrawIcons = true,
            Padding = new Padding(12, 0, 0, 0)
        };
        window.Controls.Add(header);
        window.TextChanged += (_, _) => header.Text = window.Text;
        return header;
    }

    /// <summary>通过窗口事件恢复句柄绘制状态，不引入自定义窗体基类。</summary>
    private sealed class WindowRegistration : NativeWindow
    {
        private readonly AntdUI.Window _window;
        private Size? _restoreClientSize;

        /// <summary>初始化 WindowRegistration 的实例状态，并保存传入的依赖或数据。</summary>
        internal WindowRegistration(AntdUI.Window window, bool fitWorkingArea)
        {
            _window = window;
            _restoreClientSize = window.ClientSize;
            window.HandleCreated += HandleCreated;
            window.HandleDestroyed += HandleDestroyed;
            window.SizeChanged += SizeChanged;
            window.Disposed += WindowDisposed;
            UiTheme.Changed += ThemeChanged;
            if (fitWorkingArea)
            {
                window.Load += PlaceWindow;
                window.Shown += FitWindow;
                window.DpiChanged += DpiChanged;
            }
            if (window.IsHandleCreated) HandleCreated(window, EventArgs.Empty);
        }

        /// <summary>绑定新窗口句柄并恢复与该句柄关联的状态。</summary>
        private void HandleCreated(object? sender, EventArgs e)
        {
            AssignHandle(_window.Handle);
            ApplyStyles();
            // 2.4.18 会在 OnHandleCreated 中重新按原生客户区计算大小；保留重建前的实际内容尺寸。
            if (_restoreClientSize is { } size)
            {
                _restoreClientSize = null;
                _window.ClientSize = size;
            }
            _window.RefreshDWM();
        }

        /// <summary>解除已销毁句柄的绑定，避免继续向旧句柄发送消息。</summary>
        private void HandleDestroyed(object? sender, EventArgs e)
        {
            if (!_window.Disposing && _window.WindowState == FormWindowState.Normal)
                _restoreClientSize = _window.ClientSize;
            ReleaseHandle();
        }

        private void SizeChanged(object? sender, EventArgs e)
        {
            // 首次创建前也保存设计尺寸，避免原生边框度量参与 AntdUI 的初始化计算。
            if (!_window.IsHandleCreated && !_window.Disposing) _restoreClientSize = _window.ClientSize;
        }

        /// <summary>窗口释放时注销事件并解除原生句柄绑定。</summary>
        private void WindowDisposed(object? sender, EventArgs e)
        {
            UiTheme.Changed -= ThemeChanged;
            _window.HandleCreated -= HandleCreated;
            _window.HandleDestroyed -= HandleDestroyed;
            _window.SizeChanged -= SizeChanged;
            _window.Disposed -= WindowDisposed;
            _window.Shown -= FitWindow;
            _window.Load -= PlaceWindow;
            _window.DpiChanged -= DpiChanged;
            ReleaseHandle();
        }

        private void ThemeChanged(object? sender, EventArgs e)
        {
            // Main window has its own global style refresh; independent windows follow it here.
            if (_window is GameMain || _window.IsDisposed || !_window.IsHandleCreated) return;
            void Apply() { if (!_window.IsDisposed) UiTheme.Apply(_window); }
            if (_window.InvokeRequired) _window.BeginInvoke(Apply); else Apply();
        }

        private void PlaceWindow(object? sender, EventArgs args) => WindowLayout.PlaceForOpening(_window);

        private void FitWindow(object? sender, EventArgs args)
        {
            WindowLayout.FitToWorkingArea(_window);
            if (!_window.IsDisposed && _window.IsHandleCreated)
                _window.BeginInvoke(() => WindowLayout.FitToWorkingArea(_window));
        }
        private void DpiChanged(object? sender, DpiChangedEventArgs args)
        {
            if (!_window.IsDisposed && _window.IsHandleCreated)
                _window.BeginInvoke(() => WindowLayout.FitToWorkingArea(_window));
        }

        /// <summary>处理当前窗口关注的原生消息，其余消息继续交给基类。</summary>
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0085: // WM_NCPAINT
                    // 客户端由应用自行绘制并铺满整个窗口，禁止系统再绘制原生标题栏和边框。
                    // AntdUI 只在检测到 DWM 合成关闭时拦截该消息；这里始终拦截，避免全屏游戏暂停
                    // 桌面合成或句柄重建时，建窗阶段残留的 WS_CAPTION 被系统画回自绘标题栏之上。
                    m.Result = IntPtr.Zero;
                    return;
                case 0x0083: // WM_NCCALCSIZE
                    // 系统重新计算非客户区前，若样式仍带标题栏（建窗/句柄重建瞬间），先剥离再计算。
                    if (m.WParam != IntPtr.Zero) StripCaptionStyle();
                    break;
            }
            base.WndProc(ref m);
            switch (m.Msg)
            {
                // WinForms 在窗口按钮属性变更时会重写样式，继续保留任务栏能力与无标题栏状态。
                case 0x007D: // WM_STYLECHANGED
                case 0x0018: // WM_SHOWWINDOW
                case 0x0006: // WM_ACTIVATE
                case 0x031E: // WM_DWMCOMPOSITIONCHANGED
                    if (_window.IsHandleCreated) ApplyStyles();
                    break;
                case 0x02E0: // WM_DPICHANGED
                    if (_window.IsHandleCreated) ApplyStyles(forceFrameRefresh: true);
                    break;
            }
        }

        /// <summary>在系统计算非客户区前移除标题栏样式，避免原生标题栏参与边框计算。</summary>
        private void StripCaptionStyle()
        {
            int original = GetWindowLong(Handle, -16);
            int style = original & ~0x00C00000; // WS_CAPTION (WS_BORDER | WS_DLGFRAME)
            if (original == style) return;
            // 不附加 FRAMECHANGED：这里正处于 WM_NCCALCSIZE 处理中，嵌套重算会打乱本次客户区计算。
            SetWindowLong(Handle, -16, style);
        }

        /// <summary>保留系统最小化能力标志，同时移除原生标题栏样式。</summary>
        private void ApplyStyles(bool forceFrameRefresh = false)
        {
            int original = GetWindowLong(Handle, -16);
            // 先清除标题栏、系统菜单及窗口按钮标志，再按窗体的实际配置补回能力标志。
            int style = original & ~(0x00C00000 | 0x00080000 | 0x00020000 | 0x00010000);
            if (_window.ControlBox)
            {
                style |= 0x00080000; // WS_SYSMENU
                if (_window.MinimizeBox) style |= 0x00020000;
                if (_window.MaximizeBox) style |= 0x00010000;
            }
            if (original != style)
            {
                SetWindowLong(Handle, -16, style);
                SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037); // FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE
                return;
            }
            // SetWindowLong 自身会触发 WM_STYLECHANGED；样式未变时默认退出，避免递归重写。
            if (!forceFrameRefresh) return;
            // DPI 或合成状态切换后强制刷新边框，清掉系统可能已经画出的原生标题栏残影。
            SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0481); // RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN
        }

        /// <summary>读取窗口的指定原生属性或样式。</summary>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(nint handle, int index);
        /// <summary>更新窗口的指定原生属性或样式。</summary>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(nint handle, int index, int value);
        /// <summary>调整窗口位置、层级或触发非客户区重绘。</summary>
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
        /// <summary>按指定范围刷新窗口边框和客户区。</summary>
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(nint handle, nint updateRect, nint updateRegion, uint flags);
    }
}
