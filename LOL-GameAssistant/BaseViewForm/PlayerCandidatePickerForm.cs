using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>不预选任何候选，防止按 Enter 或双击空白时误查第一个同名账号。</summary>
internal sealed class PlayerCandidatePickerForm : AntdUI.Window, IThemeAware
{
    private readonly AntdUI.Table _players = new()
    {
        Dock = DockStyle.Fill, MultipleRows = false, LostFocusClearSelection = false,
        Bordered = true, Radius = 8, EnableFocusNavigation = true
    };
    private readonly AntdUI.Label _scope = new() { Dock = DockStyle.Fill, TextMultiLine = true };
    internal sealed record CandidateRow(string Name, string Region, string Level, string Source, string Puuid, PlayerSearchCandidate Candidate);
    private readonly AntdUI.PageHeader _header = new()
    {
        Dock = DockStyle.Top, Height = 38, ShowButton = true, MinimizeBox = false,
        UseSystemStyleColor = false, UseForeColorDrawIcons = true
    };
    private readonly AntdUI.Button _confirm = new() { Text = "查询选中玩家", Width = 140, Height = 40, Enabled = false, Type = AntdUI.TTypeMini.Primary };
    private readonly AntdUI.Button _cancel = new() { Text = "取消", Width = 90, Height = 40 };
    internal PlayerSearchCandidate? SelectedCandidate { get; private set; }

    internal PlayerCandidatePickerForm(PlayerNameSearchResult result)
    {
        Text = $"选择玩家 · {result.Candidates.Count} 个候选";
        ClientSize = new Size(950, 510); MinimumSize = new Size(640, 380); StartPosition = FormStartPosition.CenterParent;
        WindowLayout.ConfigureDpi(this); AntdWindowChrome.Configure(this);
        _header.Text = Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(18, 8, 18, 12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _scope.Text = $"找到 {result.Candidates.Count} 个匹配玩家，请核对名称#TAG、大区及 PUUID 后选择。\n{result.ScopeMessage}";
        layout.Controls.Add(_scope, 0, 0); layout.Controls.Add(_players, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(_cancel); buttons.Controls.Add(_confirm); layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
        Controls.Add(_header);
        foreach (var (key, title, width) in new[] { ("Name", "名称#TAG", "25%"), ("Region", "大区", "13%"), ("Level", "等级", "10%"), ("Source", "来源", "20%"), ("Puuid", "PUUID", "32%") })
            _players.Columns.Add(new AntdUI.Column(key, title) { Width = width, LineBreak = true });
        _players.DataSource = result.Candidates.Select(candidate => new CandidateRow(candidate.RiotId,
            candidate.Platform.Length > 0 ? candidate.Platform : "当前大区", candidate.Level?.ToString() ?? "未提供",
            candidate.Source, candidate.Puuid, candidate)).ToArray();
        _players.SelectIndexChanged += (_, _) => _confirm.Enabled = _players.SelectedsReal().OfType<CandidateRow>().Count() == 1;
        _players.CellDoubleClick += (_, e) => { if (e.Record is CandidateRow) Confirm(); };
        _confirm.Click += (_, _) => Confirm();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = _cancel;
        Shown += (_, _) => { _players.SelectedIndexs = []; _confirm.Enabled = false; };
        UiTheme.Apply(this);
    }

    private void Confirm()
    {
        var selected = _players.SelectedsReal().OfType<CandidateRow>().ToArray();
        if (selected.Length != 1) return;
        SelectedCandidate = selected[0].Candidate; DialogResult = DialogResult.OK; Close();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        _header.BackColor = palette.Surface;
        _header.ForeColor = palette.TextPrimary;
        _header.ColorScheme = _confirm.ColorScheme = _cancel.ColorScheme = _scope.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        _scope.ForeColor = palette.TextPrimary;
        AntdContentTheme.Apply(this, palette);
        _players.BackColor = palette.SurfaceRaised;
        _players.ForeColor = palette.TextPrimary;
        _players.BorderColor = palette.Border;
    }
}
