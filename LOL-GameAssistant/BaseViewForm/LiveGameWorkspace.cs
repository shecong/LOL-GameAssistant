using Button = AntdUI.Button;
using Label = AntdUI.Label;
using Panel = AntdUI.Panel;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Four presentations over the existing roster. Player cards remain owned by their team panels.</summary>
internal sealed partial class LiveGameWorkspace : UserControl, IThemeAware
{
    private static readonly string[] Roles = ["上路", "打野", "中路", "下路", "辅助"];
    private readonly System.Windows.Forms.Panel _canvas = new() { Dock = DockStyle.Fill, AutoScroll = true, Name = "layoutCanvas" };
    private readonly Panel _drawer = new() { Visible = false, Padding = new Padding(12), Name = "playerDetailDrawer" };
    private readonly Dictionary<LivePlayerForm, string> _manualRoles = new();
    private LivePlayerForm[] _blue = [], _red = [];
    private LivePlayerForm? _selected;
    private string? _selectedIdentity;
    private LiveGameLayout _layout;
    private TableLayoutPanel? _table;
    private PlayerDetailPane? _detail;
    private TeamSummary? _summary;
    private string _blueTitle = "蓝方", _redTitle = "红方";
    private bool _building;
    private bool _arranging;
    private readonly Font _bodyFont = new(UiMetrics.FontFamily, 9);
    private readonly Font _headingFont = new(UiMetrics.FontFamily, 11, FontStyle.Bold);

    internal LiveGameWorkspace()
    {
        DoubleBuffered = true;
        Font = _bodyFont;
        Controls.Add(_canvas);
        Controls.Add(_drawer);
        SizeChanged += (_, _) => Arrange();
        _canvas.SizeChanged += (_, _) => Arrange();
        _canvas.Layout += (_, _) => Arrange();
        Disposed += (_, _) => { foreach (var player in _blue.Concat(_red)) Unsubscribe(player); _bodyFont.Dispose(); _headingFont.Dispose(); };
    }

    internal LivePlayerForm? SelectedPlayer => _selected;
    internal LiveGameLayout LayoutMode => _layout;

    internal void ScrollToTop()
    {
        _canvas.AutoScrollPosition = Point.Empty;
        _detail?.ScrollToTop();
    }

    internal void SetLayout(LiveGameLayout layout)
    {
        if (_layout == layout) return;
        _layout = layout;
        Build();
    }

    internal void SetPlayers(LivePlayerForm[] blue, LivePlayerForm[] red, string blueTitle, string redTitle)
    {
        bool changed = !_blue.SequenceEqual(blue) || !_red.SequenceEqual(red) ||
            _blueTitle != blueTitle || _redTitle != redTitle;
        _blueTitle = blueTitle; _redTitle = redTitle;
        if (changed)
        {
            foreach (var player in _blue.Concat(_red)) Unsubscribe(player);
            _blue = blue; _red = red;
            foreach (var player in _blue.Concat(_red)) player.OverviewChanged += PlayerChanged;
            foreach (var removed in _manualRoles.Keys.Except(_blue.Concat(_red)).ToArray()) _manualRoles.Remove(removed);
            if (_selected == null || !_blue.Concat(_red).Contains(_selected))
            {
                var players = _blue.Concat(_red).ToArray();
                _selected = players.FirstOrDefault(player => _selectedIdentity != null && player.Puuid == _selectedIdentity) ?? players.FirstOrDefault();
                _selectedIdentity ??= _selected?.Puuid;
            }
            Build();
        }
        else RefreshData();
    }

    private void Unsubscribe(LivePlayerForm player) => player.OverviewChanged -= PlayerChanged;
    private void PlayerChanged(object? sender, EventArgs e) => RefreshData();

