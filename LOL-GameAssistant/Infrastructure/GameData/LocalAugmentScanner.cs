using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using Tesseract;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>只截取本机游戏窗口中央的增幅卡片区域，并在本机使用 Tesseract OCR。</summary>
public sealed class LocalAugmentScanner : IAugmentScanner
{
    public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

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
            game.Top + game.Height * 31 / 100,
            game.Width * 2 / 3,
            game.Height * 24 / 100);
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
            int? id = null;
            var cardReadings = new List<string>(3);
            // Try the usual title band first; only missed or duplicated cards
            // need the extra passes above and below it.
            foreach ((int topPercent, int heightPercent) in new[] { (38, 8), (32, 10), (44, 10) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                int top = game.Height * topPercent / 100 - (region.Top - game.Top);
                int height = Math.Min(game.Height * heightPercent / 100, region.Height - top);
                var crop = new Rectangle(left, top, cardWidth, height);
                using var scaled = new Bitmap(crop.Width * 2, crop.Height * 2);
                using (Graphics graphics = Graphics.FromImage(scaled))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(screenshot, new Rectangle(0, 0, scaled.Width, scaled.Height),
                        crop, GraphicsUnit.Pixel);
                }
                using var stream = new MemoryStream();
                scaled.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                using var pix = Pix.LoadFromMemory(stream.ToArray());
                using var page = engine.Process(pix, PageSegMode.SparseText);
                string cardText = page.GetText();
                cardReadings.Add(cardText);
                int? candidate = AugmentNameMatcher.Match(cardText, names, limit: 1).FirstOrDefault();
                if (candidate is > 0 && !ids.Contains(candidate.Value))
                {
                    id = candidate;
                    break;
                }
            }
            recognized.Add(string.Join("\n", cardReadings));
            if (id is > 0) ids.Add(id.Value);
        }
        string text = string.Join("\n---\n", recognized);
        return ids.Count == 0
            ? new AugmentScanResult(ids, "没有识别到增幅选项；请在三张卡片显示时重试。", text)
            : new AugmentScanResult(ids, $"已识别 {ids.Count} 个增幅选项。", text);
    }

    private static Rectangle GetGameBounds(out int gameProcessId)
    {
        gameProcessId = 0;
        foreach (Process process in Process.GetProcessesByName("League of Legends"))
        {
            using (process)
            {
                if (process.MainWindowHandle != IntPtr.Zero && GetWindowRect(process.MainWindowHandle, out Rect rect))
                {
                    gameProcessId = process.Id;
                    return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                }
            }
        }
        return Rectangle.Empty;
    }

    private static bool IsGameOrSidebarForeground(int gameProcessId)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        if (foregroundProcessId == (uint)gameProcessId) return true;
        if (foregroundProcessId != (uint)Environment.ProcessId) return false;
        var title = new StringBuilder(128);
        GetWindowText(foreground, title, title.Capacity);
        return string.Equals(title.ToString(), "海克斯增幅推荐", StringComparison.Ordinal);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
