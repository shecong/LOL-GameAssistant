using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;
using Tesseract;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>只截取本机游戏窗口中央的增幅卡片区域，并在本机使用 Tesseract OCR。</summary>
public sealed class LocalAugmentScanner : IAugmentScanner
{
    /// <summary>执行本机强化选项识别。</summary>
    public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    /// <summary>截取游戏窗口中的强化卡片区域并在本机执行 OCR 识别。</summary>
    private static AugmentScanResult Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string tessdata = Path.Combine(AppContext.BaseDirectory, "Resources", "tessdata");
        if (!File.Exists(Path.Combine(tessdata, "chi_sim.traineddata")) ||
            !File.Exists(Path.Combine(tessdata, "eng.traineddata")))
            return new AugmentScanResult(Array.Empty<int>(), "OCR 语言数据缺失，请重新安装完整发布包。");

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
        using var engine = new TesseractEngine(tessdata, "chi_sim+eng", EngineMode.LstmOnly);
        var ids = new List<int>(3);
        var recognized = new List<string>(3);
        int cardWidth = game.Width * 19 / 100;
        foreach (int centerPercent in new[] { 31, 50, 69 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cropLeft = game.Left + game.Width * centerPercent / 100 - region.Left - cardWidth / 2;
            int left = Math.Clamp(cropLeft, 0, region.Width - cardWidth);
            AugmentNameMatch? best = null;
            var candidates = new Dictionary<int, AugmentNameMatch>();
            var cardReadings = new List<string>(3);
            // Exact titles can stop early. A fuzzy reading must not hide a better
            // result from another title band or image preprocessing pass.
            foreach ((int topPercent, int heightPercent) in new[] { (38, 8), (32, 10), (44, 10), (28, 10), (50, 10) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                int top = game.Height * topPercent / 100 - (region.Top - game.Top);
                int height = Math.Min(game.Height * heightPercent / 100, region.Height - top);
                var crop = new Rectangle(left, top, cardWidth, height);
                // Small windowed titles need more enlargement than 1080p/4K titles.
                int scale = game.Height < 900 ? 3 : 2;
                using var scaled = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
                using (Graphics graphics = Graphics.FromImage(scaled))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(screenshot, new Rectangle(0, 0, scaled.Width, scaled.Height),
                        crop, GraphicsUnit.Pixel);
                }
                Read(scaled, PageSegMode.SparseText);
                if (best?.Score == 1) break;
                // Game titles are pale/gold on a dark textured background. Convert
                // them to dark text on white so the texture no longer dominates OCR.
                using var contrast = PrepareTitle(scaled, binary: false);
                Read(contrast, PageSegMode.SingleBlock);
                if (best?.Score == 1) break;
                using var threshold = PrepareTitle(scaled, binary: true);
                Read(threshold, PageSegMode.SparseText);
                if (best?.Score == 1) break;

                // 对标题图像执行 OCR，并将可匹配的结果加入候选集合。
                void Read(Bitmap image, PageSegMode mode)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new MemoryStream();
                    image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    using var pix = Pix.LoadFromMemory(stream.ToArray());
                    using var page = engine.Process(pix, mode);
                    string cardText = page.GetText();
                    cardReadings.Add(cardText);
                    var candidate = AugmentNameMatcher.MatchCard(cardText, names);
                    if (candidate == null || ids.Contains(candidate.Id)) return;
                    if (!candidates.TryGetValue(candidate.Id, out var previous) || candidate.Score > previous.Score)
                        candidates[candidate.Id] = candidate;
                    if (best == null || candidate.Score > best.Score) best = candidate;
                }
            }
            // Conflicting fuzzy readings across preprocessing passes are also ambiguous.
            var ranked = candidates.Values.OrderByDescending(item => item.Score).Take(2).ToArray();
            if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.08) best = null;
            recognized.Add(string.Join("\n", cardReadings));
            if (best != null) ids.Add(best.Id);
        }
        string text = string.Join("\n---\n", recognized);
        return ids.Count == 0
            ? new AugmentScanResult(ids, "没有识别到增幅选项；请在三张卡片显示时重试。", text)
            : new AugmentScanResult(ids, $"已识别 {ids.Count} 个增幅选项。", text);
    }

    /// <summary>调整标题图像的亮度或阈值，以减少背景对 OCR 的干扰。</summary>
    private static Bitmap PrepareTitle(Bitmap source, bool binary)
    {
        var result = source.Clone(new Rectangle(Point.Empty, source.Size), PixelFormat.Format24bppRgb);
        BitmapData data = result.LockBits(new Rectangle(Point.Empty, result.Size), ImageLockMode.ReadWrite,
            PixelFormat.Format24bppRgb);
        try
        {
            byte[] row = new byte[result.Width * 3];
            for (int y = 0; y < result.Height; y++)
            {
                IntPtr address = IntPtr.Add(data.Scan0, y * data.Stride);
                Marshal.Copy(address, row, 0, row.Length);
                for (int x = 0; x < row.Length; x += 3)
                {
                    int luminance = (row[x + 2] * 299 + row[x + 1] * 587 + row[x] * 114) / 1000;
                    byte value = binary ? (byte)(luminance >= 150 ? 0 : 255)
                        : (byte)(255 - Math.Clamp((luminance - 65) * 255 / 150, 0, 255));
                    row[x] = row[x + 1] = row[x + 2] = value;
                }
                Marshal.Copy(row, 0, address, row.Length);
            }
        }
        finally { result.UnlockBits(data); }
        return result;
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
