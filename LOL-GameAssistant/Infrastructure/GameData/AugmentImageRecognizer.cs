using System.Buffers;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using Sdcb.SimdPaddleOCR;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>从游戏中央截图识别增幅；独立于窗口捕获，供离线截图验证。</summary>
internal static class AugmentImageRecognizer
{
    internal static AugmentScanResult Recognize(Bitmap screenshot, Size game, PaddleOcrAll engine,
        (int Id, string Name)[] names, CancellationToken cancellationToken = default)
    {
        var ids = new List<int>(3);
        var recognized = new List<string>(3);
        int cardWidth = game.Width * 19 / 100;
        foreach (int centerPercent in new[] { 31, 50, 69 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cropLeft = game.Width * centerPercent / 100 - game.Width / 6 - cardWidth / 2;
            int left = Math.Clamp(cropLeft, 0, screenshot.Width - cardWidth);
            AugmentNameMatch? best = null;
            var candidates = new Dictionary<int, AugmentNameMatch>();
            var support = new Dictionary<int, int>();
            var cardReadings = new List<string>(3);
            // Prefer the title band without descriptions. Keep the wider passes for
            // other UI scales, wrapped titles and uncertain/short readings.
            int titleWidth = game.Width * 15 / 100;
            int titleLeft = Math.Clamp(game.Width * centerPercent / 100 - game.Width / 6 - titleWidth / 2,
                0, screenshot.Width - titleWidth);
            var titleCrop = new Rectangle(titleLeft, game.Height * 40 / 100 - game.Height * 28 / 100,
                titleWidth, game.Height * 3 / 100);
            using (var title = Scale(screenshot, titleCrop, game.Height))
            {
                Read(title);
                if (best?.Score != 1)
                {
                    using var threshold = PrepareTitle(title, binary: true);
                    Read(threshold);
                }
            }
            // Exact titles can stop early. A fuzzy reading must not hide a better
            // result from another title band or image preprocessing pass.
            foreach ((int topPercent, int heightPercent) in new[] { (38, 8), (32, 10), (44, 10), (28, 10), (50, 10) })
            {
                if (best?.Score == 1) break;
                cancellationToken.ThrowIfCancellationRequested();
                int top = game.Height * topPercent / 100 - game.Height * 28 / 100;
                int height = Math.Min(game.Height * heightPercent / 100, screenshot.Height - top);
                var crop = new Rectangle(left, top, cardWidth, height);
                using var scaled = Scale(screenshot, crop, game.Height);
                Read(scaled);
                if (best?.Score == 1) break;
                // Game titles are pale/gold on a dark textured background. Convert
                // them to dark text on white so the texture no longer dominates OCR.
                using var contrast = PrepareTitle(scaled, binary: false);
                Read(contrast);
                if (best?.Score == 1) break;
                using var threshold = PrepareTitle(scaled, binary: true);
                Read(threshold);
                if (best?.Score == 1) break;
            }

            // 对标题图像执行 OCR，并将可匹配的结果加入候选集合。
            void Read(Bitmap image)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PaddleOcrResult result = ReadImage(image, engine);
                cancellationToken.ThrowIfCancellationRequested();
                cardReadings.Add(result.Text);
                string cardText = string.Join("\n", result.Lines
                    .Where(line => line.RecognitionScore >= 0.7f).Select(line => line.Text));
                var candidate = AugmentNameMatcher.MatchCard(cardText, names);
                if (candidate == null || ids.Contains(candidate.Id)) return;
                support[candidate.Id] = support.GetValueOrDefault(candidate.Id) + 1;
                if (!candidates.TryGetValue(candidate.Id, out var previous) || candidate.Score > previous.Score)
                    candidates[candidate.Id] = candidate;
                if (best == null || candidate.Score > best.Score) best = candidate;
            }
            // Conflicting fuzzy readings across preprocessing passes are also ambiguous.
            var ranked = candidates.Values.OrderByDescending(item => item.Score).Take(2).ToArray();
            if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.08) best = null;
            // A fuzzy short title must be reproduced by at least two OCR passes.
            if (best is { Score: < 1 } && support.GetValueOrDefault(best.Id) < 2 &&
                names.Any(item => item.Id == best.Id && item.Name.Count(char.IsLetterOrDigit) <= 3)) best = null;
            recognized.Add(string.Join("\n", cardReadings));
            if (best != null) ids.Add(best.Id);
        }
        string text = string.Join("\n---\n", recognized);
        return ids.Count == 0
            ? new AugmentScanResult(ids, "没有识别到增幅选项；请在三张卡片显示时重试。", text)
            : new AugmentScanResult(ids, $"已识别 {ids.Count} 个增幅选项。", text);
    }

    /// <summary>直接传递 BGR 像素，避免 PNG 编码及外部文件依赖。</summary>
    internal static PaddleOcrResult ReadImage(Bitmap image, PaddleOcrAll engine)
    {
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        byte[]? pixels = null;
        try
        {
            int stride = Math.Abs(data.Stride);
            int length = checked(stride * image.Height);
            pixels = ArrayPool<byte>.Shared.Rent(length);
            // Row copies also handle bitmaps whose storage uses a negative stride.
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * stride, stride);
            return engine.Run(pixels.AsSpan(0, length), image.Width, image.Height, stride, ImagePixelFormat.Bgr24);
        }
        finally
        {
            if (pixels != null) ArrayPool<byte>.Shared.Return(pixels);
            image.UnlockBits(data);
        }
    }

    private static Bitmap Scale(Bitmap screenshot, Rectangle crop, int gameHeight)
    {
        int scale = gameHeight < 900 ? 3 : 2;
        var result = new Bitmap(crop.Width * scale, crop.Height * scale, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(screenshot, new Rectangle(0, 0, result.Width, result.Height), crop, GraphicsUnit.Pixel);
        return result;
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
}
