using System.Drawing.Drawing2D;
using LOL.Lounge;

namespace LOL_GameAssistant.Infrastructure.Lounge;

// Built-in vector faces. No URLs, uploaded bytes or clipboard images are accepted.
internal static class LoungeStickerArt
{

    public static Bitmap Create(LoungeSticker sticker)
    {
        var bitmap = new Bitmap(156, 132);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ColorTranslator.FromHtml(sticker.Color));
        using var face = new SolidBrush(Color.FromArgb(255, 224, 130));
        using var ink = new Pen(Color.FromArgb(43, 39, 56), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dark = new SolidBrush(ink.Color);
        g.FillEllipse(face, 43, 12, 70, 70);
        if (sticker.Face is "sleep" or "wait")
        { g.DrawLine(ink, 57, 39, 69, 39); g.DrawLine(ink, 87, 39, 99, 39); }
        else
        { g.FillEllipse(dark, 59, 33, 7, 11); g.FillEllipse(dark, 90, 33, 7, 11); }
        if (sticker.Face is "happy" or "love") g.DrawArc(ink, 61, 43, 35, 24, 0, 180);
        else if (sticker.Face == "cry")
        {
            g.DrawArc(ink, 64, 57, 28, 16, 180, 180);
            using var tear = new SolidBrush(Color.DeepSkyBlue);
            g.FillEllipse(tear, 56, 45, 7, 23); g.FillEllipse(tear, 93, 45, 7, 23);
        }
        else if (sticker.Face == "excited") g.FillEllipse(dark, 66, 48, 25, 22);
        else g.DrawLine(ink, 67, 59, 88, 59);
        using var caption = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        using var markFont = new Font("Segoe UI", 19F, FontStyle.Bold);
        string mark = sticker.Face switch { "question" => "?", "sleep" => "zZ", "love" => "♥", "excited" => "!", _ => "✦" };
        g.DrawString(mark, markFont, Brushes.White, 116, 12);
        using var align = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(sticker.Caption, caption, Brushes.White, new RectangleF(0, 88, 156, 38), align);
        return bitmap;
    }

}
