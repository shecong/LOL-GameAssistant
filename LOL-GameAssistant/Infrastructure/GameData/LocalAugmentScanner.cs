using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Helper;
using Sdcb.SimdPaddleOCR;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>只截取本机游戏窗口中央的增幅卡片区域，并在本机使用托管 PaddleOCR。</summary>
public sealed class LocalAugmentScanner : IAugmentScanner
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    private static PaddleOcrAll? _engine;

    static LocalAugmentScanner()
    {
        // Wait for any in-flight scan before releasing model sessions at shutdown.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            ScanGate.Wait();
            try { _engine?.Dispose(); _engine = null; }
            finally { ScanGate.Release(); }
        };
    }

    /// <summary>执行本机强化选项识别。</summary>
    public async Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        // Reuse one model pipeline without overlapping scans. Cancellation also applies while queued.
        await ScanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(() => Scan(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { ScanGate.Release(); }
    }

    /// <summary>截取游戏窗口中的强化卡片区域并在本机执行 OCR 识别。</summary>
    private static AugmentScanResult Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rectangle game = GetGameBounds(out int gameProcessId);
        if (game.Width < 600 || game.Height < 400)
            return new AugmentScanResult(Array.Empty<int>(), "未找到可截取的游戏窗口，请将游戏设为无边框或窗口模式。");
        if (!IsGameOrSidebarForeground(gameProcessId))
            return new AugmentScanResult(Array.Empty<int>(), "请将游戏或增幅侧边栏置于前台后重试扫描。");
        // The title height varies with resolution and the in-game UI scale.
        // Capture enough vertical space for several narrow OCR passes per card.
        var region = new Rectangle(
            game.Left + game.Width / 6,
            game.Top + game.Height * 28 / 100,
            game.Width * 2 / 3,
            game.Height * 32 / 100);
        using var screenshot = new Bitmap(region.Width, region.Height);
        using (Graphics graphics = Graphics.FromImage(screenshot))
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);
        var catalog = AugmentCatalog.GetAll();
        // ARAMKit's Mayhem stats use 1000+ IDs. The catalog also contains Arena-only
        // entries below 1000, which otherwise produce false OCR matches.
        var mayhemCatalog = catalog.Where(item => item.Id >= 1000).ToArray();
        (int Id, string Name)[] names = mayhemCatalog.Select(item => (item.Id, item.Name))
            .Concat(mayhemCatalog.Where(item => !string.IsNullOrWhiteSpace(item.EnglishName))
                .Select(item => (item.Id, item.EnglishName!))).ToArray();
        var engine = _engine ??= AugmentOcrEngine.Create();
        return AugmentImageRecognizer.Recognize(screenshot, game.Size, engine, names, cancellationToken);
    }

    /// <summary>读取游戏窗口的客户区位置与尺寸。</summary>
    private static Rectangle GetGameBounds(out int gameProcessId)
    {
        gameProcessId = 0;
        foreach (Process process in Process.GetProcessesByName("League of Legends"))
        {
            using (process)
            {
                IntPtr window = process.MainWindowHandle;
                Point origin = Point.Empty;
                if (window != IntPtr.Zero && !IsIconic(window) &&
                    GetClientRect(window, out Rect rect) && ClientToScreen(window, ref origin))
                {
                    gameProcessId = process.Id;
                    return new Rectangle(origin, new Size(rect.Right - rect.Left, rect.Bottom - rect.Top));
                }
            }
        }
        return Rectangle.Empty;
    }

    /// <summary>判断游戏或增幅侧栏是否在前台，决定能否进行本机识别。</summary>
    private static bool IsGameOrSidebarForeground(int gameProcessId)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        if (foregroundProcessId == (uint)gameProcessId) return true;
        if (foregroundProcessId != (uint)Environment.ProcessId) return false;
        var title = new StringBuilder(128);
        GetWindowText(foreground, title, title.Capacity);
        return string.Equals(title.ToString(), "海克斯增幅推荐", StringComparison.Ordinal) ||
            string.Equals(title.ToString(), UiLanguage.T("海克斯增幅推荐"), StringComparison.Ordinal);
    }

    /// <summary>与 Windows 原生矩形布局对应的坐标结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>读取窗口客户区的原生矩形。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect bounds);

    /// <summary>将客户区坐标转换为屏幕坐标。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    /// <summary>调用 Windows API 判断窗口是否处于最小化状态。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    /// <summary>读取当前前台窗口的句柄。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>读取窗口所属线程和进程标识。</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>读取目标窗口的原生标题文本。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
