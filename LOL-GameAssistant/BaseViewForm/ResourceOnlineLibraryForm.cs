using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Chinese directory browser for the original Wind-compatible Gitee mirror.</summary>
public sealed class ResourceOnlineLibraryForm : AntdUI.Window, IThemeAware
{
    private readonly IResourceSkinRepository _repository;
    private readonly AntdUI.Input _repo = new() { Dock = DockStyle.Fill, Name = "ResourceRepository", PlaceholderText = "Gitee 仓库：owner/repository" };
    private readonly AntdUI.Input _branch = new() { Dock = DockStyle.Fill, Name = "ResourceBranch", PlaceholderText = "分支（留空使用默认）" };
    private readonly AntdUI.Input _access = new() { Dock = DockStyle.Fill, Name = "ResourceAccessToken", UseSystemPasswordChar = true, PlaceholderText = "访问令牌（仅本次窗口使用）" };
    private readonly AntdUI.Input _search = new() { Dock = DockStyle.Fill, Name = "ResourceOnlineSearch", PlaceholderText = "搜索当前目录的英雄或皮肤" };
    private readonly AntdUI.Label _pathLabel = new() { Dock = DockStyle.Fill, Text = "根目录" };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Fill, Name = "ResourceOnlineStatus", Text = "点击连接仓库，按英雄目录浏览皮肤资源。" };
    private readonly AntdUI.Table _list = new() { Dock = DockStyle.Fill, Name = "ResourceOnlineEntries", Bordered = true, MultipleRows = true };
    private readonly AntdUI.Button _connect = Button("连接仓库", "ResourceConnect");
    private readonly AntdUI.Button _up = Button("上一级", "ResourceUp");
    private readonly AntdUI.Button _download = Button("下载并加入", "ResourceDownload");
    private readonly AntdUI.Button _cancel = Button("取消任务", "ResourceDownloadCancel");
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private ResourceRepositoryOptions? _connected;
    private IReadOnlyList<ResourceRepositoryEntry> _entries = [];
    private string _path = "";
    private bool _busy;
    public List<string> DownloadedPaths { get; } = [];

    public ResourceOnlineLibraryForm(IResourceSkinRepository repository)
    {
        _repository = repository; Text = "在线皮肤资源库"; Width = 1100; Height = 760; MinimumSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Padding = new Padding(16);
        WindowLayout.ConfigureDpi(this);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 48, 64, 56, 48 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        var sourceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        sourceRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75)); sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        sourceRow.Controls.Add(_repo, 0, 0); sourceRow.Controls.Add(_branch, 1, 0);
        layout.Controls.Add(sourceRow, 0, 0); layout.Controls.Add(_access, 0, 1);
        layout.Controls.Add(new AntdUI.Label { Dock = DockStyle.Fill, Text = "使用原软件的 Gitee 目录 / WAD 下载接口。令牌留空时读取 SKIN_MANAGER_GITEE_TOKEN，否则匿名访问。\n此处填写你获授权的令牌；令牌不写入设置、下载地址或日志。双击英雄目录进入，选中皮肤后下载。" }, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = Padding.Empty };
        actions.Controls.AddRange([_connect, _up, _download, _cancel]); _cancel.Enabled = false;
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        searchRow.Controls.Add(_pathLabel, 0, 0); searchRow.Controls.Add(_search, 1, 0);
        layout.Controls.Add(actions, 0, 3); layout.Controls.Add(searchRow, 0, 4);
        _list.Columns.Add(new AntdUI.Column("Name", "英雄 / 皮肤资源") { Width = "70%", LineBreak = true });
        _list.Columns.Add(new AntdUI.Column("Kind", "类型") { Width = "15%" });
        _list.Columns.Add(new AntdUI.Column("Size", "大小") { Width = "15%" });
        layout.Controls.Add(_list, 0, 5); layout.Controls.Add(_status, 0, 6); Controls.Add(layout);
        AntdWindowChrome.AddHeader(this);
        _repo.Text = (Environment.GetEnvironmentVariable("SKIN_MANAGER_GITEE_OWNER") ?? "jinjutwo") + "/" +
            (Environment.GetEnvironmentVariable("SKIN_MANAGER_GITEE_REPO") ?? "skin-care-manager--hid.dll");
        _connect.Click += async (_, _) => await RunAsync(async token =>
        {
            _connected = null; _entries = []; ShowEntries();
            var parts = _repo.Text.Trim().Split('/');
            if (parts.Length != 2) throw new ArgumentException("请填写 owner/repository 格式的 Gitee 仓库名");
            var options = new ResourceRepositoryOptions { Owner = parts[0], Repository = parts[1], Branch = _branch.Text.Trim(),
                AccessToken = string.IsNullOrWhiteSpace(_access.Text) ? Environment.GetEnvironmentVariable("SKIN_MANAGER_GITEE_TOKEN") ?? "" : _access.Text.Trim() };
            var entries = await _repository.ListAsync(options, "", token);
            if (IsDisposed) return;
            _connected = options; _path = ""; _entries = entries; _search.Text = ""; ShowEntries();
            _status.Text = $"已连接，根目录共 {entries.Count} 项。";
        });
        _up.Click += async (_, _) =>
        {
            if (_connected == null || _path.Length == 0) return;
            int slash = _path.LastIndexOf('/'); await NavigateAsync(slash < 0 ? "" : _path[..slash]);
        };
        _list.CellDoubleClick += async (_, e) =>
        {
            if (!_busy && e.Record is ResourceEntryRow { Entry.IsDirectory: true } row)
                await NavigateAsync(row.Entry.Path);
        };
        _download.Click += async (_, _) =>
        {
            var selected = _list.SelectedsReal().OfType<ResourceEntryRow>().Select(row => row.Entry).Where(e => !e.IsDirectory).ToArray();
            if (_connected == null || selected.Length == 0) { _status.Text = "请先进入英雄目录并选择 WAD 资源文件。"; return; }
            var options = _connected;
            await RunAsync(async token =>
            {
                int done = 0;
                var progress = new Progress<ResourceDownloadProgress>(p =>
                {
                    if (!IsDisposed && _busy) _status.Text = $"{p.Name}：{p.Bytes / 1048576d:F1} MB" + (p.Total.HasValue ? $" / {p.Total.Value / 1048576d:F1} MB" : "");
                });
                foreach (var entry in selected)
                {
                    string file = await _repository.DownloadAsync(options, entry, progress, token);
                    if (!DownloadedPaths.Contains(file, StringComparer.OrdinalIgnoreCase)) DownloadedPaths.Add(file);
                    done++;
                }
                if (!IsDisposed) _status.Text = $"已下载并校验 {done} 个资源包。关闭窗口后将加入待挂载列表。";
            });
        };
        _cancel.Click += (_, _) => _operation?.Cancel();
        _search.TextChanged += (_, _) => ShowEntries();
        void SourceChanged(object? sender, EventArgs args) { _connected = null; _entries = []; _path = ""; ShowEntries(); _status.Text = "资源来源或令牌已变更，请重新连接仓库。"; }
        _repo.TextChanged += SourceChanged; _branch.TextChanged += SourceChanged; _access.TextChanged += SourceChanged;
        UiTheme.Apply(this);
    }
    private Task NavigateAsync(string path) => RunAsync(async token =>
    {
        if (_connected == null) return;
        var entries = await _repository.ListAsync(_connected, path, token);
        if (IsDisposed) return;
        _path = path; _entries = entries; _search.Text = ""; ShowEntries(); _status.Text = $"当前目录共 {entries.Count} 项。";
    });
    private void ShowEntries()
    {
        _pathLabel.Text = _path.Length == 0 ? "根目录" : _path;
        _list.SelectedIndexs = [];
        _list.DataSource = _entries.Where(e => e.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(entry => new ResourceEntryRow(entry.Name, entry.IsDirectory ? "英雄目录" : "WAD 资源",
                entry.IsDirectory ? "" : $"{entry.Size / 1048576d:F1} MB", entry)).ToArray();
    }
    private sealed record ResourceEntryRow(string Name, string Kind, string Size, ResourceRepositoryEntry Entry);
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true; _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); Enable(false); _status.Text = "正在连接资源服务器…";
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "任务已取消，已完成的下载仍会加入列表。"; }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
        finally { _operation.Dispose(); _operation = null; _busy = false; if (!IsDisposed) Enable(true); }
    }
    private void Enable(bool enabled)
    {
        foreach (Control control in new Control[] { _repo, _branch, _access, _connect, _up, _download, _list }) control.Enabled = enabled;
        _cancel.Enabled = !enabled;
    }
    private static AntdUI.Button Button(string text, string name) => new() { Text = text, Name = name, Width = 126, Height = 42 };
    public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette); BackColor = palette.Surface; ForeColor = palette.TextPrimary;
        _list.BackColor = palette.SurfaceRaised; _list.ForeColor = palette.TextPrimary;
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { _lifetime.Cancel(); base.OnFormClosing(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _lifetime.Cancel(); _connected = null; _access.Text = ""; }
        base.Dispose(disposing);
    }
}
