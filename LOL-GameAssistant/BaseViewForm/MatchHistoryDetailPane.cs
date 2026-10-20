using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>一局的紧凑详情。只使用已选中的领域详情，不自行查询战绩或开黑关系。</summary>
internal sealed class MatchHistoryDetailPane : UserControl, IThemeAware
{
    private readonly IGameAssetService _assets;
    private readonly IChampionCatalog _champions;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly AntdUI.Label _state = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly AntdUI.Label _title = new() { Text = "对局详情", Dock = DockStyle.Fill };
    private readonly AntdUI.Button _close = new() { Text = "收起", Dock = DockStyle.Right, Width = 64 };
    private readonly AntdUI.Button _expand = new() { Text = "完整详情", Dock = DockStyle.Right, Width = 100 };
    private readonly AntdUI.Button _retry = new() { Text = "重试", Dock = DockStyle.Bottom, Height = 40, Visible = false };
    private readonly FlowLayoutPanel _actions = new() { Dock = DockStyle.Bottom, Height = 48, WrapContents = false, Padding = new Padding(6) };
    private readonly AntdUI.Segmented _tabs = new() { Dock = DockStyle.Top, Height = 38 };
    private readonly ToolTip _tips = new();
    private readonly List<DataGridView> _grids = [];
    private MatchRowImages? _images;
    private TableLayoutPanel? _body;
    private FlowLayoutPanel? _items;
    private AntdUI.Label? _metrics;
    private PictureBox? _heroImage;
    private readonly List<AntdUI.Label> _metricTiles = [];
    private int _itemsRow;
    private long _version;
    internal MatchDetail? Detail { get; private set; }
    internal string ViewerPuuid { get; private set; } = "";
    internal string StateText => _state.Text ?? "";
    internal event Action? CloseRequested;
    internal event Action? RetryRequested;
    internal event Action<MatchDetail, string>? FullDetailRequested;
    internal event Action<string, string>? PlayerRequested;

