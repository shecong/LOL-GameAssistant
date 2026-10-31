using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed class TrafficMonitorForm : UserControl, IThemeAware
{
    private readonly NetworkTrafficService _traffic;
    private readonly AntdUI.Table _grid = new()
    {
        Name = "networkTrafficTable", Dock = DockStyle.Fill, Bordered = true,
        FixedHeader = true, RowHeight = 48, RowHeightHeader = 38
    };
    private readonly TrafficRow[] _rows = NetworkTrafficService.Modules.Select(info => new TrafficRow(info)).ToArray();
    private readonly AntdUI.Label _summary = new() { Dock = DockStyle.Top, Height = 58, TextMultiLine = true, Padding = new Padding(8, 0, 8, 0) };
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
            Dock = DockStyle.Top, Height = 100, TextMultiLine = true, Padding = new Padding(8),
            Text = "流量监控与网络管控\n关闭开关立即拦截新请求并取消进行中的访问；设置自动保存，本地缓存仍可读取。\n统计本次运行的 HTTP / WebSocket 正文，不含协议开销；本机通信单独列出，不代表游戏客户端自身的公网流量。"
        };
        var actions = new FlowLayoutPanel { Name = "networkTrafficActions", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4), WrapContents = true };
        var weak = new AntdUI.Button { Text = "弱网模式（关闭可选公网）", AutoSize = true };
        weak.Click += (_, _) => ChangeAll(false, publicOnly: true);
        var restore = new AntdUI.Button { Text = "允许所有模块访问", AutoSize = true };
        restore.Click += (_, _) => ChangeAll(true, publicOnly: false);
        var reset = new AntdUI.Button { Text = "清空本次统计", AutoSize = true };
        reset.Click += (_, _) => { _traffic.ResetCounters(); _previous.Clear(); RefreshSnapshot(); };
        actions.Controls.AddRange([weak, restore, reset]);
        _grid.Columns.Add(new AntdUI.ColumnSwitch("Access", "允许联网") { Width = "88", Fixed = true });
        foreach (var (key, title, width) in new[] {
            ("Name", "模块", "160"), ("Description", "访问内容 / 影响", "320"),
            ("Requests", "请求 / 连接", "100"), ("Blocked", "已拦截", "85"),
            ("Upload", "上传", "100"), ("Download", "下载", "100"), ("Rate", "上传 / 下载速度", "180") })
            _grid.Columns.Add(new AntdUI.Column(key, title) { Width = width, LineBreak = key == "Description" });
        _grid.SizeChanged += (_, _) => _grid.Columns[2].Width = Math.Max(320, _grid.ClientSize.Width - 831).ToString();
        _grid.DataSource = _rows;
        _grid.CheckedChanged += (_, e) =>
        {
            if (_refreshing || e.Record is not TrafficRow row) return;
            try { _traffic.SetEnabled(row.Module, e.Value); RefreshSnapshot(); }
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
                var s = snapshots[i]; var row = _rows[i];
                var previous = _previous.GetValueOrDefault(s.Info.Module, (s.Uploaded, s.Downloaded));
                row.Access = s.Enabled;
                row.Requests = s.Requests.ToString("N0");
                row.Blocked = s.Blocked.ToString("N0");
                row.Upload = FormatBytes(s.Uploaded);
                row.Download = FormatBytes(s.Downloaded);
                row.Rate = $"{FormatBytes((long)(Math.Max(0, s.Uploaded - previous.Item1) / seconds))}/s / {FormatBytes((long)(Math.Max(0, s.Downloaded - previous.Item2) / seconds))}/s";
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
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        AntdContentTheme.Apply(_grid, palette);
    }

    private sealed class TrafficRow(NetworkModuleInfo info) : AntdUI.NotifyProperty
    {
        public NetworkModule Module => info.Module;
        public string Name => info.Name;
        public string Description => (info.Local ? "[本机] " : "[公网] ") + info.Description;
        private bool _access;
        public bool Access { get => _access; set => Set(ref _access, value); }
        private string _requests = "0", _blocked = "0", _upload = "0 B", _download = "0 B", _rate = "0 B/s / 0 B/s";
        public string Requests { get => _requests; set => Set(ref _requests, value); }
        public string Blocked { get => _blocked; set => Set(ref _blocked, value); }
        public string Upload { get => _upload; set => Set(ref _upload, value); }
        public string Download { get => _download; set => Set(ref _download, value); }
        public string Rate { get => _rate; set => Set(ref _rate, value); }
        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value; OnPropertyChanged(name);
        }
    }
}
