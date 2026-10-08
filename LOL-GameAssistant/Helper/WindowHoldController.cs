using LOL_GameAssistant.Domain.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

public enum QuickShoutHotkeyAction { RandomBuiltIn, RandomCustom, SelectedBatch, GameKda, Player1 = 100, Player2, Player3, Player4, Player5, Player6, Player7, Player8, Player9, Player10 }

/// <summary>
/// 置顶键使用 Windows 热键；局内面板同时检测按键状态，避免游戏拦截热键消息后无响应。
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
    private readonly System.Windows.Forms.Timer _hotkeyWatch = new() { Interval = 50 };
    private IntPtr _hook;
    private IntPtr _registeredHandle;
    private Keys _hotkey = Keys.Oem3;
    private Keys _overlayKey = Keys.Oem3;
    private Keys _registeredKey;
    private Keys _holdingKey;
    private bool _overlayEnabled;
    private bool _registeredForOverlay;
    private bool _holdingOverlay;
    private Func<bool>? _canShowOverlay;
    private Action<Rectangle>? _showOverlay;
    private Action? _hideOverlay;
    private Dictionary<Keys, QuickShoutHotkeyAction> _gameHotkeys = new();
    private Action<QuickShoutHotkeyAction>? _shoutAction;
    private readonly Dictionary<int, QuickShoutHotkeyAction> _pressedGameHotkeys = new();
    private bool _onlyWhenLeagueFocused = true;
    private bool _capturePaused;
    private bool _holding;
    private bool _disposed;
    private long _nextRegisterAttempt;

    /// <summary>初始化 WindowHoldController 的实例状态，并保存传入的依赖或数据。</summary>
    public WindowHoldController(Form window)
    {
        _window = window;
        _callback = HookCallback;
        _window.HandleCreated += WindowHandleCreated;
        _window.HandleDestroyed += WindowHandleDestroyed;
        _hotkeyWatch.Tick += (_, _) =>
        {
            ObserveHotkeyState(ShouldUseOverlay(), IsLeagueForeground(), IsCaptureActive,
                CurrentGameModifiers(), vk => (GetAsyncKeyState(vk) & 0x8000) != 0);
            RefreshRegistration();
        };
        _hotkeyWatch.Start();
    }

    /// <summary>应用置顶热键、前台限制和透明度设置，并重新注册有效热键。</summary>
    public void Apply(AssistantSettings config)
    {
        Keys newKey = ParseKey(config.HoldToTopHotkey);
        if (_holding) EndHold();
        if (_hotkey != newKey) UnregisterHoldHotkey();
        _hotkey = newKey;
        _overlayKey = ParseGameHotkey(config.BattleOverlayHotkey, Keys.Oem3);
        _overlayEnabled = config.BattleOverlayEnabled && !HasBattleOverlayHotkeyConflict(config);
        RuntimeDiagnostics.Report("对局信息快捷键", _overlayEnabled ? "已启用" : "未启用",
            _overlayEnabled ? $"{FormatGameHotkey(_overlayKey)} · 按住显示，松开隐藏 · 热键消息与按键状态双重检测" :
                config.BattleOverlayEnabled ? "快捷键配置无效或与喊话 / KDA 冲突" : "已在设置中关闭");
        _onlyWhenLeagueFocused = config.HoldToTopOnlyWhenLeagueFocused;
        _window.Opacity = Math.Clamp(config.WindowOpacityPercent, 40, 100) / 100D;
        _nextRegisterAttempt = 0;
        RefreshRegistration();
    }

    public void ConfigureBattleOverlay(Func<bool> canShow, Action<Rectangle> show, Action hide)
    {
        _canShowOverlay = canShow;
        _showOverlay = show;
        _hideOverlay = hide;
    }

    private bool ShouldUseOverlay() => _overlayEnabled && _canShowOverlay?.Invoke() == true && IsLeagueGameForeground();

    // Focus stays on a shortcut editor when the user Alt-Tabs away from the assistant.
    // Suspend input only while that editor's window is actually in the foreground.
    private bool IsCaptureActive => _capturePaused && _window.IsHandleCreated && GetForegroundWindow() == _window.Handle;

    internal void ObserveHotkeyState(bool overlayAvailable, bool leagueFocused, bool captureActive,
        Keys modifiers, Func<int, bool> isDown)
    {
        if (_disposed) return;
        if (_holding && (captureActive || !IsHotkeyHeld(_holdingKey, isDown) ||
            (_holdingOverlay ? !overlayAvailable : _onlyWhenLeagueFocused && !leagueFocused)))
            EndHold();
        if (_holding || captureActive || !_overlayEnabled || !overlayAvailable ||
            modifiers != (_overlayKey & Keys.Modifiers) || !IsHotkeyHeld(_overlayKey, isDown)) return;

        _holdingOverlay = true;
        _holdingKey = _overlayKey;
        _holding = true;
        BeginHold();
    }

    internal static bool IsHotkeyHeld(Keys key, Func<int, bool> isDown) =>
        isDown((int)(key & Keys.KeyCode)) &&
        ((key & Keys.Control) == 0 || isDown((int)Keys.ControlKey)) &&
        ((key & Keys.Alt) == 0 || isDown((int)Keys.Menu)) &&
        ((key & Keys.Shift) == 0 || isDown((int)Keys.ShiftKey));

    private static bool IsHotkeyHeld(Keys key) => IsHotkeyHeld(key, vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>设置页录入快捷键期间暂停注册，避免旧快捷键吃掉输入。</summary>
    public void SetCapturePaused(bool paused)
    {
        _capturePaused = paused;
        if (paused && _holding) EndHold();
        _pressedGameHotkeys.Clear();
        if (paused) UnregisterHoldHotkey();
        else RefreshRegistration();
    }

    /// <summary>注册快捷喊话热键，并将热键动作分派到对应的发送流程。</summary>
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

    /// <summary>根据配置建立游戏热键与动作的映射。</summary>
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
            .Where(group => group.Count() == 1 && group.Key != ParseKey(config.HoldToTopHotkey) &&
                (!config.BattleOverlayEnabled || group.Key != ParseGameHotkey(config.BattleOverlayHotkey, Keys.Oem3)))
            .ToDictionary(group => group.Key, group => group.Single().Action);
    }

    /// <summary>检查当前热键是否与其他游戏热键冲突。</summary>
    public static bool HasGameHotkeyConflict(AssistantSettings config) =>
        CreateGameHotkeyBindings(config).Count !=
        (config.QuickShoutHotkeysEnabled ? 3 : 0) + (config.GameKdaHotkeyEnabled ? 1 : 0) + (config.GameKdaPlayerHotkeysEnabled ? 10 : 0);

    public static bool HasBattleOverlayHotkeyConflict(AssistantSettings config)
    {
        if (!config.BattleOverlayEnabled) return false;
        if (!TryParseGameHotkey(config.BattleOverlayHotkey, out Keys key)) return true;
        return (config.QuickShoutHotkeysEnabled && new[]
            {
                ParseGameHotkey(config.QuickShoutBuiltInHotkey, Keys.F6),
                ParseGameHotkey(config.QuickShoutCustomHotkey, Keys.F7),
                ParseGameHotkey(config.QuickShoutBatchHotkey, Keys.F8)
            }.Contains(key)) ||
            (config.GameKdaHotkeyEnabled && key == ParseGameHotkey(config.GameKdaHotkey, Keys.F9)) ||
            (config.GameKdaPlayerHotkeysEnabled && key is >= Keys.NumPad0 and <= Keys.NumPad9);
    }

    /// <summary>解析游戏热键配置并返回按键组合。</summary>
    private static Keys ParseGameHotkey(string? value, Keys fallback) =>
        TryParseGameHotkey(value, out Keys key) ? key : fallback;

    /// <summary>尝试解析游戏热键配置，格式无效时返回失败。</summary>
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
                string token = part is "·" or "`" or "~" ? "Oem3" :
                    part.Length == 1 && char.IsAsciiDigit(part[0]) ? $"D{part}" : part;
                if (!Enum.TryParse(token, true, out Keys parsed) || (key & Keys.KeyCode) != Keys.None ||
                    !IsGameHotkeyMainKey(parsed)) return false;
                key |= parsed;
            }
        }
        return IsGameHotkeyMainKey(key & Keys.KeyCode);
    }

    /// <summary>判断按键是否可以作为游戏热键的主键。</summary>
    private static bool IsGameHotkeyMainKey(Keys key) => key is > Keys.None and <= (Keys)254 &&
        key is not (Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey or
                    Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin or
                    Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2);

    /// <summary>将按键组合格式化为可保存和显示的热键文本。</summary>
    public static string FormatGameHotkey(Keys key)
    {
        string modifiers = ((key & Keys.Control) != 0 ? "Ctrl+" : "") +
            ((key & Keys.Alt) != 0 ? "Alt+" : "") + ((key & Keys.Shift) != 0 ? "Shift+" : "");
        Keys main = key & Keys.KeyCode;
        string name = main is >= Keys.D0 and <= Keys.D9 ? ((int)main - (int)Keys.D0).ToString() : main.ToString();
        return modifiers + name;
    }

    /// <summary>读取当前按下的热键修饰键。</summary>
    private static Keys CurrentGameModifiers() =>
        ((GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0 ? Keys.Control : Keys.None) |
        ((GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0 ? Keys.Alt : Keys.None) |
        ((GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0 ? Keys.Shift : Keys.None);

    /// <summary>将配置文本转换为按键值。</summary>
    public static Keys ParseKey(string? value) =>
        Enum.TryParse(value, true, out Keys parsed) && parsed != Keys.None
            ? parsed : Keys.Oem3;

    /// <summary>将按键值转换为可读名称。</summary>
    public static string DescribeKey(Keys key) => key == Keys.Oem3 ? "·" : key.ToString();

    /// <summary>窗口新句柄创建后恢复热键注册。</summary>
    private void WindowHandleCreated(object? sender, EventArgs e) => RefreshRegistration();

    /// <summary>窗口句柄销毁前清理其热键注册。</summary>
    private void WindowHandleDestroyed(object? sender, EventArgs e) => UnregisterHoldHotkey();

    /// <summary>根据最新设置更新已注册的窗口热键。</summary>
    private void RefreshRegistration()
    {
        if (_disposed || !_window.IsHandleCreated) return;
        bool overlay = ShouldUseOverlay();
        Keys key = overlay ? _overlayKey : _hotkey;
        bool shouldRegister = !IsCaptureActive &&
            (!_onlyWhenLeagueFocused || IsLeagueForeground());
        if (!shouldRegister)
        {
            if (_registeredHandle != IntPtr.Zero)
                RuntimeDiagnostics.Report("按住置顶键", "待机", "等待 LOL 窗口位于前台");
            UnregisterHoldHotkey();
            return;
        }
        if (_registeredHandle == _window.Handle && _registeredKey == key && _registeredForOverlay == overlay) return;
        if (_holding && !_holdingOverlay) EndHold();
        if (Environment.TickCount64 < _nextRegisterAttempt) return;
        UnregisterHoldHotkey();
        uint modifiers = ModNoRepeat | ((key & Keys.Control) != 0 ? 2u : 0) |
            ((key & Keys.Alt) != 0 ? 1u : 0) | ((key & Keys.Shift) != 0 ? 4u : 0);
        if (RegisterHotKey(_window.Handle, HoldHotkeyId, modifiers, (uint)(key & Keys.KeyCode)))
        {
            _registeredHandle = _window.Handle;
            _registeredKey = key;
            _registeredForOverlay = overlay;
            RuntimeDiagnostics.Report("按住置顶键", "已注册",
                $"{FormatGameHotkey(key)} · {(overlay ? "对局信息面板" : _onlyWhenLeagueFocused ? "仅 LOL 前台" : "所有窗口")}");
        }
        else
        {
            _nextRegisterAttempt = Environment.TickCount64 + 3000;
            RuntimeDiagnostics.Report("按住置顶键", "不可用",
                $"Windows 注册热键失败（{Marshal.GetLastWin32Error()}），请更换按键或关闭占用该键的程序");
        }
    }

    /// <summary>移除按住置顶功能对应的热键。</summary>
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
        if (_disposed || IsCaptureActive || _registeredHandle == IntPtr.Zero || _holding)
            return true;
        if (_onlyWhenLeagueFocused && !IsLeagueForeground()) return true;
        if (_registeredForOverlay && !ShouldUseOverlay()) return true;
        // A queued WM_HOTKEY must not reopen the window after a quick tap has already ended.
        if (!IsHotkeyHeld(_registeredKey)) return true;
        _holdingOverlay = _registeredForOverlay;
        _holdingKey = _registeredKey;
        _holding = true;
        BeginHold();
        return true;
    }

    /// <summary>开始按住置顶状态并记录窗口原有状态。</summary>
    private void BeginHold()
    {
        if (_window.IsDisposed) return;
        if (_holdingOverlay)
        {
            IntPtr foreground = GetForegroundWindow();
            Rectangle bounds = Screen.FromHandle(foreground).Bounds;
            if (GetWindowRect(foreground, out NativeRect rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            _showOverlay?.Invoke(bounds);
            RuntimeDiagnostics.Report("对局信息面板", "显示中", $"按住 {FormatGameHotkey(_holdingKey)} 查看双方信息");
            return;
        }
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

    /// <summary>结束按住置顶状态并恢复窗口原有状态。</summary>
    private void EndHold()
    {
        _holding = false;
        if (_holdingOverlay)
        {
            _holdingOverlay = false;
            _hideOverlay?.Invoke();
            RuntimeDiagnostics.Report("对局信息面板", "已隐藏", "快捷键已松开、切出游戏或正在录入快捷键");
            return;
        }
        if (_window.IsDisposed) return;
        SetWindowPos(_window.Handle, HwndNotopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        _window.Hide();
        RuntimeDiagnostics.Report("按住置顶键", "已隐藏",
            "已松开快捷键，可从托盘恢复窗口");
    }

    /// <summary>安装用于识别游戏喊话热键的键盘钩子。</summary>
    private void InstallShoutHook()
    {
        if (_hook != IntPtr.Zero || _disposed) return;
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
    }

    /// <summary>移除游戏喊话键盘钩子。</summary>
    private void RemoveShoutHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>过滤键盘钩子消息并分派有效的游戏热键。</summary>
    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _disposed)
            return CallNextHookEx(_hook, code, wParam, lParam);
        int virtualKey = Marshal.ReadInt32(lParam);
        bool keyUp = wParam == (IntPtr)WmKeyUp || wParam == (IntPtr)WmSysKeyUp;
        bool keyDown = wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown;
        // 忽略注入事件，避免发送文字时递归触发；录入快捷键期间不拦截。
        bool injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0;
        if (!IsCaptureActive && !injected)
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

    /// <summary>将识别到的热键动作交给对应的异步操作。</summary>
    private async Task DispatchGameHotkeyAsync(QuickShoutHotkeyAction action)
    {
        // 等待组合键松开，避免仍按住 Ctrl / Alt 时把聊天回车变成另一条游戏命令。
        for (int attempt = 0; attempt < 50 && CurrentGameModifiers() != Keys.None; attempt++)
        {
            if (_disposed || IsCaptureActive || !IsLeagueGameForeground()) return;
            await Task.Delay(40);
        }
        if (!_disposed && !IsCaptureActive && CurrentGameModifiers() == Keys.None && IsLeagueGameForeground())
            RunOnWindowThread(() => _shoutAction?.Invoke(action));
    }

    /// <summary>将窗口操作调度到创建该窗口的 UI 线程。</summary>
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

    /// <summary>判断前台窗口是否属于英雄联盟相关进程。</summary>
    private static bool IsLeagueForeground() => IsForegroundProcess(
        "League of Legends", "LeagueClient", "LeagueClientUx");

    /// <summary>判断当前前台进程是否为游戏进程。</summary>
    private static bool IsLeagueGameForeground() => IsForegroundProcess("League of Legends");

    /// <summary>根据进程名称判断当前前台进程。</summary>
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

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_holding) EndHold();
        _hotkeyWatch.Stop();
        _hotkeyWatch.Dispose();
        _window.HandleCreated -= WindowHandleCreated;
        _window.HandleDestroyed -= WindowHandleDestroyed;
        UnregisterHoldHotkey();
        RemoveShoutHook();
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    /// <summary>为窗口注册系统级热键。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>注销此前为窗口注册的系统热键。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>安装原生输入钩子并返回其句柄。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    /// <summary>移除此前安装的输入钩子。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    /// <summary>将当前输入消息传递给后续钩子。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    /// <summary>读取指定模块在当前进程中的句柄。</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    /// <summary>读取当前前台窗口的句柄。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>读取窗口所属线程和进程标识。</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    /// <summary>读取按键的当前异步状态。</summary>
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    /// <summary>调用 Windows API 更新窗口显示状态。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>调整窗口位置、层级或触发非客户区重绘。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
}
