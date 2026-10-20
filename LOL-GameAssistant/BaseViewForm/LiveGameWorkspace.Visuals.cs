using System.Drawing.Drawing2D;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed partial class LiveGameWorkspace
{
    // All views share these column proportions, image ownership and semantic colors.
    // Draw coordinates use logical 96-DPI units; text and graphics scale together.
    private abstract class PaintedPanel : Control, IThemeAware
    {
        protected readonly Font Small = new(UiMetrics.FontFamily, 8.5f);
        protected readonly Font Body = new(UiMetrics.FontFamily, 10f);
        protected readonly Font Bold = new(UiMetrics.FontFamily, 10.5f, FontStyle.Bold);
        protected readonly Font Large = new(UiMetrics.FontFamily, 22f, FontStyle.Bold);
        protected ThemePalette Palette => UiTheme.Palette;
        protected float W => Width / ScaleFactor;
        protected float H => Height / ScaleFactor;
        private float ScaleFactor => DeviceDpi / 96f;
        protected Graphics Canvas = null!;

        protected PaintedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Canvas = e.Graphics;
            var state = Canvas.Save();
            Canvas.ScaleTransform(ScaleFactor, ScaleFactor);
            Canvas.SmoothingMode = SmoothingMode.AntiAlias;
            Canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Canvas.Clear(Palette.Surface);
            Render();
            Canvas.Restore(state);
        }
        protected abstract void Render();
        protected void TextAt(string? text, Font font, Color color, float x, float y, float width, float height,
            bool right = false, bool center = false)
        {
            if (width <= 0 || height <= 0) return;
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = center ? StringAlignment.Center : right ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            Canvas.DrawString(text ?? "", font, brush, new RectangleF(x, y, width, height), format);
        }
        protected void Box(float x, float y, float width, float height, Color fill, Color? stroke = null, float radius = 6)
        {
            if (width <= 0 || height <= 0) return;
            using var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(width, height));
            path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(fill);
            Canvas.FillPath(brush, path);
            if (stroke.HasValue) { using var pen = new Pen(stroke.Value); Canvas.DrawPath(pen, path); }
        }
        protected Color TeamColor(bool red) => red ? Color.FromArgb(240, 108, 133) : Color.FromArgb(55, 178, 248);
        protected void Portrait(LivePlayerForm player, float x, float y, float size, bool red)
        {
            using var circle = new GraphicsPath(); circle.AddEllipse(x, y, size, size);
            var state = Canvas.Save(); Canvas.SetClip(circle);
            if (player.LayoutPortrait is { } image) Canvas.DrawImage(image, x, y, size, size);
            else
            {
                using var fill = new SolidBrush(Palette.SurfaceMuted); Canvas.FillEllipse(fill, x, y, size, size);
                TextAt(player.LayoutName.Length > 0 ? player.LayoutName[..1] : "?", Bold, Palette.TextSecondary, x, y, size, size, center: true);
            }
            Canvas.Restore(state);
            using var pen = new Pen(TeamColor(red), 1.5f); Canvas.DrawEllipse(pen, x, y, size, size);
        }
        protected void Rank(LivePlayerForm player, float x, float y, float width, bool right = false)
        {
            var emblem = player.LayoutRankEmblem;
            float size = Math.Min(34, width / 3);
            if (emblem != null) Canvas.DrawImage(emblem, right ? x + width - size : x, y, size, 32);
            TextAt(player.LayoutRank, Small, Palette.IsDark ? Color.FromArgb(151, 211, 249) : Palette.TextPrimary,
                right ? x : x + size + 3, y, Math.Max(0, width - size - 3), 32, right);
        }
        protected void Outcomes(LivePlayerForm player, float x, float y, float available)
        {
            float step = Math.Min(26, available / 5), size = Math.Max(12, step - 4);
            var games = player.LayoutHistory;
            for (int i = 0; i < 5; i++)
            {
                var p = i < games.Count ? games[i].GetParticipant(player.Puuid ?? "") : null;
                bool known = p?.stats?.HasWinResult == true, won = known && p!.IsWin();
                var color = !known ? Palette.Border : won ? Color.FromArgb(25, 137, 105) : Color.FromArgb(177, 62, 78);
                Box(x + step * i, y, size, 22, color, radius: 3);
                TextAt(known ? won ? "胜" : "负" : "·", Small, Color.White, x + step * i, y, size, 22, center: true);
            }
        }
        protected void Line(float y)
        {
            using var pen = new Pen(Palette.Border); Canvas.DrawLine(pen, 12, y, W - 12, y);
        }
        public virtual void ApplyTheme(ThemePalette palette) { BackColor = palette.Surface; ForeColor = palette.TextPrimary; Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Small.Dispose(); Body.Dispose(); Bold.Dispose(); Large.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class TeamBanner(bool red) : PaintedPanel
    {
        internal LivePlayerForm[] Players = [];
        protected override void Render()
        {
            var accent = TeamColor(red);
            Box(0.5f, 0.5f, W - 1, H - 1, Palette.SurfaceRaised, Palette.Border);
            using var gradient = new LinearGradientBrush(new RectangleF(1, 1, Math.Max(1, W - 2), Math.Max(1, H - 2)),
                Color.FromArgb(Palette.IsDark ? 95 : 45, accent), Color.FromArgb(0, accent), 0f);
            Canvas.FillRectangle(gradient, 1, 1, W - 2, H - 2);
            // Small vector team crest stays sharp at any DPI and has no external dependency.
            float cy = H / 2;
            using var brush = new SolidBrush(accent);
            Canvas.FillPolygon(brush, new PointF[] { new(15, cy - 14), new(25, cy - 5), new(30, cy - 18),
                new(35, cy - 5), new(45, cy - 14), new(40, cy + 6), new(30, cy + 18), new(20, cy + 6) });
            TextAt(Text, Bold, Palette.TextPrimary, 56, H > 48 ? cy - 22 : cy - 12, W - 66, 25);
            if (H > 48)
                TextAt(TeamStats(Players), Small, Palette.TextSecondary, 56, cy + 3, W - 64, 22);
            else if (W > 500)
                TextAt($"{Players.Length} 人   ·   {TeamStats(Players)}", Small, Palette.TextSecondary, 148, cy - 12, W - 160, 24);
        }
    }

    private static string TeamStats(LivePlayerForm[] players)
    {
        int count = players.Sum(p => p.RecentSampleSize), wins = players.Sum(p => p.RecentWins);
        var levels = players.Where(p => p.PlayerLevel.HasValue).Select(p => p.PlayerLevel!.Value).ToArray();
        return $"平均等级 {(levels.Length == 0 ? "—" : levels.Average().ToString("0"))}   ·   近期胜率 {(count > 0 ? $"{100d * wins / count:0}%" : "—")}";
    }

    private sealed class ColumnHeader(bool roles, bool mirror) : PaintedPanel
    {
        protected override void Render()
        {
            Canvas.Clear(Palette.SurfaceRaised);
            if (roles)
            {
                Cell("玩家", 0, .37f); Cell("段位", .37f, .27f); Cell("KDA", .64f, .18f); Cell("胜率", .82f, .18f);
            }
            else
            {
                Cell("玩家", 0, .34f); Cell("段位", .34f, .23f); Cell("胜率", .57f, .11f);
                Cell("KDA", .68f, .12f); Cell("最近五场", .80f, .20f);
            }
            void Cell(string text, float x, float width) => TextAt(text, Small, Palette.TextSecondary,
                mirror ? W * (1 - x - width) : W * x + 12, 0, W * width - 14, H, mirror);
        }
    }

    private sealed class PlayerTile : PaintedPanel
    {
        internal LivePlayerForm Player { get; }
        internal bool Selected;
        private readonly bool _red, _compact, _roster, _roles;
        private readonly ToolTip _tip = new();
        private bool _hover;
        internal PlayerTile(LivePlayerForm player, bool red, bool compact, bool roster, bool roles)
        {
            Player = player; _red = red; _compact = compact; _roster = roster; _roles = roles;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            RefreshData();
        }
        internal void RefreshData()
        {
            AccessibleName = $"{Player.LayoutName}，{Player.LayoutPosition}，{Player.LayoutRank}，胜率{Player.LayoutWinRate}，KDA {Player.LayoutKda}";
            AccessibleRole = AccessibleRole.PushButton;
            _tip.SetToolTip(this, $"{AccessibleName}\n{Player.LayoutAssessment} · {Player.LayoutNote}\n点击查看最近十场完整战绩；对位视图中右键可指定位置");
            Invalidate();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        }
        protected override void Render()
        {
            if (Player.IsDisposed) return;
            var p = Palette; var accent = TeamColor(_red);
            Box(.5f, .5f, W - 1, H - 1, Selected || _hover ? p.SurfaceMuted : p.SurfaceRaised,
                Selected || _hover ? accent : _compact ? p.Border : null, _compact ? 7 : 3);
            if (_compact)
            {
                Box(1, 1, W - 2, 3, accent, radius: 1);
                float y = Math.Max(12, (H - 230) / 2);
                Portrait(Player, 14, y + 4, 46, _red);
                TextAt(Player.LayoutName, Bold, p.TextPrimary, 70, y, W - 82, 27);
                TextAt(Player.LayoutPosition, Small, p.TextSecondary, 70, y + 27, W - 82, 23);
                Rank(Player, 14, y + 65, W - 42);
                TextAt("›", Body, p.TextSecondary, W - 24, y + 65, 16, 32);
                TextAt(Player.LayoutWinRate, Large, p.TextPrimary, 14, y + 109, W * .55f, 40);
                TextAt(Player.LayoutKda, Bold, p.TextPrimary, W * .62f, y + 116, W * .32f, 27);
                TextAt("近期胜率", Small, p.TextSecondary, 14, y + 151, W * .55f, 20);
                TextAt("KDA", Small, p.TextSecondary, W * .62f, y + 151, W * .32f, 20);
                Outcomes(Player, 14, y + 186, W - 28);
                TextAt($"最近 5 场  ·  详情 {Player.LayoutHistory.Count} 场", Small, p.TextSecondary, 14, y + 211, W - 28, 20);
            }
            else if (_roster)
            {
                float y = (H - 42) / 2;
                Portrait(Player, 10, y + 2, 38, _red);
                TextAt(Player.LayoutName, Bold, p.TextPrimary, 58, y, W - 72, 23);
                TextAt($"{Player.LayoutPosition}  ·  {Player.LayoutRank}", Small, p.TextSecondary, 58, y + 23, W - 72, 19);
            }
            else if (_roles) RenderRole();
            else
            {
                float y = Math.Max(4, (H - 76) / 2), identity = W * .34f;
                Portrait(Player, 12, y + 5, 42, _red);
                TextAt(Player.LayoutName, Bold, p.TextPrimary, 64, y + 2, identity - 68, 25);
                TextAt(Player.LayoutPosition, Small, p.TextSecondary, 64, y + 29, identity - 68, 20);
                Rank(Player, identity + 8, y + 11, W * .23f - 16);
                TextAt(Player.LayoutWinRate, Bold, p.TextPrimary, W * .57f + 8, y + 10, W * .11f - 8, 26);
                TextAt(Player.LayoutKda, Bold, p.TextPrimary, W * .68f + 8, y + 4, W * .12f - 8, 26);
                TextAt($"{Player.RecentSampleSize} 场", Small, p.TextSecondary, W * .68f + 8, y + 30, W * .12f - 8, 20);
                Outcomes(Player, W * .80f + 8, y + 15, W * .20f - 14);
                TextAt(string.Join("  ·  ", new[] { Player.LayoutAssessment, Player.LayoutNote }.Where(s => s.Length > 0)),
                    Small, p.TextSecondary, 64, y + 55, W - 84, 18);
                Line(H - 1);
            }
            if (Focused) Box(4, 4, W - 8, H - 8, Color.Transparent, accent, 4);
        }
        private void RenderRole()
        {
            var p = Palette; float y = Math.Max(4, (H - 70) / 2);
            Portrait(Player, _red ? W - 56 : 12, y + 6, 44, _red);
            float identity = W * .37f;
            TextAt(Player.LayoutName, Bold, p.TextPrimary, _red ? W - identity + 4 : 66, y + 3, identity - 72, 25, _red);
            TextAt(Player.LayoutPosition, Small, p.TextSecondary, _red ? W - identity + 4 : 66, y + 31, identity - 72, 22, _red);
            Rank(Player, _red ? W * .36f + 6 : identity + 8, y + 10, W * .27f - 16, _red);
            float kda = _red ? W * .18f + 8 : W * .64f + 8, rate = _red ? 8 : W * .82f + 8;
            TextAt(Player.LayoutKda, Bold, p.TextPrimary, kda, y + 3, W * .18f - 16, 28, _red);
            TextAt($"{Player.RecentSampleSize} 场", Small, p.TextSecondary, kda, y + 31, W * .18f - 16, 22, _red);
            TextAt(Player.LayoutWinRate, Bold, TeamColor(_red), rate, y + 14, W * .18f - 16, 28, _red);
            TextAt(Player.LayoutAssessment, Small, p.TextSecondary, 12, y + 56, W - 24, 18, _red);
            Line(H - 1);
        }
        protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
    }

    private sealed class PlayerProfile : PaintedPanel
    {
        internal LivePlayerForm? Player;
        protected override void Render()
        {
            Box(.5f, .5f, W - 1, H - 1, Palette.SurfaceRaised, Palette.Border);
            TextAt("玩家详情", Bold, Palette.TextSecondary, 16, 4, W - 32, 30);
            if (Player is not { IsDisposed: false } player) return;
            Portrait(player, 16, 46, 64, false);
            TextAt(player.LayoutName, Large, Palette.TextPrimary, 96, 42, W - 112, 40);
            TextAt($"{player.LayoutPosition}   ·   {player.LayoutRank}   ·   Lv.{player.PlayerLevel?.ToString() ?? "—"}",
                Body, Palette.TextSecondary, 96, 84, W - 112, 26);
            float width = (W - 48) / 3;
            string[] values = [player.LayoutWinRate, player.LayoutKda, player.RecentSampleSize.ToString()];
            string[] labels = ["近期胜率", "平均 KDA", "统计场数"];
            for (int i = 0; i < 3; i++)
            {
                float x = 16 + i * (width + 8);
                Box(x, 128, width, 84, Palette.SurfaceMuted);
                TextAt(labels[i], Small, Palette.TextSecondary, x, 135, width, 22, center: true);
                TextAt(values[i], Large, Palette.TextPrimary, x, 158, width, 42, center: true);
            }
            TextAt(player.LayoutAssessment + "  ·  " + player.LayoutNote, Small, Palette.TextSecondary, 16, 219, W - 32, 24);
        }
    }

    private sealed class PlayerDetailPane : Panel, IThemeAware
    {
        private readonly PlayerProfile _profile = new() { Dock = DockStyle.Top, Height = 250 };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        private readonly MatchColumnHeader _columns = new() { Dock = DockStyle.Top, Height = 28 };
        private readonly MatchScrollPanel _matches = new() { Dock = DockStyle.Fill, AutoScroll = true, Name = "layoutRecentMatches" };
        private LivePlayerForm? _player;
        private string? _historyKey;
        private bool _arrangingRows;
        internal PlayerDetailPane()
        {
            Controls.Add(_matches); Controls.Add(_columns); Controls.Add(_heading); Controls.Add(_profile);
            _matches.SizeChanged += (_, _) => ArrangeRows();
            _matches.Layout += (_, _) => ArrangeRows();
            _matches.VisibleChanged += (_, _) => ArrangeRows();
        }
        private void ArrangeRows()
        {
            if (_arrangingRows) return;
            _arrangingRows = true;
            try
            {
                MatchListScrolling.LayoutRows(_matches, _matches.Controls.Cast<Control>().ToArray(), 6, 6);
                MatchListScrolling.LayoutRows(_matches, _matches.Controls.Cast<Control>().ToArray(), 6, 6);
            }
            finally { _arrangingRows = false; }
        }
        internal void ScrollToTop() => _matches.AutoScrollPosition = Point.Empty;
        internal void SetPlayer(LivePlayerForm player)
        {
            _profile.Player = player; _profile.Invalidate();
            var games = player.LayoutHistory;
            _heading.Text = $"最近 {games.Count} 场战绩  ·  滚动查看全部，点击 › 查看对局";
            string key = string.Join(",", games.Select(game => game.GameId)) + ":" + player.LayoutHistoryStatus;
            if (_player == player && _historyKey == key) return;
            _player = player; _historyKey = key;
            ControlLifetime.ClearAndDispose(_matches);
            _matches.AutoScrollPosition = Point.Empty;
            foreach (var game in games) _matches.Controls.Add(player.CreateLayoutHistoryRow(game));
            if (games.Count == 0) _matches.Controls.Add(new Label { Text = player.LayoutHistoryStatus, Height = 64, Padding = new Padding(8), ForeColor = UiTheme.Palette.TextSecondary });
            ArrangeRows();
        }
        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = _heading.BackColor = palette.SurfaceRaised;
            _heading.ForeColor = palette.TextPrimary;
            _matches.BackColor = palette.Surface;
            _profile.ApplyTheme(palette);
            _columns.ApplyTheme(palette);
            foreach (var row in _matches.Controls.OfType<IThemeAware>()) row.ApplyTheme(palette);
        }
    }

    private sealed class MatchColumnHeader : PaintedPanel
    {
        protected override void Render()
        {
            Canvas.Clear(Palette.SurfaceMuted);
            Cell("英雄", .02f, .23f); Cell("结果", .27f, .10f); Cell("K / D / A", .38f, .18f);
            Cell("模式", .57f, .17f); Cell("时长", Width >= 612 ? .75f : .81f, .10f);
            if (Width >= 612) Cell("时间", .85f, .15f);
            void Cell(string text, float x, float width) => TextAt(text, Small, Palette.TextSecondary, W * x, 0, W * width, H);
        }
    }

    private sealed class TeamSummary : PaintedPanel
    {
        private LivePlayerForm[] _blue = [], _red = [];
        internal void SetTeams(LivePlayerForm[] blue, LivePlayerForm[] red) { _blue = blue; _red = red; Invalidate(); }
        protected override void Render()
        {
            Box(.5f, .5f, W - 1, H - 1, Palette.SurfaceRaised, Palette.Border);
            TextAt("对局摘要", Bold, Palette.TextPrimary, 16, 8, W - 32, 32);
            TextAt("双方整体对比", Body, Palette.TextSecondary, 16, 46, W - 32, 26);
            Comparison("近期胜率", Rate(_blue), Rate(_red), "%", 80);
            Comparison("平均 KDA", Kda(_blue), Kda(_red), "", 136);
            Comparison("平均等级", Level(_blue), Level(_red), "", 192);
            Line(242);
            TextAt("位置胜率对比", Body, Palette.TextSecondary, 16, 252, W - 32, 26);
            for (int i = 0; i < Roles.Length; i++)
                Comparison(Roles[i], Rate(_blue.Where(p => p.LayoutPosition == Roles[i]).ToArray()),
                    Rate(_red.Where(p => p.LayoutPosition == Roles[i]).ToArray()), "%", 290 + i * 44, true);
            float foot = Math.Max(512, H - 88);
            Box(12, foot, W - 24, 72, Palette.SurfaceMuted);
            TextAt("基于已获取的有效战绩统计", Small, Palette.TextSecondary, 24, foot + 9, W - 48, 24);
            TextAt("缺少数据时显示 —，仅供参考", Small, Palette.TextSecondary, 24, foot + 34, W - 48, 24);
        }
        private void Comparison(string label, double? blue, double? red, string suffix, float y, bool compact = false)
        {
            TextAt(label, Small, Palette.TextSecondary, 16, y, W - 32, 22, center: true);
            string Format(double? value) => value.HasValue ? value.Value.ToString(suffix == "%" || label == "平均等级" ? "0" : "0.00") + suffix : "—";
            TextAt(Format(blue), Bold, TeamColor(false), 16, y, W / 3 - 16, 22);
            TextAt(Format(red), Bold, TeamColor(true), W * 2 / 3, y, W / 3 - 16, 22, right: true);
            float width = W - 32, barY = y + (compact ? 27 : 30);
            Box(16, barY, width, 7, Palette.Border, radius: 3);
            if (blue.HasValue && red.HasValue && blue + red > 0)
            {
                float split = (float)(blue.Value / (blue.Value + red.Value));
                Box(16, barY, width * split, 7, TeamColor(false), radius: 2);
                Box(16 + width * split, barY, width * (1 - split), 7, TeamColor(true), radius: 2);
            }
        }
        private static double? Rate(LivePlayerForm[] players) => players.Sum(p => p.RecentSampleSize) is > 0 and var size
            ? 100d * players.Sum(p => p.RecentWins) / size : null;
        private static double? Level(LivePlayerForm[] players) => players.Where(p => p.PlayerLevel.HasValue).Select(p => (double?)p.PlayerLevel).Average();
        private static double? Kda(LivePlayerForm[] players) => players.Where(p => p.LayoutKdaValue.HasValue).Select(p => p.LayoutKdaValue).Average();
    }
}
