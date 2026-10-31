using System.Drawing.Drawing2D;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal static class JungleRouteOverlayRenderer
{
    internal static Bitmap Render(JungleRouteAnalysis analysis, string hotkey)
    {
        var image = new Bitmap(1120, 740);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(18, 24, 36));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var title = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold);
        using var body = new Font(UiMetrics.FontFamily, 11);
        using var white = new SolidBrush(Color.FromArgb(224, 234, 245));
        using var muted = new SolidBrush(Color.FromArgb(164, 187, 210));
        graphics.DrawString("敌方打野 · 历史路线分析", title, white, 24, 18);
        graphics.DrawString($"{hotkey} 收起 · 再次打开时重新分析", body, muted, 24, 57);
        using var map = new JungleRouteMap { Size = new(650, 640) };
        map.SetRoute(analysis.Prediction?.Points ?? [], predicted: true);
        using var mapImage = new Bitmap(map.Width, map.Height);
        using (var mapGraphics = Graphics.FromImage(mapImage)) map.RenderOverlay(mapGraphics);
        graphics.DrawImageUnscaled(mapImage, 12, 88);
        string enemy = analysis.Enemy == null ? "" : $"{analysis.Enemy.Name} · {(analysis.TeamId == 100 ? "蓝" : "红")}方打野\n\n";
        graphics.DrawString(enemy + analysis.Status, body, white, new RectangleF(682, 100, 410, 160));
        string detail = analysis.Prediction is { } prediction
            ? $"常见起始区域：{prediction.OpeningRegion}\n历史一致：{prediction.SupportingGames}/{prediction.TotalGames} 场\n置信度：{prediction.Confidence}\n{(prediction.SameChampion ? "同英雄样本" : "跨英雄参考")}\n\n{prediction.ActivityHint}\n\n" +
                string.Join("\n", prediction.Points.Take(8).Select(p => $"{p.Seconds / 60}:{p.Seconds % 60:D2}  {JungleRoutePredictor.Region(p.Position)}"))
            : "暂无可靠路线。若客户端无法识别打野，可在助手路线窗口中手动确认。";
        graphics.DrawString(detail, body, white, new RectangleF(682, 270, 410, 360));
        graphics.DrawString("仅参考已结束对局的历史采样；虚线不代表本局实时位置或精确清野顺序。", body, muted, new RectangleF(682, 650, 410, 74));
        return image;
    }
}
