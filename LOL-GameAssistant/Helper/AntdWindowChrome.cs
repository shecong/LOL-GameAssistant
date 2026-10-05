using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>普通弹窗使用 AntdUI 标题栏处理拖动、关闭和窗口按钮。</summary>
internal static class AntdWindowChrome
{
    private static readonly ConditionalWeakTable<AntdUI.Window, WindowRegistration> Registrations = new();

    /// <summary>配置无边框窗口及其绘制恢复、任务栏行为。</summary>
    internal static void Configure(AntdUI.Window window)
    {
        window.FormBorderStyle = FormBorderStyle.None;
        // 同一窗体只注册一次；重复配置不会叠加句柄事件，也不会重复安装原生消息处理。
        Registrations.GetValue(window, static owner => new WindowRegistration(owner));
    }

    /// <summary>为普通 AntdUI 弹窗添加支持拖动和窗口按钮的标题栏。</summary>
    internal static AntdUI.PageHeader AddHeader(AntdUI.Window window)
    {
        Configure(window);
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
        private bool _hasCreatedHandle;

        /// <summary>初始化 WindowRegistration 的实例状态，并保存传入的依赖或数据。</summary>
        internal WindowRegistration(AntdUI.Window window)
        {
            _window = window;
            window.HandleCreated += HandleCreated;
            window.HandleDestroyed += HandleDestroyed;
            window.Disposed += WindowDisposed;
            if (window.IsHandleCreated) HandleCreated(window, EventArgs.Empty);
        }

        /// <summary>绑定新窗口句柄并恢复与该句柄关联的状态。</summary>
        private void HandleCreated(object? sender, EventArgs e)
        {
            AssignHandle(_window.Handle);
            // AntdUI 2.4.12 在句柄重建后跳过 OnCreated；恢复绘制但不重复 DPI 缩放。
            if (_hasCreatedHandle) _window.OnCreated\u200C();
            _hasCreatedHandle = true;
            ApplyStyles();
        }

        /// <summary>解除已销毁句柄的绑定，避免继续向旧句柄发送消息。</summary>
        private void HandleDestroyed(object? sender, EventArgs e) => ReleaseHandle();

        /// <summary>窗口释放时注销事件并解除原生句柄绑定。</summary>
        private void WindowDisposed(object? sender, EventArgs e)
        {
            _window.HandleCreated -= HandleCreated;
            _window.HandleDestroyed -= HandleDestroyed;
            _window.Disposed -= WindowDisposed;
            ReleaseHandle();
        }

        /// <summary>处理当前窗口关注的原生消息，其余消息继续交给基类。</summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WinForms 在窗口按钮属性变更时会重写样式，继续保留任务栏能力与无标题栏状态。
            if (m.Msg == 0x007D && _window.IsHandleCreated) ApplyStyles(); // WM_STYLECHANGED
        }

        /// <summary>保留系统最小化能力标志，同时移除原生标题栏样式。</summary>
        private void ApplyStyles()
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
            // SetWindowLong 自身会触发 WM_STYLECHANGED；样式未变时必须退出，避免递归重写。
            if (original == style) return;
            SetWindowLong(Handle, -16, style);
            SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037); // FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE
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
    }
}
