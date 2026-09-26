using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Events;

namespace LOL_GameAssistant.Helper;

/// <summary>在对局窗口前台执行 ZuAnBot 使用的 WindowsInput 聊天按键序列。</summary>
public sealed class QuickMessageSenderController : IDisposable
{
    private DateTime _lastSentAtUtc = DateTime.MinValue;
    private int _sending;

    public QuickMessageSenderController(Form owner)
    { }

    public async Task<GameShoutSendResult> TestChatOpenAsync()
    {
        GameShoutSendResult? focusError = await FocusGameAsync();
        if (focusError.HasValue) return focusError.Value;

        try
        {
            await Simulate.Events().Click(KeyCode.Enter).Wait(100).Invoke();
            RuntimeDiagnostics.Report("游戏回车测试", "按键已注入", "WindowsInput 点击 Enter；请目视确认游戏聊天框是否打开");
            return new(true, "已通过 WindowsInput 注入一次回车；请确认聊天框是否打开。");
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Report("游戏回车测试", "输入失败", ex.Message);
            return new(false, $"回车测试失败：{ex.Message}");
        }
    }

    public async Task<GameShoutSendResult> SendSelectedToGameAsync(string message, bool sendToAll,
        bool useClipboard, bool perCharacter, int minimumIntervalSeconds)
    {
        string body = message.Trim();
        if (body.Length == 0 || body.Length > 500)
            return new(false, "短句为空或超过 500 字。");
        if (Volatile.Read(ref _sending) != 0)
            return new(false, "上一条喊话仍在发送，请稍候。");

        int interval = Math.Clamp(minimumIntervalSeconds, 2, 30);
        TimeSpan elapsed = DateTime.UtcNow - _lastSentAtUtc;
        if (elapsed.TotalSeconds < interval)
            return new(false, $"发送冷却中，还需等待 {Math.Max(1, interval - (int)Math.Floor(elapsed.TotalSeconds))} 秒。");

        GameShoutSendResult? focusError = await FocusGameAsync();
        if (focusError.HasValue) return focusError.Value;
        return await SendOnceAsync(body, sendToAll, useClipboard, perCharacter, interval);
    }

    /// <summary>按同一游戏内喊话序列依次发送多条短消息，用于双方 KDA 汇总。</summary>
    public async Task<GameShoutSendResult> SendBatchToGameAsync(IReadOnlyList<string> messages,
        bool sendToAll, bool useClipboard, int minimumIntervalSeconds, bool requireForeground = false)
    {
        if (messages.Count == 0 || messages.Count > 10 ||
            messages.Any(message => string.IsNullOrWhiteSpace(message) || message.Length > 500))
            return new(false, "游戏内 KDA 汇总内容为空、过长或消息数量过多。");
        if (Interlocked.Exchange(ref _sending, 1) != 0)
            return new(false, "另一条游戏内喊话正在发送。");

        try
        {
            int interval = Math.Clamp(minimumIntervalSeconds, 2, 30);
            TimeSpan elapsed = DateTime.UtcNow - _lastSentAtUtc;
            if (elapsed.TotalSeconds < interval)
                await Task.Delay(TimeSpan.FromSeconds(interval - elapsed.TotalSeconds));

            // 自动 KDA 只在用户已经切到游戏时发送，避免后台抢焦点和加载画面吞键。
            if (requireForeground && !IsLeagueGameForeground())
                return new(false, "等待游戏窗口进入前台。");
            GameShoutSendResult? focusError = requireForeground ? null : await FocusGameAsync();
            if (focusError.HasValue) return focusError.Value;
            for (int index = 0; index < messages.Count; index++)
            {
                if (!IsLeagueGameForeground())
                    return new(false, $"游戏失去前台焦点；已注入 {index}/{messages.Count} 条 KDA 汇总。", index);
                string text = sendToAll ? "/all " + messages[index] : messages[index];
                GameShoutSendResult result = await SendChatLineAsync(text, useClipboard);
                if (!result.Succeeded) return result with { SentCount = index };
                _lastSentAtUtc = DateTime.UtcNow;
                if (index < messages.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(interval));
            }
            RuntimeDiagnostics.Report("对局 KDA 评估", "按键已注入",
                $"已通过 WindowsInput 依次注入 {messages.Count} 条；游戏端没有送达回执");
            return new(true, $"已注入双方 KDA 汇总（{messages.Count} 条）；请在游戏聊天中确认。", messages.Count);
        }
        finally { Volatile.Write(ref _sending, 0); }
    }

    private async Task<GameShoutSendResult?> FocusGameAsync()
    {
        if (IsLeagueGameForeground()) return null;

        using Process? game = Process.GetProcessesByName("League of Legends")
            .FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);
        if (game == null)
            return new GameShoutSendResult(false, "未找到正在运行的英雄联盟对局。");

