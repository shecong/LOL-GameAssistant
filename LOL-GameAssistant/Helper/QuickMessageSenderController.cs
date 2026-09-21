using System.Diagnostics;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// 处理用户主动触发的局内快捷弹幕。
/// 仅在 LOL 对局窗口位于前台时发送一次，不提供后台循环、定时或批量刷屏能力。
/// </summary>
public sealed class QuickMessageSenderController : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C4F4C;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const int ChatOpenDelayMilliseconds = 130;
    private const int PasteSettleDelayMilliseconds = 90;

    private readonly Form _owner;
    private Keys _registeredKey = Keys.None;
    private string _message = "";
    private int _minimumIntervalSeconds = 3;
    private DateTime _lastSentAtUtc = DateTime.MinValue;
    private bool _disposed;

    public QuickMessageSenderController(Form owner)
    {
        _owner = owner;
        AssignHandle(owner.Handle);
    }

    /// <summary>应用保存后的配置，并重新注册当前快捷键。</summary>
    public void Apply(AssistantSettings config)
    {
        Unregister();
        _message = config.QuickMessageText?.Trim() ?? "";
        _minimumIntervalSeconds = Math.Clamp(config.QuickMessageSendIntervalSeconds, 2, 30);
        if (!config.QuickMessageAutoSendEnabled || string.IsNullOrWhiteSpace(_message)) return;

        Keys key = WindowHoldController.ParseKey(config.QuickMessageHotkey);
        if (!RegisterHotKey(Handle, HotkeyId, 0, (uint)key))
        {
            GameMain.infoMsg.AddMsg("快捷弹幕键注册失败，可能已被系统或其它程序占用。");
            return;
        }
        _registeredKey = key;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == (IntPtr)HotkeyId)
        {
            _ = SendOnceAsync();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// 由用户热键触发一次发送。通过前台窗口验证和最小间隔避免向错误程序或连续误触发送内容。
    /// </summary>
    private async Task SendOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(_message)) return;

        DateTime now = DateTime.UtcNow;
        TimeSpan elapsed = now - _lastSentAtUtc;
        if (elapsed.TotalSeconds < _minimumIntervalSeconds)
        {
            int remaining = Math.Max(1, _minimumIntervalSeconds - (int)Math.Floor(elapsed.TotalSeconds));
            GameMain.infoMsg.AddMsg($"快捷弹幕冷却中，请在 {remaining} 秒后再试。");
            return;
        }

        if (!IsLeagueGameForeground())
        {
            GameMain.infoMsg.AddMsg("未发送快捷弹幕：请先将英雄联盟对局窗口切到前台。");
            return;
        }

        // 游戏对 Unicode SendInput 的支持因输入法/渲染后端而异，中文尤其常被吞。
        // 使用剪贴板粘贴可保持中文、英文和特殊字符完整，仍只在用户主动按下热键时发送一次。
        if (!TrySendVirtualKey((ushort)Keys.Enter))
        {
            ReportInputBlocked("打开聊天框");
            return;
        }

        // LOL 的聊天输入框会在下一帧才接收文字。原来的 70ms 在低帧率或全屏切换时
        // 容易让 Ctrl+V 落在聊天框尚未打开的时刻。
        await Task.Delay(ChatOpenDelayMilliseconds).ConfigureAwait(true);
        if (!TryPasteText(_message))
        {
            ReportInputBlocked("粘贴消息");
            return;
        }
        await Task.Delay(PasteSettleDelayMilliseconds).ConfigureAwait(true);
        if (!TrySendVirtualKey((ushort)Keys.Enter))
        {
            ReportInputBlocked("发送消息");
            return;
        }

        _lastSentAtUtc = now;
        GameMain.infoMsg.AddMsg("快捷弹幕已发送。");
    }

    private static void ReportInputBlocked(string action)
    {
        GameMain.infoMsg.AddMsg(
            $"快捷弹幕未发送：无法{action}。若 LOL 以管理员身份运行，请也以管理员身份启动助手。");
    }

    private static bool TryPasteText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            var inputs = new[]
            {
                CreateKeyboardInput((ushort)Keys.ControlKey, 0, 0),
                CreateKeyboardInput((ushort)Keys.V, 0, 0),
                CreateKeyboardInput((ushort)Keys.V, 0, KeyEventFKeyUp),
                CreateKeyboardInput((ushort)Keys.ControlKey, 0, KeyEventFKeyUp)
            };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }

    /// <summary>只允许向实际对局进程写入按键，避免热键在其它应用前台时误发送。</summary>
    private static bool IsLeagueGameForeground()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return false;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "League of Legends", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySendVirtualKey(ushort key)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(key, 0, 0),
            CreateKeyboardInput(key, 0, KeyEventFKeyUp)
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char character in text)
        {
            inputs.Add(CreateKeyboardInput(0, character, KeyEventFUnicode));
            inputs.Add(CreateKeyboardInput(0, character, KeyEventFUnicode | KeyEventFKeyUp));
        }

        if (inputs.Count > 0)
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                WVk = virtualKey,
                WScan = scanCode,
                DwFlags = flags
            }
        }
    };

    private void Unregister()
    {
        if (_registeredKey == Keys.None) return;
        UnregisterHotKey(Handle, HotkeyId);
        _registeredKey = Keys.None;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        ReleaseHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    // Win32 的 INPUT 联合体在 x64 下由最大的 MOUSEINPUT 决定，长度固定是 32 字节。
    // 即使本程序只使用 KEYBDINPUT，也必须保留该大小；否则 Marshal.SizeOf<INPUT>() 会给出
    // 32 而不是原生 API 要求的 40，SendInput 会静默失败，游戏完全收不到按键。
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKeyCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
