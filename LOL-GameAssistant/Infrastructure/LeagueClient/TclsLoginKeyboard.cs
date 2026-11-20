using System.Runtime.InteropServices;
using FlaUI.Core.WindowsAPI;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Focused, paced keyboard input. Never uses the clipboard, SetValue or window text messages.</summary>
internal static class TclsLoginKeyboard
{
    internal readonly record struct Stroke(ushort ScanCode, bool Shift);

    internal static bool HasFocusedEditor(IntPtr window, Point point, bool allowWindowFocus = false)
    {
        uint thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (GetAncestor(GetForegroundWindow(), 2) != window || !GetGUIThreadInfo(thread, ref info)) return false;
        if (info.Focus == window || info.Focus == IntPtr.Zero)
            return allowWindowFocus && info.Active == window;
        return GetAncestor(info.Focus, 2) == window && GetWindowRect(info.Focus, out var bounds) &&
            point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
    }

    internal static Stroke[]? Plan(string value, IntPtr layout, bool capsLock)
    {
        var strokes = new List<Stroke>();
        foreach (char character in value)
        {
            short mapped = VkKeyScanEx(character, layout);
            if (char.IsControl(character) || mapped == -1 || (mapped >> 8 & ~1) != 0) return null;
            ushort key = (ushort)(mapped & 0xff);
            uint scan = MapVirtualKeyEx(key, 0, layout);
            if (scan == 0 || scan > 0xff) return null;
            bool shift = (mapped & 0x100) != 0;
            if (capsLock && character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') shift = !shift;
            strokes.Add(new((ushort)scan, shift));
        }
        return strokes.ToArray();
    }

    internal static bool Replace(IntPtr window, string value, Action guard, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(window, out _));
        var strokes = Plan(value, layout, (GetKeyState(0x14) & 1) != 0);
        uint selectAll = MapVirtualKeyEx(0x41, 0, layout);
        if (strokes == null || strokes.Length == 0 || selectAll == 0 || selectAll > 0xff) return false;
        // Don't mix the user's held modifier with credential input.
        if (new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0)) return false;
        void Wait(int milliseconds)
        {
            if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested();
            guard();
        }
        void Send(ushort scan, bool up)
        {
            var input = INPUT.KeyboardInput(new KEYBDINPUT
            {
                wScan = scan,
                dwFlags = KeyEventFlags.KEYEVENTF_SCANCODE | (up ? KeyEventFlags.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = IntPtr.Zero
            });
            if (User32.SendInput(1, [input], INPUT.Size) != 1)
                throw new InvalidOperationException("键盘输入未被系统接收。");
        }
        void Tap(ushort scan)
        {
            token.ThrowIfCancellationRequested();guard();
            Send(scan, false);
            try { Wait(60); }
            finally { Send(scan, true); }
            Wait(80);
        }
        void Modified(ushort scan, Action action)
        {
            guard();Send(scan, false);
            try { Wait(60);action(); }
            finally { Send(scan, true); }
            Wait(80);
        }
        Modified(0x1d, () => Tap((ushort)selectAll)); // Ctrl+A
        Tap(0x0e); // Backspace
        foreach (var stroke in strokes)
        {
            if (stroke.Shift) Modified(0x2a, () => Tap(stroke.ScanCode));
            else Tap(stroke.ScanCode);
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretBounds;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, IntPtr layout);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
}