    internal MatchHistoryDetailPane(IGameAssetService assets, IChampionCatalog champions)
    {
        _assets = assets; _champions = champions; MinimumSize = new(240, 0);
        var header = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(12, 2, 4, 2) };
        header.Controls.Add(_title); header.Controls.Add(_expand); header.Controls.Add(_close);
        Controls.Add(_content); Controls.Add(_actions); Controls.Add(header);
        _content.Controls.Add(_state); _content.Controls.Add(_retry);
        _tabs.Items.Add(new AntdUI.SegmentedItem { Text = "对局概览" });
        _tabs.Items.Add(new AntdUI.SegmentedItem { Text = "数据分析" });
        _tabs.SelectIndexChanged += (_, _) => UpdateMetricText();
        _close.Click += (_, _) => CloseRequested?.Invoke();
        _retry.Click += (_, _) => RetryRequested?.Invoke();
        _expand.Click += (_, _) => { if (Detail is { } detail) FullDetailRequested?.Invoke(detail, ViewerPuuid); };
        _content.SizeChanged += (_, _) => ResizeBody();
        ApplyTheme(UiTheme.Palette);
    }

    private void Reset()
    {
        _version++; Detail = null; _expand.Enabled = false; _retry.Visible = false;
        if (_tabs.Parent != null) _tabs.Parent.Controls.Remove(_tabs);
        // Dispose UI references before disposing their image storage.
        if (_body != null) { _content.Controls.Remove(_body); _body.Dispose(); _body = null; }
        _grids.Clear(); ControlLifetime.ClearAndDispose(_actions);
        _content.AutoScrollPosition = Point.Empty; _content.AutoScrollMinSize = Size.Empty;
        _images?.Dispose(); _images = null;
        _items = null; _metrics = null; _heroImage = null; _metricTiles.Clear();
        if (_tabs.Parent != null) _tabs.Parent.Controls.Remove(_tabs);
        _state.Visible = true; _state.BringToFront();
    }
    internal void SetLoading(MatchHistoryGame game, string puuid)
    {
        Reset(); ViewerPuuid = puuid; _title.Text = "对局详情 · 加载中";
        _state.Text = $"正在读取本局详情…\n{game.GetModeText()}\n仅加载当前选中的对局";
    }
    internal void SetError(string message)
    {
        Reset(); _title.Text = "对局详情 · 暂不可用"; _state.Text = $"本局详情加载失败\n{message}\n列表仍可正常浏览，或选择其他对局。";
        _retry.Visible = true; _retry.BringToFront();
    }
    internal void SetDetail(MatchDetail detail, string puuid)
    {
        // Tabs are reused, so detach before disposing the previous body.
        if (_tabs.Parent != null) _tabs.Parent.Controls.Remove(_tabs);
        Reset(); Detail = detail; ViewerPuuid = puuid; _expand.Enabled = true;
        _title.Text = "对局详情 · 已加载"; _state.Visible = false;
        _images = new(_assets, UpdateImages);
        var mine = detail.GetParticipant(puuid); var stats = mine?.stats;
        string result = (detail.endOfGameResult.StartsWith("Abort", StringComparison.OrdinalIgnoreCase) || detail.gameDuration is > 0 and < 300)
            ? "重开" : stats is not { HasWinResult: true } ? "胜负未提供" : stats.Win ? "胜利" : "失败";
        _body = new TableLayoutPanel { AutoSize = false, ColumnCount = 1, Padding = new Padding(10, 4, 10, 12), BackColor = UiTheme.Palette.SurfaceRaised };
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = Label($"{result}  ·  {(mine == null ? "英雄未知" : _champions.GetDisplayName(mine.championId))}  ·  {(stats?.HasKdaResult == true ? mine.GetKdaText() : "—")}\n{detail.GetModeText()} · {detail.GetDurationText()} · {detail.PlatformId}", 70);
        heading.Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold);
        var hero = new Panel { Height = 74, Padding = new Padding(0, 6, 0, 6) };
        _heroImage = new PictureBox { Dock = DockStyle.Left, Width = 60, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 10, 0) };
        heading.Dock = DockStyle.Fill; hero.Controls.Add(heading); hero.Controls.Add(_heroImage);
        var metricStrip = new TableLayoutPanel { Height = 62, ColumnCount = 4, RowCount = 1, BackColor = UiTheme.Palette.SurfaceMuted };
        for (int i = 0; i < 4; i++)
        {
            metricStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var tile = Label("—", 58); tile.Dock = DockStyle.Fill; tile.TextAlign = ContentAlignment.MiddleCenter;
            _metricTiles.Add(tile); metricStrip.Controls.Add(tile, i, 0);
        }
        _metrics = heading;
        AddBody(hero); AddBody(metricStrip); AddBody(_tabs); _tabs.SelectIndex = 0;
        foreach (var team in detail.participants.GroupBy(p => p.teamId).OrderBy(t => t.Key == mine?.teamId ? 0 : 1).ThenBy(t => t.Key))
        {
            bool own = team.Key == mine?.teamId;
            string title = detail.participants.Select(p => p.teamId).Distinct().Count() <= 2 ? own ? "我方" : "敌方" : $"队伍 {team.Key}";
            var label = Label(title + $" · {team.Count()} 人", 28);
            label.Tag = own;
            label.ForeColor = own ? UiTheme.Palette.Accent : Color.FromArgb(220, 106, 123);
            AddBody(label);
            var grid = CreateTeam(team.ToArray(), own); _grids.Add(grid); AddBody(grid);
        }
        AddBody(Label("出装与符文", 28));
        _items = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 46, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        if (stats != null)
        {
            foreach (int id in new[] { stats.item0, stats.item1, stats.item2, stats.item3, stats.item4, stats.item5, stats.item6 }.Where(i => i > 0)) AddIcon("item", id);
            foreach (int id in stats.RuneIds.Take(6)) AddIcon("rune", id);
        }
        _itemsRow = _body.RowCount; AddBody(_items);
        if (stats?.AugmentIds.Count > 0)
        {
            var augments = Label("强化信息加载中…", 56); AddBody(augments);
            _ = LoadAugmentsAsync(augments, stats.AugmentIds, _version);
        }
        _content.Controls.Add(_body); _body.BringToFront();
        if (!detail.IsCrossRegion) _actions.Controls.Add(new MatchReplayActions(detail.GameId) { Margin = Padding.Empty });
        _actions.Controls.Add(new AntdUI.Label { Text = detail.IsCrossRegion ? "跨区回放不可用" : "仅加载当前选中的对局", Width = 200, Height = 32 });
        UpdateMetricText(); ResizeBody(); UiTheme.Apply(this);
        foreach (var player in detail.participants) _ = _images.LoadAsync("champion", player.championId);
    }
    private void AddBody(Control control)
    {
        int row = _body!.RowCount++; _body.RowStyles.Add(new RowStyle(SizeType.Absolute, control.Height));
        control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 0, 0, 4); _body.Controls.Add(control, 0, row);
    }
    private static AntdUI.Label Label(string text, int height) => new() { Text = text, Height = height, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Palette.TextPrimary };
    private DataGridView CreateTeam(IReadOnlyList<MatchParticipant> players, bool own)
    {
        var grid = new DataGridView
        {
            Height = 28 + players.Count * 33, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToResizeRows = false,
            ScrollBars = ScrollBars.None, RowTemplate = { Height = 33 }, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28, EnableHeadersVisualStyles = false, Tag = own,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
        };
        grid.Columns.Add(new DataGridViewImageColumn { HeaderText = "", Width = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ImageLayout = DataGridViewImageCellLayout.Zoom, DefaultCellStyle = new() { NullValue = null, Padding = new Padding(2) } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "玩家", FillWeight = 48, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "K / D / A", FillWeight = 25, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "伤害", FillWeight = 27, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var p in players)
        {
            var identity = Detail!.participantIdentities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
            string name = identity?.GameName.Length > 0 ? identity.GameName : identity?.SummonerName ?? "未知玩家";
            int index = grid.Rows.Add(null!, (identity?.Puuid == ViewerPuuid ? "我 · " : "") + name,
                p.stats is { HasKdaResult: true } ? p.GetKdaText() : "—", p.stats is { HasDamageResult: true } s ? s.totalDamageDealtToChampions.ToString("N0") : "—");
            grid.Rows[index].Tag = p;
            grid.Rows[index].Cells[1].ToolTipText = identity == null ? "身份未提供" : $"{identity.GameName}#{identity.TagLine}\n{identity.Puuid}";
        }
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || Detail == null) return;
            var p = (MatchParticipant)grid.Rows[e.RowIndex].Tag!;
            string? id = Detail.participantIdentities.FirstOrDefault(i => i.participantId == p.participantId)?.player?.Puuid;
            if (!string.IsNullOrWhiteSpace(id)) PlayerRequested?.Invoke(id, Detail.PlatformId);
        };
        grid.SelectionChanged += (_, _) => { if (!grid.Focused && grid.SelectedRows.Count > 0) grid.ClearSelection(); };
        int maximumDamage = Math.Max(1, players.Max(p => p.stats?.totalDamageDealtToChampions ?? 0));
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3 || grid.Rows[e.RowIndex].Tag is not MatchParticipant p || p.stats?.HasDamageResult != true) return;
            e.PaintBackground(e.CellBounds, false);
            int bar = Math.Clamp((int)((e.CellBounds.Width - 8) * (double)p.stats.totalDamageDealtToChampions / maximumDamage), 0, e.CellBounds.Width - 8);
            using var brush = new SolidBrush(Color.FromArgb(45, own ? UiTheme.Palette.Accent : Color.FromArgb(220, 100, 115)));
            e.Graphics!.FillRectangle(brush, e.CellBounds.X + 3, e.CellBounds.Y + 4, bar, e.CellBounds.Height - 8);
            e.PaintContent(e.CellBounds); e.Handled = true;
        };
        grid.ClearSelection(); return grid;
    }
    private void UpdateMetricText()
    {
        var mine = Detail?.GetParticipant(ViewerPuuid); var s = mine?.stats;
        if (_metrics == null || s == null || _metricTiles.Count != 4) return;
        string[] text = _tabs.SelectIndex == 1
            ? [$"金币\n{s.goldEarned:N0}", $"承伤\n{s.totalDamageTaken:N0}", $"治疗\n{s.totalHeal:N0}", $"五杀\n{s.pentaKills}"]
            : [$"KDA\n{(s.HasKdaResult ? mine.GetKdaRatio().ToString("0.00") : "—")}", $"伤害\n{(s.HasDamageResult ? s.totalDamageDealtToChampions.ToString("N0") : "—")}",
                $"补刀\n{(s.HasFarmResult ? (s.totalMinionsKilled + s.neutralMinionsKilled).ToString() : "—")}", $"视野\n{(s.HasVisionResult ? s.visionScore.ToString() : "—")}"];
        for (int i = 0; i < text.Length; i++) _metricTiles[i].Text = text[i];
    }
    private void AddIcon(string kind, int id)
    {
        var box = new PictureBox { Size = new(30, 30), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 5, 0), Tag = (kind, id) };
        _tips.SetToolTip(box, (kind == "item" ? "装备 " : "符文 ") + id); _items!.Controls.Add(box); _ = _images!.LoadAsync(kind, id);
    }
    private void UpdateImages()
    {
        if (IsDisposed || _images == null) return;
        foreach (var grid in _grids)
            foreach (DataGridViewRow row in grid.Rows)
                if (row.Tag is MatchParticipant p) row.Cells[0].Value = _images.Get("champion", p.championId);
        if (_items != null)
            foreach (PictureBox box in _items.Controls) if (box.Tag is ValueTuple<string, int> key) box.Image = _images.Get(key.Item1, key.Item2);
        if (_heroImage != null && Detail?.GetParticipant(ViewerPuuid) is { } mine) _heroImage.Image = _images.Get("champion", mine.championId);
    }
    private async Task LoadAugmentsAsync(AntdUI.Label label, IReadOnlyList<int> ids, long version)
    {
        try
        {
            var names = await LOL_GameAssistant.Infrastructure.GameData.AugmentCatalog.ResolveAsync(ids);
            if (!IsDisposed && !label.IsDisposed && version == _version) label.Text = "强化：" + string.Join(" / ", names.Select(a => a.Name));
        }
        catch { if (!label.IsDisposed && version == _version) label.Text = "强化信息暂不可用"; }
    }
    private void ResizeBody()
    {
        if (_body == null) return;
        if (_items != null && _itemsRow < _body.RowStyles.Count)
        {
            int columns = Math.Max(1, (_content.ClientSize.Width - 20) / 35);
            _body.RowStyles[_itemsRow].Height = Math.Max(42, ((_items.Controls.Count + columns - 1) / columns) * 36 + 10);
        }
        _body.Size = new Size(Math.Max(220, _content.ClientSize.Width),
            _body.Padding.Vertical + (int)_body.RowStyles.Cast<RowStyle>().Sum(r => r.Height));
    }
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _content.BackColor = palette.SurfaceRaised;
        ForeColor = palette.TextPrimary; _title.ForeColor = _state.ForeColor = palette.TextPrimary;
        var scheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        _title.ColorScheme = _state.ColorScheme = _close.ColorScheme = _expand.ColorScheme = _retry.ColorScheme = _tabs.ColorScheme = scheme;
        _tabs.BackColor = palette.SurfaceRaised;
        _actions.BackColor = palette.SurfaceRaised;
        if (_body != null)
        {
            _body.BackColor = palette.SurfaceRaised;
            foreach (var label in _body.Controls.OfType<AntdUI.Label>())
            {
                label.ColorScheme = scheme;
                label.ForeColor = label.Tag is bool own ? own ? palette.Accent : Color.FromArgb(220, 106, 123) : palette.TextPrimary;
            }
            foreach (var tile in _metricTiles) { tile.ColorScheme = scheme; tile.ForeColor = palette.TextPrimary; }
            if (_metrics != null) { _metrics.ColorScheme = scheme; _metrics.ForeColor = palette.TextPrimary; }
        }
        foreach (var label in _actions.Controls.OfType<AntdUI.Label>()) { label.ColorScheme = scheme; label.ForeColor = palette.TextSecondary; }
        foreach (var grid in _grids)
        {
            grid.BackgroundColor = palette.SurfaceRaised; grid.GridColor = palette.Border;
            grid.DefaultCellStyle.BackColor = palette.SurfaceRaised; grid.DefaultCellStyle.ForeColor = palette.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = palette.Accent; grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceMuted; grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextSecondary;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _images?.Dispose(); _tips.Dispose(); if (_tabs.Parent == null) _tabs.Dispose(); }
        base.Dispose(disposing);
    }
}
