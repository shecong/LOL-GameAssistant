using System.Drawing.Drawing2D;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// 简单的统计图表绘制工具（使用 System.Drawing）
    /// </summary>
    public static class ChartDrawer
    {
        private static readonly Color WinColor = Color.FromArgb(76, 175, 80);
        private static readonly Color LossColor = Color.FromArgb(244, 67, 54);
        private static readonly Color GridColor = Color.FromArgb(235, 235, 235);
        private static readonly Color TextColor = Color.FromArgb(60, 60, 60);
        private static readonly Color AxisColor = Color.FromArgb(180, 180, 180);
        private static readonly Color BarColor = Color.FromArgb(33, 150, 243);

        /// <summary>
        /// 绘制 KDA 趋势折线图
        /// </summary>
        public static void DrawKdaTrend(Graphics g, Rectangle bounds,
            List<(double kda, bool win)> data, string title)
        {
            int ml = 45, mr = 15, mt = 28, mb = 28;
            var ca = new Rectangle(bounds.X + ml, bounds.Y + mt,
                bounds.Width - ml - mr, bounds.Height - mt - mb);
            if (data.Count == 0 || ca.Width <= 0 || ca.Height <= 0) return;

            g.FillRectangle(Brushes.White, bounds);
            using (var f = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                g.DrawString(title, f, Brushes.Black, bounds.X + 5, bounds.Y + 3);

            double maxVal = Math.Max(8, data.Count > 0 ? data.Max(d => d.kda) * 1.25 : 8);
            int n = data.Count;

            using (var gp = new Pen(GridColor, 1))
            using (var ap = new Pen(AxisColor, 1.2f))
            using (var lf = new Font("Microsoft YaHei UI", 7))
            {
                int ySteps = 4;
                for (int i = 0; i <= ySteps; i++)
                {
                    float y = ca.Bottom - (float)(i * ca.Height / ySteps);
                    g.DrawLine(gp, ca.Left, y, ca.Right, y);
                    g.DrawString($"{maxVal * i / ySteps:F1}", lf, Brushes.DimGray, bounds.X + 3, y - 6);
                }
                for (int i = 0; i < Math.Min(n, 30); i += Math.Max(1, n / 8))
                {
                    float x = ca.Left + (float)(i * ca.Width / Math.Max(1, n - 1));
                    g.DrawString($"{i + 1}", lf, Brushes.DimGray, x - 4, ca.Bottom + 4);
                }
                g.DrawLine(ap, ca.Left, ca.Top, ca.Left, ca.Bottom);
                g.DrawLine(ap, ca.Left, ca.Bottom, ca.Right, ca.Bottom);
            }

            if (n > 1)
            {
                using (var wp = new Pen(WinColor, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var lp = new Pen(LossColor, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        float x1 = ca.Left + (float)(i * ca.Width / (n - 1));
                        float x2 = ca.Left + (float)((i + 1) * ca.Width / (n - 1));
                        float y1 = ca.Bottom - (float)(data[i].kda / maxVal * ca.Height);
                        float y2 = ca.Bottom - (float)(data[i + 1].kda / maxVal * ca.Height);
                        g.DrawLine(data[i].win ? wp : lp, x1, y1, x2, y2);
                    }
                }
                using (var wb = new SolidBrush(WinColor))
                using (var lb = new SolidBrush(LossColor))
                {
                    for (int i = 0; i < n; i++)
                    {
                        float x = ca.Left + (float)(i * ca.Width / (n - 1));
                        float y = ca.Bottom - (float)(data[i].kda / maxVal * ca.Height);
                        g.FillEllipse(data[i].win ? wb : lb, x - 3, y - 3, 6, 6);
                    }
                }
            }
        }

        /// <summary>
        /// 绘制英雄使用频率水平条形图
        /// </summary>
        public static void DrawChampionBars(Graphics g, Rectangle bounds,
            List<(string name, int games, double winRate)> data, string title)
        {
            int ml = 110, mr = 20, mt = 28, mb = 10;
            var ca = new Rectangle(bounds.X + ml, bounds.Y + mt,
                bounds.Width - ml - mr, bounds.Height - mt - mb);
            if (data.Count == 0 || ca.Height <= 0) return;

            g.FillRectangle(Brushes.White, bounds);
            using (var f = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                g.DrawString(title, f, Brushes.Black, bounds.X + 5, bounds.Y + 3);

            int maxG = data.Max(d => d.games);
            if (maxG == 0) return;

            float bh = Math.Min(22, (float)ca.Height / data.Count - 3);
            float totalH = (bh + 3) * data.Count;
            float sy = ca.Top + Math.Max(0, (ca.Height - totalH) / 2);

            using (var lf = new Font("Microsoft YaHei UI", 8))
            using (var vf = new Font("Microsoft YaHei UI", 8, FontStyle.Bold))
            using (var barB = new SolidBrush(BarColor))
            using (var whiteB = new SolidBrush(Color.White))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    float y = sy + i * (bh + 3);
                    float bw = Math.Max(1, (float)data[i].games / maxG * ca.Width);
                    g.FillRectangle(barB, ca.Left, y, bw, bh);
                    g.DrawString(data[i].name, lf, Brushes.DimGray, bounds.X + 3, y + 2);
                    g.DrawString($"{data[i].games}\u573A ({data[i].winRate:F1}%)", vf, whiteB, ca.Left + 4, y + 2);
                }
            }
        }
    }
}
