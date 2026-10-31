using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed class TrafficMonitorForm : UserControl, IThemeAware
{
    private readonly NetworkTrafficService _traffic;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeight = 38, RowTemplate = { Height = 48 }, EnableHeadersVisualStyles = false
    };
    private readonly AntdUI.Label _summary = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 0, 8, 0) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Dictionary<NetworkModule, (long Up, long Down)> _previous = [];
    private long _lastTick = Environment.TickCount64;
    private bool _refreshing;

    public TrafficMonitorForm() : this(NetworkTrafficService.Shared) { }
    public TrafficMonitorForm(NetworkTrafficService traffic)
    {
        _traffic = traffic;
        Dock = DockStyle.Fill;
        Font = new Font(UiMetrics.FontFamily, 9F);
        Padding = new Padding(8);
        var hint = new AntdUI.Label
        {
            Dock = DockStyle.Top, Height = 82, Padding = new Padding(8),
            Text = "流量监控与网络管控\n关闭开关立即拦截新请求并取消进行中的访问；设置自动保存，本地缓存仍可读取。\n统计本次运行的 HTTP / WebSocket 正文，不含协议开销；本机通信单独列出，不代表游戏客户端自身的公网流量。"
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(4), WrapContents = false };
        var weak = new AntdUI.Button { Text = "弱网模式（关闭可选公网）", AutoSize = true };
        weak.Click += (_, _) => ChangeAll(false, publicOnly: true);
        var restore = new AntdUI.Button { Text = "允许所有模块访问", AutoSize = true };
        restore.Click += (_, _) => ChangeAll(true, publicOnly: false);
        var reset = new AntdUI.Button { Text = "清空本次统计", AutoSize = true };
        reset.Click += (_, _) => { _traffic.ResetCounters(); _previous.Clear(); RefreshSnapshot(); };
        actions.Controls.AddRange([weak, restore, reset]);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Access", HeaderText = "允许联网", FillWeight = 65, MinimumWidth = 72 });
        AddColumn("Module", "模块", 140, 150);
        AddColumn("Description", "访问内容 / 影响", 260, 230);
        AddColumn("Requests", "请求 / 连接", 75, 85);
        AddColumn("Blocked", "已拦截", 65, 70);
        AddColumn("Upload", "上传", 80, 80);
        AddColumn("Download", "下载", 80, 80);
        AddColumn("Rate", "上传 / 下载速度", 130, 150);
        _refreshing = true;
        foreach (var info in NetworkTrafficService.Modules)
        {
            int index = _grid.Rows.Add(true, info.Name, (info.Local ? "[本机] " : "[公网] ") + info.Description, "0", "0", "0 B", "0 B", "0 B/s / 0 B/s");
            _grid.Rows[index].Tag = info.Module;
            _grid.Rows[index].Cells["Description"].ToolTipText = info.Description;
        }
        _refreshing = false;
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_refreshing || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = _grid.Rows[e.RowIndex];
            try { _traffic.SetEnabled((NetworkModule)row.Tag!, row.Cells[0].Value is true); RefreshSnapshot(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { RefreshSnapshot(); _summary.Text = "保存网络设置失败：" + ex.Message; }
        };
        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(actions);
        Controls.Add(hint);
        _timer.Tick += (_, _) => RefreshSnapshot();
        VisibleChanged += (_, _) => { if (Visible) { RefreshSnapshot(); _timer.Start(); } else _timer.Stop(); };
        Disposed += (_, _) => _timer.Dispose();
        RefreshSnapshot();
        UiTheme.Apply(this);
    }
    private void AddColumn(string name, string title, int weight, int minimum) => _grid.Columns.Add(new DataGridViewTextBoxColumn
        { Name = name, HeaderText = title, FillWeight = weight, MinimumWidth = minimum, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
    private void ChangeAll(bool enabled, bool publicOnly)
    {
        try { foreach (var info in NetworkTrafficService.Modules.Where(m => !publicOnly || !m.Local)) _traffic.SetEnabled(info.Module, enabled); RefreshSnapshot(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { RefreshSnapshot(); _summary.Text = "保存网络设置失败：" + ex.Message; }
    }
    private void RefreshSnapshot()
    {
        var snapshots = _traffic.Snapshot();
        long now = Environment.TickCount64;
        double seconds = Math.Max(0.001, (now - _lastTick) / 1000d);
        _lastTick = now;
        _refreshing = true;
        try
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                var s = snapshots[i]; var row = _grid.Rows[i];
                var previous = _previous.GetValueOrDefault(s.Info.Module, (s.Uploaded, s.Downloaded));
                row.Cells[0].Value = s.Enabled;
                row.Cells["Requests"].Value = s.Requests.ToString("N0");
                row.Cells["Blocked"].Value = s.Blocked.ToString("N0");
                row.Cells["Upload"].Value = FormatBytes(s.Uploaded);
                row.Cells["Download"].Value = FormatBytes(s.Downloaded);
                row.Cells["Rate"].Value = $"{FormatBytes((long)(Math.Max(0, s.Uploaded - previous.Item1) / seconds))}/s / {FormatBytes((long)(Math.Max(0, s.Downloaded - previous.Item2) / seconds))}/s";
                _previous[s.Info.Module] = (s.Uploaded, s.Downloaded);
            }
            var external = snapshots.Where(s => !s.Info.Local).ToArray();
            _summary.Text = $"公网：↑ {FormatBytes(external.Sum(s => s.Uploaded))}  ↓ {FormatBytes(external.Sum(s => s.Downloaded))}    本机：{FormatBytes(snapshots.Where(s => s.Info.Local).Sum(s => s.Uploaded + s.Downloaded))}    已关闭 {snapshots.Count(s => !s.Enabled)} 个模块    已拦截 {snapshots.Sum(s => s.Blocked):N0} 次";
        }
        finally { _refreshing = false; }
    }
    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.00} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.00} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _grid.BackgroundColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _grid.GridColor = palette.Border;
        _grid.DefaultCellStyle.BackColor = palette.Surface;
        _grid.DefaultCellStyle.ForeColor = palette.TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = palette.SurfaceMuted;
        _grid.DefaultCellStyle.SelectionForeColor = palette.TextPrimary;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceRaised;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
    }
}
