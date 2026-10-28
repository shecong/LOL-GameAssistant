using Label = AntdUI.Label;
using Panel = AntdUI.Panel;
using ComboBox = AntdUI.Select;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class EnemyJungleRouteForm : AntdUI.Window, IThemeAware
{
    private readonly EnemyJungleRouteController _controller;
    private readonly AntdUI.PageHeader _header;
    private readonly JungleRouteMap _map = new() { Dock = DockStyle.Fill, Name = "jungleRouteMap" };
    private readonly ComboBox _enemy = new() { Name = "jungleEnemy", List = true, DropDownArrow = true, Width = 240, Height = UiMetrics.ControlHeight };
    private readonly ComboBox _sample = new() { Name = "jungleSample", List = true, DropDownArrow = true, Dock = DockStyle.Top, Height = UiMetrics.ControlHeight };
    private readonly Label _status = new() { Name = "jungleRouteStatus", TextMultiLine = true, TextAlign = ContentAlignment.TopLeft, Dock = DockStyle.Top, Height = 68, Padding = new Padding(12, 8, 12, 4) };
    private readonly Label _explanation = new() { TextMultiLine = true, TextAlign = ContentAlignment.TopLeft, Dock = DockStyle.Top, Height = 160, Padding = new Padding(8), AutoEllipsis = true };
    private readonly AntdUI.Table _events = new() { Name = "jungleRouteEvidence", Dock = DockStyle.Fill, Bordered = true, Radius = 8, EmptyHeader = true };
    internal sealed record EvidenceRow(string Time, string Region, string Evidence);
    private bool _updating;
    private string _renderedContext = "";

    internal EnemyJungleRouteForm(EnemyJungleRouteController controller)
    {
        _controller = controller;
        Text = "敌方打野 · 历史路线分析";
        Size = new Size(1120, 780);
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font(UiMetrics.FontFamily, 9F);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12) };
        body.ColumnStyles.Add(new(SizeType.Percent, 64));
        body.ColumnStyles.Add(new(SizeType.Percent, 36));
        body.RowStyles.Add(new(SizeType.Percent, 100));
        body.Controls.Add(_map, 0, 0);
        var side = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        side.Controls.Add(_events);
        side.Controls.Add(_explanation);
        side.Controls.Add(_sample);
        body.Controls.Add(side, 1, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 8, 12, 0) };
        toolbar.Controls.Add(new Label { Text = "敌方打野", Width = 70, Height = UiMetrics.ControlHeight });
        toolbar.Controls.Add(_enemy);
        var refresh = new AntdUI.Button { Name = "refreshJungleRoute", Text = "重新分析", AutoSize = true };
        refresh.Click += async (_, _) => { refresh.Enabled = false; try { await controller.RefreshAsync(true); } finally { if (!IsDisposed) refresh.Enabled = true; } };
        toolbar.Controls.Add(refresh);
        var settings = new AntdUI.Button { Text = "AI 与推荐设置", AutoSize = true };
        settings.Click += (_, _) => Program.GameMain?.OpenAiRecommendationSettings();
        toolbar.Controls.Add(settings);
        Controls.Add(body);
        Controls.Add(_status);
        Controls.Add(toolbar);
        _header = AntdWindowChrome.AddHeader(this);
        _events.Columns.Add(new AntdUI.Column(nameof(EvidenceRow.Time), "时间") { Width = "18%" });
        _events.Columns.Add(new AntdUI.Column(nameof(EvidenceRow.Region), "区域") { Width = "34%", LineBreak = true });
        _events.Columns.Add(new AntdUI.Column(nameof(EvidenceRow.Evidence), "历史依据") { Width = "48%", LineBreak = true });
        _enemy.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating) controller.SelectEnemy(_enemy.SelectedIndex <= 0 ? null : controller.Current.Enemies.ElementAtOrDefault(_enemy.SelectedIndex - 1)?.Puuid);
        };
        _sample.SelectedIndexChanged += (_, _) => { if (!_updating) RenderRoute(); };
        controller.Changed += UpdateAnalysis;
        Disposed += (_, _) => controller.Changed -= UpdateAnalysis;
        UiTheme.Apply(this);
        UpdateAnalysis();
    }

    private void UpdateAnalysis()
    {
        if (IsDisposed) return;
        var analysis = _controller.Current;
        _updating = true;
        try
        {
            string? selected = _enemy.SelectedIndex > 0 && _enemy.SelectedIndex <= _enemy.Items.Count - 1 ? _enemy.SelectedValue?.ToString() : null;
            var names = new[] { "自动识别（客户端分路）" }.Concat(analysis.Enemies.Select(p =>
                $"{p.Name} · {Bootstrap.AppCompositionRoot.ChampionCatalog.GetDisplayName(p.ChampionId)}")).ToArray();
            if (!_enemy.Items.Cast<string>().SequenceEqual(names)) { _enemy.Items.Clear(); _enemy.Items.AddRange(names); }
            _enemy.SelectedIndex = selected != null && Array.IndexOf(names, selected) is int index && index >= 0 ? index : 0;
            _status.Text = (analysis.Enemy == null ? "" : $"{analysis.Enemy.Name} · {(analysis.TeamId == 100 ? "蓝方" : "红方")}打野\n") + analysis.Status;
            int keepSample = _renderedContext == analysis.ContextKey ? Math.Max(0, _sample.SelectedIndex) : 0;
            _renderedContext = analysis.ContextKey;
            _sample.Items.Clear();
            _sample.Items.Add("预测路线 · 本局阵营方向");
            foreach (var sample in analysis.Samples)
                _sample.Items.Add($"历史 {sample.GameId} · {(sample.CreatedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(sample.CreatedAt).ToLocalTime().ToString("MM-dd HH:mm") : "时间未知")} · {(sample.TeamId == 100 ? "蓝" : "红")}方");
            _sample.SelectedIndex = Math.Min(keepSample, _sample.Items.Count - 1);
        }
        finally { _updating = false; }
        RenderRoute();
    }

    private void RenderRoute()
    {
        var analysis = _controller.Current;
        int sampleIndex = _sample.SelectedIndex - 1;
        var sample = sampleIndex >= 0 ? analysis.Samples.ElementAtOrDefault(sampleIndex) : null;
        var points = sample == null ? analysis.Prediction?.Points ?? [] : JungleRoutePredictor.Normalize(sample, analysis.TeamId);
        _map.SetRoute(points, predicted: sample == null);
        var prediction = analysis.Prediction;
        _explanation.Text = sample != null ? $"历史对局 {sample.GameId}\n已映射到本局敌方阵营。下表为历史采样及事件依据。\n连线仅连接离散位置，不是精确行走轨迹。"
            : prediction == null ? "暂无可靠路线。可确认敌方打野后重试。\n仅使用已结束对局的时间线，不展示本局隐藏位置。"
            : $"常见起始采样区：{prediction.OpeningRegion}\n历史起始区域一致：{prediction.SupportingGames}/{prediction.TotalGames} 场 · {(prediction.SameChampion ? "同英雄样本" : "跨英雄参考")}\n{prediction.ActivityHint}\n代表路线来源：对局 {prediction.RepresentativeGameId}\n另有 {analysis.UnavailableGames} 场时间线缺失或采样不足。\n虚线为历史习惯推测，不代表本局实时位置或营地顺序。";
            var entries = points.Select(point => (point.Seconds, Region: JungleRoutePredictor.Region(point.Position), Evidence: point.Evidence)).ToList();
            // 事件地点与角色站位分开列出，远程击杀/助攻不能用于绘制精确路线。
            foreach (var ev in sample?.Events ?? [])
            {
                var position = ev.Position == null || sample!.TeamId == analysis.TeamId ? ev.Position : ev.Position.Mirror();
                entries.Add((ev.Seconds, position == null ? "位置未知" : JungleRoutePredictor.Region(position), ev.Description));
            }
            _events.DataSource = entries.OrderBy(e => e.Seconds)
                .Select(entry => new EvidenceRow($"{entry.Seconds / 60}:{entry.Seconds % 60:D2}", entry.Region, entry.Evidence)).ToArray();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette);
        BackColor = palette.Surface;
        _status.BackColor = palette.Surface;
        _explanation.BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _status.ForeColor = palette.TextPrimary;
        _explanation.ForeColor = palette.TextPrimary;
        _header.ForeColor = palette.TextPrimary;
        _header.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        _events.BackColor = palette.SurfaceRaised;
        _enemy.BackColor = palette.SurfaceRaised;
        _sample.BackColor = palette.SurfaceRaised;
        _events.ForeColor = palette.TextPrimary;
        _enemy.ForeColor = palette.TextPrimary;
        _sample.ForeColor = palette.TextPrimary;
        _map.Invalidate();
    }
}
