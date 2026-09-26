using LOL_GameAssistant.Domain.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// 置顶键由 Windows 注册热键触发，按键状态负责松开；喊话保留独立的键盘钩子。
/// </summary>
public sealed class WindowHoldController : IDisposable
{
    public const int HotkeyMessage = 0x0312;
    private const int HoldHotkeyId = 0x4C47;
    private const uint ModNoRepeat = 0x4000;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int SwShownoactivate = 4;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly Form _window;
    private readonly LowLevelKeyboardProc _callback;
    private readonly System.Windows.Forms.Timer _hotkeyWatch = new() { Interval = 120 };
    private IntPtr _hook;
    private IntPtr _registeredHandle;
    private Keys _hotkey = Keys.Oem3;
    private Keys _shoutBuiltInKey = Keys.F6;
    private Keys _shoutCustomKey = Keys.F7;
    private bool _shoutHotkeysEnabled;
    private Action<bool>? _shoutAction;
    private bool _onlyWhenLeagueFocused = true;
    private bool _capturePaused;
    private bool _holding;
    private bool _disposed;
    private long _nextRegisterAttempt;

    public WindowHoldController(Form window)
    {
        _window = window;
        _callback = HookCallback;
        _window.HandleCreated += WindowHandleCreated;
        _window.HandleDestroyed += WindowHandleDestroyed;
        _hotkeyWatch.Tick += (_, _) =>
        {
            if (_holding && ((GetAsyncKeyState((int)_hotkey) & 0x8000) == 0 ||
                (_onlyWhenLeagueFocused && !IsLeagueForeground())))
                EndHold();
            RefreshRegistration();
        };
        _hotkeyWatch.Start();
    }

    public void Apply(AssistantSettings config)
    {
        Keys newKey = ParseKey(config.HoldToTopHotkey);
        if (_holding) EndHold();
        if (_hotkey != newKey) UnregisterHoldHotkey();
        _hotkey = newKey;
        _onlyWhenLeagueFocused = config.HoldToTopOnlyWhenLeagueFocused;
        _window.Opacity = Math.Clamp(config.WindowOpacityPercent, 40, 100) / 100D;
        _nextRegisterAttempt = 0;
        RefreshRegistration();
    }

    /// <summary>设置页录入快捷键期间暂停注册，避免旧快捷键吃掉输入。</summary>
    public void SetCapturePaused(bool paused)
    {
        _capturePaused = paused;
        if (paused) UnregisterHoldHotkey();
        else RefreshRegistration();
    }

    public void ConfigureQuickShoutHotkeys(AssistantSettings config, Action<bool> action)
    {
        _shoutAction = action;
        _shoutBuiltInKey = ParseFunctionKey(config.QuickShoutBuiltInHotkey, Keys.F6);
        _shoutCustomKey = ParseFunctionKey(config.QuickShoutCustomHotkey, Keys.F7);
        _shoutHotkeysEnabled = config.QuickShoutHotkeysEnabled &&
            _shoutBuiltInKey != _shoutCustomKey && _shoutBuiltInKey != _hotkey &&
            _shoutCustomKey != _hotkey;
        if (_shoutHotkeysEnabled) InstallShoutHook();
        else RemoveShoutHook();
        RuntimeDiagnostics.Report("游戏内喊话快捷键",
            _shoutHotkeysEnabled && _hook != IntPtr.Zero ? "已启用" : "不可用",
            _shoutHotkeysEnabled
                ? $"默认词库 {_shoutBuiltInKey} · 自定义词库 {_shoutCustomKey} · 仅游戏前台"
                : "已关闭、快捷键重复或与置顶键冲突");
    }

    private static Keys ParseFunctionKey(string? value, Keys fallback) =>
        Enum.TryParse(value, true, out Keys key) && key is >= Keys.F2 and <= Keys.F12
            ? key : fallback;

    public static Keys ParseKey(string? value) =>
        Enum.TryParse(value, true, out Keys parsed) && parsed != Keys.None
            ? parsed : Keys.Oem3;

    public static string DescribeKey(Keys key) => key == Keys.Oem3 ? "·" : key.ToString();

    private void WindowHandleCreated(object? sender, EventArgs e) => RefreshRegistration();

    private void WindowHandleDestroyed(object? sender, EventArgs e) => UnregisterHoldHotkey();

