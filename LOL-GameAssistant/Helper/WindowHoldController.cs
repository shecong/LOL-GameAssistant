using System.Diagnostics;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// 通过 Windows 全局键盘钩子实现“按住显示，松开还原”。不读写游戏内存、不注入游戏进程。
/// </summary>
public sealed class WindowHoldController : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly Form _window;
    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook = IntPtr.Zero;
    private Keys _hotkey = Keys.Oem3;
    private bool _onlyWhenLeagueFocused = true;
    private bool _holding;
    private bool _topMostBeforeHold;
    private bool _visibleBeforeHold;
    private bool _minimizedBeforeHold;
    private readonly System.Windows.Forms.Timer _releaseWatchdog = new() { Interval = 40 };
    private bool _disposed;

    public WindowHoldController(Form window)
    {
        _window = window;
        _callback = HookCallback;
        InstallHook();
        _releaseWatchdog.Tick += (_, _) => RestoreIfKeyReleased();
    }

    public void Apply(AssistantSettings config)
    {
        _hotkey = ParseKey(config.HoldToTopHotkey);
        _onlyWhenLeagueFocused = config.HoldToTopOnlyWhenLeagueFocused;
        _window.Opacity = Math.Clamp(config.WindowOpacityPercent, 40, 100) / 100D;
    }

    public static Keys ParseKey(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out Keys parsed) && parsed != Keys.None
            ? parsed
            : Keys.Oem3;
    }

    public static string DescribeKey(Keys key)
    {
        return key == Keys.Oem3 ? "·" : key.ToString();
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero || _disposed) return;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        IntPtr moduleHandle = GetModuleHandle(module?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, moduleHandle, 0);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _disposed)
            return CallNextHookEx(_hook, code, wParam, lParam);

        int virtualKey = Marshal.ReadInt32(lParam);
        bool keyDown = wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown;
        bool keyUp = wParam == (IntPtr)WmKeyUp || wParam == (IntPtr)WmSysKeyUp;

        if (virtualKey == (int)_hotkey)
        {
            if (keyDown && !_holding && (!_onlyWhenLeagueFocused || IsLeagueForeground()))
            {
                _holding = true;
                RunOnWindowThread(BeginHold);
            }
            else if (keyUp && _holding)
            {
                _holding = false;
                RunOnWindowThread(EndHold);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void RunOnWindowThread(Action action)
    {
        if (_window.IsDisposed || !_window.IsHandleCreated) return;

        try
        {
            if (_window.InvokeRequired) _window.BeginInvoke(action);
            else action();
        }
        catch (InvalidOperationException)
        {
            // 窗口正在关闭。
        }
    }

    private void BeginHold()
    {
        if (_window.IsDisposed) return;
        _topMostBeforeHold = _window.TopMost;
        _visibleBeforeHold = _window.Visible;
        _minimizedBeforeHold = _window.WindowState == FormWindowState.Minimized;
        if (!_window.Visible) _window.Show();
        if (_window.WindowState == FormWindowState.Minimized)
            _window.WindowState = FormWindowState.Normal;
        _window.TopMost = true;
        // 不 Activate / BringToFront：否则按下显示时助手会抢走游戏焦点，
        // 一些键盘驱动也会因此吞掉原按键的 KeyUp。
        _releaseWatchdog.Start();
    }

    private void EndHold()
    {
        _releaseWatchdog.Stop();
        if (_window.IsDisposed) return;
        _window.TopMost = _topMostBeforeHold;
        if (!_visibleBeforeHold)
            _window.Hide();
        else if (_minimizedBeforeHold)
            _window.WindowState = FormWindowState.Minimized;
    }

    /// <summary>
    /// KeyUp 被 IME、叠加层或切换前台窗口吞掉时，仍按物理键状态及时恢复。
    /// </summary>
    private void RestoreIfKeyReleased()
    {
        if (!_holding || _disposed) return;
        if ((GetAsyncKeyState((int)_hotkey) & 0x8000) != 0) return;
        _holding = false;
        EndHold();
    }

    private static bool IsLeagueForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == 0) return false;

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClient", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClientUx", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _releaseWatchdog.Stop();
        _releaseWatchdog.Dispose();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