        SetForegroundWindow(game.MainWindowHandle);
        for (int attempt = 0; attempt < 8 && !IsLeagueGameForeground(); attempt++)
            await Task.Delay(200);
        if (!IsLeagueGameForeground())
            return new GameShoutSendResult(false, "游戏窗口未获得前台焦点；可在游戏内用快捷键喊话。");

        // 按钮切换到全屏游戏后，给游戏一段时间恢复输入焦点。
        await Task.Delay(900);
        return IsLeagueGameForeground()
            ? null
            : new GameShoutSendResult(false, "等待输入焦点时游戏窗口离开前台，未发送。");
    }

    private async Task<GameShoutSendResult> SendOnceAsync(string message, bool sendToAll,
        bool useClipboard, bool perCharacter, int minimumIntervalSeconds)
    {
        if (Interlocked.Exchange(ref _sending, 1) != 0)
            return new(false, "上一条喊话仍在发送，请稍候。");

        try
        {
            TimeSpan elapsed = DateTime.UtcNow - _lastSentAtUtc;
            if (elapsed.TotalSeconds < minimumIntervalSeconds)
                return new(false, "发送冷却中，请稍候再试。");
            if (!IsLeagueGameForeground())
                return new(false, "英雄联盟对局窗口不在前台，未发送。");

            List<string> messages = BuildMessages(message, perCharacter);
            if (messages.Count == 0)
                return new(false, "短句没有可发送的文字。");
            if (messages.Count > 40)
                return new(false, "逐字发送最多支持 40 字，请缩短短句。");

            for (int index = 0; index < messages.Count; index++)
            {
                if (!IsLeagueGameForeground())
                    return new(false, $"游戏失去前台焦点；已注入 {index} 条，后续未发送。");

                string text = sendToAll ? "/all " + messages[index] : messages[index];
                GameShoutSendResult result = await SendChatLineAsync(text, useClipboard);
                if (!result.Succeeded) return result;
                if (index == 0) _lastSentAtUtc = DateTime.UtcNow;
            }

            RuntimeDiagnostics.Report("快捷消息", "按键已注入",
                $"WindowsInput Enter → 文字 → Enter；{messages.Count} 条；游戏端没有送达回执");
            return new(true, $"已执行 {messages.Count} 次游戏聊天按键序列；请确认聊天内容是否出现。");
        }
        finally
        {
            Volatile.Write(ref _sending, 0);
        }
    }

    private static List<string> BuildMessages(string message, bool perCharacter)
    {
        if (!perCharacter) return [message];
        var messages = new List<string>();
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(message);
        while (enumerator.MoveNext())
        {
            string character = enumerator.GetTextElement();
            if (!string.IsNullOrWhiteSpace(character)) messages.Add(character);
        }
        return messages;
    }

    private static async Task<GameShoutSendResult> SendChatLineAsync(string text, bool useClipboard)
    {
        ClipboardSnapshot originalClipboard = default;
        bool clipboardChanged = false;
        try
        {
            if (useClipboard)
            {
                originalClipboard = CaptureClipboard();
                Clipboard.SetText(text);
                clipboardChanged = true;
                await Simulate.Events()
                    .Click(KeyCode.Enter).Wait(100)
                    .ClickChord(KeyCode.Control, KeyCode.V).Wait(100)
                    .Click(KeyCode.Enter).Wait(100)
                    .Invoke();
            }
            else
            {
                // 与 ZuAnBot 的游戏内发送序列保持一致。
                await Simulate.Events()
                    .Click(KeyCode.Enter).Wait(100)
                    .Click(text).Wait(100)
                    .Click(KeyCode.Enter).Wait(100)
                    .Invoke();
            }
            return new(true, "聊天按键序列已执行。");
        }
        catch (Exception ex)
        {
            Program.GameMain.infoMsg.AddMsg($"游戏内喊话失败：{ex.Message}");
            RuntimeDiagnostics.Report("快捷消息", "输入失败", ex.Message);
            return new(false, $"游戏内喊话失败：{ex.Message}");
        }
        finally
        {
            if (clipboardChanged) RestoreClipboard(originalClipboard, text);
        }
    }

    private static ClipboardSnapshot CaptureClipboard()
    {
        try
        {
            return Clipboard.ContainsText()
                ? new ClipboardSnapshot(true, Clipboard.GetText())
                : new ClipboardSnapshot(false, null);
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
            if (!Clipboard.ContainsText() || !string.Equals(Clipboard.GetText(), sentText, StringComparison.Ordinal)) return;
            if (snapshot.HasText && snapshot.Text != null) Clipboard.SetText(snapshot.Text);
            else Clipboard.Clear();
        }
        catch (ExternalException)
        {
            RuntimeDiagnostics.Report("快捷消息", "按键已注入", "无法恢复原剪贴板内容");
        }
    }

    private static bool IsLeagueGameForeground()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return false;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void Dispose()
    { }

    private readonly record struct ClipboardSnapshot(bool HasText, string? Text);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public readonly record struct GameShoutSendResult(bool Succeeded, string Message, int SentCount = 0);