    private void RefreshRegistration()
    {
        if (_disposed || !_window.IsHandleCreated) return;
        bool shouldRegister = !_capturePaused &&
            (!_onlyWhenLeagueFocused || IsLeagueForeground());
        if (!shouldRegister)
        {
            if (_registeredHandle != IntPtr.Zero)
                RuntimeDiagnostics.Report("按住置顶键", "待机", "等待 LOL 窗口位于前台");
            UnregisterHoldHotkey();
            return;
        }
        if (_registeredHandle == _window.Handle) return;
        if (Environment.TickCount64 < _nextRegisterAttempt) return;
        UnregisterHoldHotkey();
        if (RegisterHotKey(_window.Handle, HoldHotkeyId, ModNoRepeat, (uint)_hotkey))
        {
            _registeredHandle = _window.Handle;
            RuntimeDiagnostics.Report("按住置顶键", "已注册",
                $"{DescribeKey(_hotkey)} · {(_onlyWhenLeagueFocused ? "仅 LOL 前台" : "所有窗口")}");
        }
        else
        {
            _nextRegisterAttempt = Environment.TickCount64 + 3000;
            RuntimeDiagnostics.Report("按住置顶键", "不可用",
                $"Windows 注册热键失败（{Marshal.GetLastWin32Error()}），请更换按键或关闭占用该键的程序");
        }
    }

    private void UnregisterHoldHotkey()
    {
        if (_registeredHandle == IntPtr.Zero) return;
        UnregisterHotKey(_registeredHandle, HoldHotkeyId);
        _registeredHandle = IntPtr.Zero;
    }

    /// <summary>由主窗体的 WndProc 转交 WM_HOTKEY。</summary>
    public bool HandleHotkey(IntPtr wParam)
    {
        if (wParam != (IntPtr)HoldHotkeyId) return false;
        if (_disposed || _capturePaused || _registeredHandle == IntPtr.Zero || _holding)
            return true;
        if (_onlyWhenLeagueFocused && !IsLeagueForeground()) return true;
        _holding = true;
        BeginHold();
        return true;
    }

    private void BeginHold()
    {
        if (_window.IsDisposed) return;
        ShowWindow(_window.Handle, SwShownoactivate);
        if (!SetWindowPos(_window.Handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow))
        {
            RuntimeDiagnostics.Report("按住置顶键", "显示失败",
                $"Windows 置顶失败（{Marshal.GetLastWin32Error()}）");
            _holding = false;
            _window.Hide();
            return;
        }
        RuntimeDiagnostics.Report("按住置顶键", "显示中",
            $"按住 {DescribeKey(_hotkey)} 时以非激活方式置顶");
    }

    private void EndHold()
    {
        _holding = false;
        if (_window.IsDisposed) return;
        SetWindowPos(_window.Handle, HwndNotopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        _window.Hide();
        RuntimeDiagnostics.Report("按住置顶键", "已隐藏",
            "已松开快捷键，可从托盘恢复窗口");
    }

    private void InstallShoutHook()
    {
        if (_hook != IntPtr.Zero || _disposed) return;
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
    }

    private void RemoveShoutHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _disposed)
            return CallNextHookEx(_hook, code, wParam, lParam);
        int virtualKey = Marshal.ReadInt32(lParam);
        bool keyUp = wParam == (IntPtr)WmKeyUp || wParam == (IntPtr)WmSysKeyUp;
        bool keyDown = wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown;
        if (_shoutHotkeysEnabled && (keyDown || keyUp) &&
            (virtualKey == (int)_shoutBuiltInKey || virtualKey == (int)_shoutCustomKey) &&
            IsLeagueGameForeground())
        {
            if (keyUp)
            {
                bool custom = virtualKey == (int)_shoutCustomKey;
                RunOnWindowThread(() => _shoutAction?.Invoke(custom));
            }
            return (IntPtr)1;
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
        catch (InvalidOperationException) { /* 窗口正在关闭 */ }
    }

    private static bool IsLeagueForeground() => IsForegroundProcess(
        "League of Legends", "LeagueClient", "LeagueClientUx");

    private static bool IsLeagueGameForeground() => IsForegroundProcess("League of Legends");

    private static bool IsForegroundProcess(params string[] names)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == 0) return false;
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return names.Any(name => process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkeyWatch.Stop();
        _hotkeyWatch.Dispose();
        _window.HandleCreated -= WindowHandleCreated;
        _window.HandleDestroyed -= WindowHandleDestroyed;
        UnregisterHoldHotkey();
        RemoveShoutHook();
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
}