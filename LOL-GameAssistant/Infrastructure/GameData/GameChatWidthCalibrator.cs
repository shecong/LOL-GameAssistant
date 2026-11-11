using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>发送随机标尺后自动截取前台游戏聊天，OCR 结果经后端校验后才写入设置。</summary>
public sealed class GameChatWidthCalibrator(IApplicationSettingsStore settingsStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<GameShoutSendResult> CalibrateAsync(Func<string, Func<Task<GameShoutSendResult>>, Task<GameShoutSendResult>> send)
    {
        if (!await _gate.WaitAsync(0)) return new(false, "行宽校准正在进行，请稍候。");
        try
        {
            string probe = GameChatWidthCalibration.CreateProbe();
            return await send(probe, async () =>
            {
                await Task.Delay(400);
                var widths = await Task.Run(() =>
                {
                    using var engine = AugmentOcrEngine.Create();
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        using var screenshot = CaptureGame();
                        if (screenshot == null) return null;
                        var result = AugmentImageRecognizer.ReadImage(screenshot, engine);
                        var lines = result.Lines.Where(line => line.RecognitionScore >= .85f)
                            .OrderBy(line => Math.Min(Math.Min(line.Box.Y1, line.Box.Y2), Math.Min(line.Box.Y3, line.Box.Y4)))
                            .Select(line => line.Text).ToArray();
                        var measured = GameChatWidthCalibration.Measure(probe, lines);
                        if (measured != null) return measured;
                    }
                    return null;
                });
                if (widths == null)
                    return new(false, "未可靠识别标尺换行，原校准保持不变。请保持游戏前台、聊天文字完整可见后重试。", 1);
                var settings = settingsStore.Load();
                settings.GameChatFirstLineWidth = widths.FirstLine;
                settings.GameChatContinuationLineWidth = widths.ContinuationLine;
                settingsStore.Save(settings);
                string message = $"自动校准完成：首行 {widths.FirstLine / 2} 个全角字，续行 {widths.ContinuationLine / 2} 个全角字；已保存并应用于 KDA 排版。";
                RuntimeDiagnostics.Report("聊天行宽校准", "已校准", message);
                return new(true, message, 1);
            });
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Report("聊天行宽校准", "失败", ex.Message);
            return new(false, "自动校准失败，原校准保持不变：" + ex.Message);
        }
        finally { _gate.Release(); }
    }

    private static Bitmap? CaptureGame()
    {
        IntPtr window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint pid);
        if (pid == 0) return null;
        using var process = Process.GetProcessById((int)pid);
        if (process.ProcessName != "League of Legends" || !GetClientRect(window, out var rect)) return null;
        var origin = Point.Empty;
        if (!ClientToScreen(window, ref origin) || rect.Right < 600 || rect.Bottom < 400) return null;
        // Capture the client area only; allow chat to be moved or resized in game.
        var bitmap = new Bitmap(rect.Right, rect.Bottom, PixelFormat.Format24bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(origin, Point.Empty, bitmap.Size);
            if (GetForegroundWindow() != window) { bitmap.Dispose(); return null; }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
}
