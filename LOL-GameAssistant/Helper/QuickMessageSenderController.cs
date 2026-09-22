using LOL_GameAssistant.Domain.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
    private string _language = "中文";
    private int _minimumIntervalSeconds = 3;
    private DateTime _lastSentAtUtc = DateTime.MinValue;
    private int _sending;
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
        _language = config.QuickMessageLanguage;
        _minimumIntervalSeconds = Math.Clamp(config.QuickMessageSendIntervalSeconds, 2, 30);
        if (!config.QuickMessageAutoSendEnabled || string.IsNullOrWhiteSpace(_message))
        {
            RuntimeDiagnostics.Report("快捷消息", "已关闭", "未启用或消息内容为空");
            return;
        }

        Keys key = WindowHoldController.ParseKey(config.QuickMessageHotkey);
        if (!RegisterHotKey(Handle, HotkeyId, 0, (uint)key))
        {
            GameMain.infoMsg.AddMsg("快捷弹幕键注册失败，可能已被系统或其它程序占用。");
            RuntimeDiagnostics.Report("快捷消息", "不可用", "快捷键被系统或其它程序占用");
            return;
        }
        _registeredKey = key;
        RuntimeDiagnostics.Report("快捷消息", "已注册", $"{WindowHoldController.DescribeKey(key)} · {_language} · 间隔 {_minimumIntervalSeconds} 秒");
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
        if (string.IsNullOrWhiteSpace(_message) || Interlocked.Exchange(ref _sending, 1) != 0) return;

        try
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan elapsed = now - _lastSentAtUtc;
            if (elapsed.TotalSeconds < _minimumIntervalSeconds)
            {
                int remaining = Math.Max(1, _minimumIntervalSeconds - (int)Math.Floor(elapsed.TotalSeconds));
                GameMain.infoMsg.AddMsg($"快捷弹幕冷却中，请在 {remaining} 秒后再试。");
                RuntimeDiagnostics.Report("快捷消息", "冷却中", $"还需等待 {remaining} 秒");
                return;
            }

            if (!IsLeagueGameForeground())
            {
                GameMain.infoMsg.AddMsg("未发送快捷弹幕：请先将英雄联盟对局窗口切到前台。");
                RuntimeDiagnostics.Report("快捷消息", "未发送", "英雄联盟对局窗口不在前台");
                return;
            }

            InputResult openResult = SendVirtualKey((ushort)Keys.Enter);
            if (!openResult.Succeeded)
            {
                ReportInputBlocked("打开聊天框", openResult.ErrorCode);
                return;
            }

            ClipboardSnapshot clipboard = CaptureClipboard();
            try
            {
                // 游戏对 Unicode SendInput 的支持因输入法/渲染后端而异，中文尤其常被吞。
                // Paste retains CJK and special characters while still requiring an explicit user hotkey.
                await Task.Delay(ChatOpenDelayMilliseconds).ConfigureAwait(true);
                InputResult pasteResult = PasteText(_message);
                if (!pasteResult.Succeeded)
                {
                    ReportInputBlocked("粘贴消息", pasteResult.ErrorCode);
                    return;
                }

                await Task.Delay(PasteSettleDelayMilliseconds).ConfigureAwait(true);
                InputResult sendResult = SendVirtualKey((ushort)Keys.Enter);
                if (!sendResult.Succeeded)
                {
                    ReportInputBlocked("发送消息", sendResult.ErrorCode);
                    return;
                }
            }
            finally
            {
                RestoreClipboard(clipboard, _message);
            }

            _lastSentAtUtc = now;
            GameMain.infoMsg.AddMsg("快捷弹幕按键已提交到游戏。若聊天框没有响应，请检查管理员权限或全屏输入限制。");
            RuntimeDiagnostics.Report("快捷消息", "已提交", "已提交打开聊天、粘贴和发送按键；游戏端不会提供可验证的送达回执");
        }
        finally
        {
            Volatile.Write(ref _sending, 0);
        }
    }

    private static void ReportInputBlocked(string action, int errorCode)
    {
        GameMain.infoMsg.AddMsg(
            $"快捷弹幕未发送：无法{action}（Windows 错误 {errorCode}）。若 LOL 以管理员身份运行，请也以管理员身份启动助手。");
        RuntimeDiagnostics.Report("快捷消息", "输入被拒绝", $"{action} 失败，Windows 错误 {errorCode}");
    }

    private static InputResult PasteText(string text)
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
            return SendInputs(inputs);
        }
        catch (ExternalException)
        {
            return new InputResult(false, -1);
        }
        catch (ThreadStateException)
        {
            return new InputResult(false, -1);
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

    private static InputResult SendVirtualKey(ushort key)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(key, 0, 0),
            CreateKeyboardInput(key, 0, KeyEventFKeyUp)
        };
        return SendInputs(inputs);
    }

    private static InputResult SendInputs(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return new InputResult(sent == inputs.Length, sent == inputs.Length ? 0 : Marshal.GetLastWin32Error());
    }

    private static ClipboardSnapshot CaptureClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? new ClipboardSnapshot(true, Clipboard.GetText()) : new ClipboardSnapshot(false, null);
        }
        catch (ExternalException)
        {
            return new ClipboardSnapshot(false, null);
        }
    }

    private static void RestoreClipboard(ClipboardSnapshot snapshot, string sentText)
    {
        try
        {
            // Do not overwrite another application's clipboard update that happened while the chat was opened.
            if (!Clipboard.ContainsText() || !string.Equals(Clipboard.GetText(), sentText, StringComparison.Ordinal)) return;
            if (snapshot.HasText && snapshot.Text != null) Clipboard.SetText(snapshot.Text);
            else Clipboard.Clear();
        }
        catch (ExternalException)
        {
            RuntimeDiagnostics.Report("快捷消息", "已提交", "消息已提交，但无法恢复原剪贴板内容");
        }
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

    private readonly record struct InputResult(bool Succeeded, int ErrorCode);
    private readonly record struct ClipboardSnapshot(bool HasText, string? Text);

    [DllImport("user32.dll", SetLastError = true)]
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