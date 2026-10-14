using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LiveGameForm
{
    private readonly AntdUI.Label _teamOverview1 = CreateOverviewLabel();
    private readonly AntdUI.Label _teamOverview2 = CreateOverviewLabel();
    private readonly AntdUI.Label _gameSubtitle = CreateOverviewLabel();
    private string _modeDescription = "双方阵容";

    private static AntdUI.Label CreateOverviewLabel() => new() {
        AutoSize = false, BackColor = Color.Transparent,
        Font = new Font(UiMetrics.FontFamily, 9F), ForeColor = UiTheme.Palette.TextSecondary
    };

    private void ConfigureReferenceOverview()
    {
        infoBar.Controls.Add(_gameSubtitle);
        _gameSubtitle.SetBounds(32, 44, 660, 24);
        lblGameInfo.Padding = new Padding(30, 0, 0, 28);
        void Configure(GradientPanel header, AntdUI.Label title, AntdUI.Label overview, Color color)
        {
            header.Controls.Add(overview);
            title.Dock = DockStyle.None;
            var crest = new TeamCrest(color);
            header.Controls.Add(crest);
            crest.SetBounds(16, 10, 44, 52);
            void Layout()
            {
                title.SetBounds(70, 8, Math.Max(100, header.Width - 220), 30);
                overview.SetBounds(70, 38, Math.Max(100, header.Width - 160), 24);
            }
            header.Resize += (_, _) => Layout();
            Layout();
        }
        Configure(headerTeam1, lblTeamTitle1, _teamOverview1, Color.FromArgb(74, 180, 255));
        Configure(headerTeam2, lblTeamTitle2, _teamOverview2, Color.FromArgb(255, 149, 162));
        panelTeam1.ControlAdded += (_, _) => UpdateTeamOverview();
        panelTeam2.ControlAdded += (_, _) => UpdateTeamOverview();
        panelTeam1.ControlRemoved += (_, _) => UpdateTeamOverview();
        panelTeam2.ControlRemoved += (_, _) => UpdateTeamOverview();
        UpdateTeamOverview();
    }

    internal static string DescribeTeam(IEnumerable<LivePlayerForm> players)
    {
        var cards = players.ToArray();
        var levels = cards.Where(c => c.PlayerLevel.HasValue).Select(c => c.PlayerLevel!.Value).ToArray();
        int samples = cards.Sum(c => c.RecentSampleSize);
        string level = levels.Length == 0 ? "—" : $"{levels.Average():0}";
        string rate = samples == 0 ? "—" : $"{100.0 * cards.Sum(c => c.RecentWins) / samples:0}%";
        return $"平均等级  {level}   |   近期胜率  {rate}";
    }

    private void UpdateTeamOverview()
    {
        _teamOverview1.Text = DescribeTeam(panelTeam1.Controls.OfType<LivePlayerForm>());
        _teamOverview2.Text = DescribeTeam(panelTeam2.Controls.OfType<LivePlayerForm>());
        int count = panelTeam1.Controls.OfType<LivePlayerForm>().Count() + panelTeam2.Controls.OfType<LivePlayerForm>().Count();
        _gameSubtitle.Text = count == 0 ? "等待进入大厅、选人或对局" : $"{_modeDescription}  |  {panelTeam1.Controls.OfType<LivePlayerForm>().Count()}v{panelTeam2.Controls.OfType<LivePlayerForm>().Count()}  |  {count} 名玩家  |  每人最近 10 场战绩";
        _teamOverview1.ForeColor = _teamOverview2.ForeColor = _gameSubtitle.ForeColor = UiTheme.Palette.TextSecondary;
    }

    private sealed class TeamCrest : Control
    {
        private readonly Color _color;
        internal TeamCrest(Color color)
        {
            _color = color;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.ScaleTransform(Width / 44F, Height / 52F);
            using var brush = new SolidBrush(_color);
            e.Graphics.FillPolygon(brush, [new(22, 8), new(29, 22), new(27, 36), new(22, 45), new(17, 36), new(15, 22)]);
            e.Graphics.FillPolygon(brush, [new(2, 10), new(14, 22), new(14, 32), new(6, 25)]);
            e.Graphics.FillPolygon(brush, [new(42, 10), new(30, 22), new(30, 32), new(38, 25)]);
            e.Graphics.FillPolygon(brush, [new(10, 5), new(18, 15), new(14, 19)]);
            e.Graphics.FillPolygon(brush, [new(34, 5), new(26, 15), new(30, 19)]);
        }
    }
}