    private void Build()
    {
        if (_building || IsDisposed) return;
        _building = true;
        SuspendLayout();
        try
        {
            _drawer.Visible = false;
            ControlLifetime.ClearAndDispose(_drawer);
            ControlLifetime.ClearAndDispose(_canvas);
            _detail = null; _summary = null;
            _table = Grid(_layout == LiveGameLayout.RoleComparison ? 3 : _layout == LiveGameLayout.DetailWorkspace ? 3 : _layout == LiveGameLayout.CompactCards ? 5 : 2);
            _canvas.Controls.Add(_table);
            if (_blue.Length + _red.Length == 0)
            {
                _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _table.Controls.Add(TextLabel("等待进入大厅、选人或对局\n获取阵容后将在这里显示玩家信息"), 0, 0);
                _table.SetColumnSpan(_table.Controls[0], _table.ColumnCount);
            }
            else if (_layout == LiveGameLayout.DetailWorkspace) BuildWorkbench();
            else if (_layout == LiveGameLayout.RoleComparison) BuildRoles();
            else if (_layout == LiveGameLayout.CompactCards) BuildCards();
            else BuildOverview();
            ApplyTheme(UiTheme.Palette);
        }
        finally { _building = false; ResumeLayout(true); }
        Arrange();
        RefreshData();
    }

    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel { ColumnCount = columns, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(4), Name = "layoutGrid" };
        for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        return grid;
    }

    private TeamBanner Heading(string text, bool red) => new(red)
    {
        Text = text, Dock = DockStyle.Fill, Margin = new Padding(4),
        Players = red ? _red : _blue
    };

    private static Label TextLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(4), Padding = new Padding(8)
    };

    private PlayerTile Tile(LivePlayerForm player, bool red, bool compact = false, bool roster = false)
    {
        var tile = new PlayerTile(player, red, compact, roster, _layout == LiveGameLayout.RoleComparison)
        { Dock = DockStyle.Fill, Margin = new Padding(4, 0, 4, compact ? 8 : 1), TabStop = true };
        tile.Click += (_, _) => SelectPlayer(player);
        if (_layout == LiveGameLayout.RoleComparison)
        {
            var actions = Roles.Select(role => (role, (Action)(() => { _manualRoles[player] = role; Build(); }), true)).ToList();
            actions.Insert(0, ("手动指定位置", () => { }, false));
            actions.Add(("恢复识别位置", () => { _manualRoles.Remove(player); Build(); }, true));
            UiContextMenu.Bind(tile, actions.ToArray());
        }
        return tile;
    }

    private void BuildOverview()
    {
        _table!.RowCount = Math.Max(_blue.Length, _red.Length) + 2;
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        for (int i = 2; i < _table.RowCount; i++) _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _table.Controls.Add(Heading(_blueTitle, false), 0, 0);
        _table.Controls.Add(Heading(_redTitle, true), 1, 0);
        _table.Controls.Add(new ColumnHeader(false, false) { Dock = DockStyle.Fill }, 0, 1);
        _table.Controls.Add(new ColumnHeader(false, false) { Dock = DockStyle.Fill }, 1, 1);
        for (int i = 0; i < _blue.Length; i++) _table.Controls.Add(Tile(_blue[i], false), 0, i + 2);
        for (int i = 0; i < _red.Length; i++) _table.Controls.Add(Tile(_red[i], true), 1, i + 2);
    }

    internal string PositionFor(LivePlayerForm player) => _manualRoles.GetValueOrDefault(player, player.LayoutPosition);

    private void BuildRoles()
    {
        _table!.ColumnStyles[0].Width = 46;
        _table.ColumnStyles[1].Width = 8;
        _table.ColumnStyles[2].Width = 46;
        var left = _blue.ToList(); var right = _red.ToList();
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _table.Controls.Add(Heading(_blueTitle, false), 0, 0);
        _table.Controls.Add(TextLabel("位置"), 1, 0);
        _table.Controls.Add(Heading(_redTitle, true), 2, 0);
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _table.Controls.Add(new ColumnHeader(true, false) { Dock = DockStyle.Fill }, 0, 1);
        _table.Controls.Add(new ColumnHeader(true, true) { Dock = DockStyle.Fill }, 2, 1);
        int row = 2;
        foreach (string role in Roles)
        {
            AddPair(left.FirstOrDefault(player => PositionFor(player) == role), right.FirstOrDefault(player => PositionFor(player) == role), role);
        }
        // Never invent roles for unassigned/duplicate-role players (custom lobbies, ARAM, incomplete rosters).
        while (left.Count > 0 || right.Count > 0) AddPair(left.FirstOrDefault(), right.FirstOrDefault(), "待确认");
        void AddPair(LivePlayerForm? blue, LivePlayerForm? red, string role)
        {
            _table.RowCount = row + 1;
            _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _table.Controls.Add(blue == null ? TextLabel("暂无该位置玩家") : Tile(blue, false), 0, row);
            _table.Controls.Add(TextLabel(role), 1, row);
            _table.Controls.Add(red == null ? TextLabel("暂无该位置玩家") : Tile(red, true), 2, row);
            if (blue != null) left.Remove(blue);
            if (red != null) right.Remove(red);
            row++;
        }
    }

    private void BuildCards()
    {
        int columns = Math.Clamp((_canvas.ClientSize.Width - 16) / 156, 1, 5);
        _table!.ColumnCount = columns;
        _table.ColumnStyles.Clear();
        for (int i = 0; i < columns; i++) _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        int row = 0;
        AddTeam(_blue, false, _blueTitle);
        AddTeam(_red, true, _redTitle);
        _table.RowCount = row;
        void AddTeam(LivePlayerForm[] players, bool red, string title)
        {
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            var header = Heading(title, red);
            _table.Controls.Add(header, 0, row++);
            _table.SetColumnSpan(header, columns);
            int rows = Math.Max(1, (players.Length + columns - 1) / columns);
            for (int i = 0; i < rows; i++) _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < players.Length; i++) _table.Controls.Add(Tile(players[i], red, compact: true), i % columns, row + i / columns);
            row += rows;
        }
    }

    private void BuildWorkbench()
    {
        _table!.ColumnStyles[0].Width = 22;
        _table.ColumnStyles[1].Width = 54;
        _table.ColumnStyles[2].Width = 24;
        _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var roster = Grid(1);
        roster.Dock = DockStyle.Fill;
        roster.RowCount = _blue.Length + _red.Length + 2;
        int row = 0;
        Add(_blue, false, _blueTitle); Add(_red, true, _redTitle);
        _table.Controls.Add(roster, 0, 0);
        _detail = new PlayerDetailPane { Dock = DockStyle.Fill, Margin = new Padding(8, 4, 8, 4) };
        _table.Controls.Add(_detail, 1, 0);
        _summary = new TeamSummary { Dock = DockStyle.Fill, Margin = new Padding(4) };
        _table.Controls.Add(_summary, 2, 0);
        void Add(LivePlayerForm[] players, bool red, string title)
        {
            roster.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            roster.Controls.Add(Heading(title, red), 0, row++);
            foreach (var player in players)
            {
                roster.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                roster.Controls.Add(Tile(player, red, roster: true), 0, row++);
            }
        }
    }

    internal void SelectPlayer(LivePlayerForm player)
    {
        if (!_blue.Concat(_red).Contains(player)) return;
        _selected = player;
        _selectedIdentity = player.Puuid;
        if (_layout != LiveGameLayout.DetailWorkspace)
        {
            if (_detail == null || _detail.IsDisposed)
            {
                _detail = new PlayerDetailPane { Dock = DockStyle.Fill };
                var close = new Button { Text = "关闭详情 ×", Dock = DockStyle.Top, Height = 34 };
                close.Click += (_, _) => _drawer.Visible = false;
                _drawer.Controls.Add(_detail); _drawer.Controls.Add(close);
            }
            _drawer.Visible = true;
            _drawer.BringToFront();
        }
        RefreshData(); Arrange();
    }

    private void RefreshData()
    {
        if (_building || IsDisposed) return;
        if (_table != null)
        {
            foreach (var tile in Descendants(_table).OfType<PlayerTile>())
            {
                tile.Selected = tile.Player == _selected;
                tile.RefreshData();
            }
            foreach (var banner in Descendants(_table).OfType<TeamBanner>()) banner.Invalidate();
        }
        if (_selected is { IsDisposed: false }) _detail?.SetPlayer(_selected);
        _summary?.SetTeams(_blue, _red);
    }

    private void Arrange()
    {
        if (_building || _arranging || _table == null || IsDisposed) return;
        if (_layout == LiveGameLayout.CompactCards && _table.ColumnCount != Math.Clamp((_canvas.ClientSize.Width - 16) / 156, 1, 5)) { Build(); return; }
        int minHeight = _layout switch
        {
            LiveGameLayout.RoleComparison => 104 + Math.Max(1, _table.RowCount - 2) * 90,
            LiveGameLayout.DetailWorkspace => 42 * 2 + Math.Max(_blue.Length + _red.Length, 1) * 54,
            LiveGameLayout.CompactCards => 112 + Math.Max(1, _table.RowCount - 2) * 238,
            _ => 104 + Math.Max(Math.Max(_blue.Length, _red.Length), 1) * 90
        };
        _arranging = true;
        try
        {
            _table.Height = Math.Max(_canvas.ClientSize.Height, minHeight);
            MatchListScrolling.LayoutRows(_canvas, [_table], 0, 0);
            _table.Height = Math.Max(_canvas.ClientSize.Height, minHeight);
            MatchListScrolling.LayoutRows(_canvas, [_table], 0, 0);
        }
        finally { _arranging = false; }
        int width = Math.Min(ClientSize.Width - 12, Math.Clamp(ClientSize.Width / 2, 360, 560));
        _drawer.SetBounds(Math.Max(0, ClientSize.Width - width), 0, Math.Max(1, width), ClientSize.Height);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _canvas.BackColor = palette.Surface;
        _drawer.BackColor = palette.SurfaceRaised;
        foreach (var label in Descendants(this).OfType<Label>())
        {
            label.BackColor = label.Tag is "redHeading" ? palette.RedHeader : label.Tag is "blueHeading" ? palette.BlueHeader : palette.SurfaceRaised;
            label.ForeColor = label.Tag is "redHeading" or "blueHeading" ? Color.White : palette.TextPrimary;
        }
        foreach (var tile in Descendants(this).OfType<PlayerTile>()) tile.ApplyTheme(palette);
        foreach (var visual in Descendants(this).OfType<PaintedPanel>()) visual.ApplyTheme(palette);
        _detail?.ApplyTheme(palette);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

}
