using System.Drawing.Imaging;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class AugmentCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void CaptureIgnoresSwapChainAlphaWhenResizedForOcr(byte alpha)
    {
        // Two different rows also verify the frame's BGR byte order and stride.
        byte[] pixels = [210, 230, 240, alpha, 19, 16, 2, alpha];
        using var capture = NativeGameOverlayBridge.CreateCaptureBitmap(pixels, 1, 2);
        using var scaled = new Bitmap(2, 4, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            graphics.DrawImage(capture, new Rectangle(0, 0, 2, 4), new Rectangle(0, 0, 1, 2), GraphicsUnit.Pixel);
        }
        Assert.Equal(Color.FromArgb(240, 230, 210).ToArgb(), scaled.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(2, 16, 19).ToArgb(), scaled.GetPixel(1, 3).ToArgb());
        Assert.Equal(255, capture.GetPixel(0, 0).A);
    }
}
