using LOL_GameAssistant.Domain.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

public enum QuickShoutHotkeyAction { RandomBuiltIn, RandomCustom, SelectedBatch, GameKda, Player1 = 100, Player2, Player3, Player4, Player5, Player6, Player7, Player8, Player9, Player10 }

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
    private Dictionary<Keys, QuickShoutHotkeyAction> _gameHotkeys = new();
    private Action<QuickShoutHotkeyAction>? _shoutAction;
    private readonly Dictionary<int, QuickShoutHotkeyAction> _pressedGameHotkeys = new();
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
        _pressedGameHotkeys.Clear();
        if (paused) UnregisterHoldHotkey();
        else RefreshRegistration();
    }

    public void ConfigureQuickShoutHotkeys(AssistantSettings config, Action<QuickShoutHotkeyAction> action)
    {
        _shoutAction = action;
        _pressedGameHotkeys.Clear();
        _gameHotkeys = CreateGameHotkeyBindings(config);
        if (_gameHotkeys.Count > 0) InstallShoutHook();
        else RemoveShoutHook();
        RuntimeDiagnostics.Report("游戏内喊话快捷键",
            _gameHotkeys.Count > 0 && _hook != IntPtr.Zero ? "已启用" : "不可用",
            string.Join(" · ", _gameHotkeys.Select(binding => $"{binding.Value}: {binding.Key}")) + " · 仅游戏前台");
    }

    internal static Dictionary<Keys, QuickShoutHotkeyAction> CreateGameHotkeyBindings(AssistantSettings config)
    {
        var candidates = new List<(Keys Key, QuickShoutHotkeyAction Action)>();
        if (config.QuickShoutHotkeysEnabled)
        {
            candidates.Add((ParseGameHotkey(config.QuickShoutBuiltInHotkey, Keys.F6), QuickShoutHotkeyAction.RandomBuiltIn));
            candidates.Add((ParseGameHotkey(config.QuickShoutCustomHotkey, Keys.F7), QuickShoutHotkeyAction.RandomCustom));
            candidates.Add((ParseGameHotkey(config.QuickShoutBatchHotkey, Keys.F8), QuickShoutHotkeyAction.SelectedBatch));
        }
        if (config.GameKdaHotkeyEnabled)
            candidates.Add((ParseGameHotkey(config.GameKdaHotkey, Keys.F9), QuickShoutHotkeyAction.GameKda));
        if (config.GameKdaPlayerHotkeysEnabled)
            for (int slot = 0; slot < 10; slot++)
                candidates.Add((slot == 9 ? Keys.NumPad0 : Keys.NumPad1 + slot, (QuickShoutHotkeyAction)(100 + slot)));
        // 禁用冲突键，防止旧配置误触另一个动作；其它有效快捷键继续可用。
        return candidates.GroupBy(candidate => candidate.Key)
            .Where(group => group.Count() == 1 && group.Key != ParseKey(config.HoldToTopHotkey))
            .ToDictionary(group => group.Key, group => group.Single().Action);
    }

    public static bool HasGameHotkeyConflict(AssistantSettings config) =>
        CreateGameHotkeyBindings(config).Count !=
        (config.QuickShoutHotkeysEnabled ? 3 : 0) + (config.GameKdaHotkeyEnabled ? 1 : 0) + (config.GameKdaPlayerHotkeysEnabled ? 10 : 0);

    private static Keys ParseGameHotkey(string? value, Keys fallback) =>
        TryParseGameHotkey(value, out Keys key) ? key : fallback;

    public static bool TryParseGameHotkey(string? value, out Keys key)
    {
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) key |= Keys.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) key |= Keys.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) key |= Keys.Shift;
            else
            {
                string token = part.Length == 1 && char.IsAsciiDigit(part[0]) ? $"D{part}" : part;
                if (!Enum.TryParse(token, true, out Keys parsed) || (key & Keys.KeyCode) != Keys.None ||
                    !IsGameHotkeyMainKey(parsed)) return false;
                key |= parsed;
            }
        }
        return IsGameHotkeyMainKey(key & Keys.KeyCode);
    }

    private static bool IsGameHotkeyMainKey(Keys key) => key is > Keys.None and <= (Keys)254 &&
        key is not (Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey or
                    Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin or
                    Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2);

    public static string FormatGameHotkey(Keys key)
    {
        string modifiers = ((key & Keys.Control) != 0 ? "Ctrl+" : "") +
            ((key & Keys.Alt) != 0 ? "Alt+" : "") + ((key & Keys.Shift) != 0 ? "Shift+" : "");
        Keys main = key & Keys.KeyCode;
        string name = main is >= Keys.D0 and <= Keys.D9 ? ((int)main - (int)Keys.D0).ToString() : main.ToString();
        return modifiers + name;
    }

    private static Keys CurrentGameModifiers() =>
        ((GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0 ? Keys.Control : Keys.None) |
        ((GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0 ? Keys.Alt : Keys.None) |
        ((GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0 ? Keys.Shift : Keys.None);

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
        // 忽略注入事件，避免发送文字时递归触发；录入快捷键期间不拦截。
        bool injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0;
        if (!_capturePaused && !injected)
        {
            if (keyDown && _pressedGameHotkeys.ContainsKey(virtualKey)) return (IntPtr)1;
            // 在主键按下时确定动作，松开 Ctrl / Alt / Shift 的先后顺序不影响触发。
            if (keyUp && _pressedGameHotkeys.Remove(virtualKey, out QuickShoutHotkeyAction pending))
            {
                if (IsLeagueGameForeground()) _ = DispatchGameHotkeyAsync(pending);
                return (IntPtr)1;
            }
            if (keyDown && _gameHotkeys.TryGetValue((Keys)virtualKey | CurrentGameModifiers(), out QuickShoutHotkeyAction action) &&
                IsLeagueGameForeground())
            {
                _pressedGameHotkeys[virtualKey] = action;
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private async Task DispatchGameHotkeyAsync(QuickShoutHotkeyAction action)
    {
        // 等待组合键松开，避免仍按住 Ctrl / Alt 时把聊天回车变成另一条游戏命令。
        for (int attempt = 0; attempt < 50 && CurrentGameModifiers() != Keys.None; attempt++)
        {
            if (_disposed || _capturePaused || !IsLeagueGameForeground()) return;
            await Task.Delay(40);
        }
        if (!_disposed && !_capturePaused && CurrentGameModifiers() == Keys.None && IsLeagueGameForeground())
            RunOnWindowThread(() => _shoutAction?.Invoke(action));
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
