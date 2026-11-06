using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Skins;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Independent local resource package page; native hero state is never reused.</summary>
public sealed class ResourceSkinForm : UserControl, IThemeAware
{
    private readonly IResourceSkinService _resources;
    private readonly ISkinEngineCoordinator _engines;
    private readonly IApplicationSettingsStore _settings;
    private readonly List<string> _packages = [];
    private readonly AntdUI.Input _path = new() { Name = "ResourceGamePath", Dock = DockStyle.Fill, PlaceholderText = "英雄联盟 Game 目录" };
    private readonly AntdUI.Label _status = new() { Name = "ResourceSkinStatus", Dock = DockStyle.Fill, Text = "添加本地资源包，然后在进入对局前点击挂载。" };
    private readonly ListView _list = new() { Name = "ResourcePackages", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly AntdUI.Button _add = Button("添加资源包", "ResourceAdd");
    private readonly AntdUI.Button _remove = Button("移除所选", "ResourceRemove");
    private readonly AntdUI.Button _apply = Button("挂载资源包", "ResourceApply");
    private readonly AntdUI.Button _clear = Button("清理已挂载", "ResourceClear");
    private readonly AntdUI.Button _browse = Button("选择目录", "ResourceBrowse");
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetime = new();
    private ResourceSkinState? _shownState;
    private bool _busy;

    public ResourceSkinForm(IResourceSkinService resources, ISkinEngineCoordinator engines, IApplicationSettingsStore settings)
    {
        _resources = resources; _engines = engines; _settings = settings;
        Dock = DockStyle.Fill; Padding = new Padding(16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 48, 56, 76 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.Controls.Add(new AntdUI.Label { Text = "资源包换肤", Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        pathRow.Controls.Add(_path, 0, 0); pathRow.Controls.Add(_browse, 1, 0); layout.Controls.Add(pathRow, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        actions.Controls.AddRange([_add, _remove, _apply, _clear]); layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(new AntdUI.Label { Dock = DockStyle.Fill, Text = "支持 WAD / Riftmod / Fantome / ZIP。挂载会替换本页上一次的资源组合。\n需要 Game 目录已有 Wind 兼容 HID.dll；检测到对局结束后自动清理本助手挂载的文件。" }, 0, 3);
        _list.Columns.Add("资源包", 420); _list.Columns.Add("状态", 100); _list.Columns.Add("大小", 100); _list.Columns.Add("条目", 80);
        layout.Controls.Add(_list, 0, 4); layout.Controls.Add(_status, 0, 5); Controls.Add(layout);
        var config = settings.Load();
        _path.Text = resources.State.Files.Count > 0 ? resources.State.GameDirectory :
            CslolSkinService.ResolveGameDirectory(config.CslolGamePath) ?? CslolSkinService.ResolveGameDirectory(config.GameClientPath) ?? config.CslolGamePath;
        _browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择英雄联盟 Game 目录", SelectedPath = _path.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.SelectedPath;
        };
        _add.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "添加本地换肤资源包", Multiselect = true, Filter = "换肤资源包|*.wad.client;*.riftmod.client;*.fantome;*.zip" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (string path in dialog.FileNames) if (!_packages.Contains(path, StringComparer.OrdinalIgnoreCase)) _packages.Add(path);
            RefreshList();
        };
        _remove.Click += (_, _) =>
        {
            foreach (ListViewItem item in _list.SelectedItems) if (item.Tag is string path) _packages.Remove(path);
            RefreshList();
        };
        _apply.Type = AntdUI.TTypeMini.Primary;
        _apply.Click += async (_, _) => await RunAsync(async () =>
        {
            string game = ResourceSkinService.ResolveGame(_path.Text.Trim());
            if (!File.Exists(Path.Combine(game, "HID.dll"))) throw new InvalidOperationException("游戏目录缺少 HID.dll，尚不能使用此加载方式");
            if (_packages.Count == 0) throw new InvalidOperationException("请先添加资源包");
            await _engines.SelectAsync(SkinEngineMode.Resource, _lifetime.Token);
            await _resources.ApplyAsync(game, _packages.ToArray(), _lifetime.Token);
            var latest = _settings.Load(); latest.CslolGamePath = game; _settings.Save(latest);
            _status.Text = $"已挂载 {_resources.State.Files.Count} 个 WAD，等待下一次对局加载。";
        });
        _clear.Click += async (_, _) => await RunAsync(async () => { await Task.Run(() => _resources.ClearAsync(_lifetime.Token)); _status.Text = "已清理本助手挂载的资源包。"; });
        _refresh.Tick += (_, _) =>
        {
            if (_busy || !Visible) return;
            if (!ReferenceEquals(_shownState, _resources.State))
            {
                if (_shownState?.Files.Count > 0 && _resources.State.Files.Count == 0) _status.Text = "对局已结束，挂载资源已自动清理。";
                RefreshList();
            }
            if (_resources is ResourceSkinService { CleanupError: { } error }) _status.Text = "自动清理未完成：" + error;
        };
        _refresh.Start(); RefreshList(); UiTheme.Apply(this);
    }
    private static AntdUI.Button Button(string text, string name) => new() { Name = name, Text = text, Width = 128, Height = 42 };
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; SetEnabled(false); _status.Text = "正在处理资源包…";
        try { await action(); }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "操作已取消。"; }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
        finally { _busy = false; if (!IsDisposed) { SetEnabled(true); RefreshList(); } }
    }
    private void SetEnabled(bool enabled)
    {
        foreach (Control control in new Control[] { _add, _remove, _apply, _clear, _path, _browse }) control.Enabled = enabled;
    }
    private void RefreshList()
    {
        _shownState = _resources.State; _list.BeginUpdate(); _list.Items.Clear();
        foreach (var file in _shownState.Files)
            _list.Items.Add(new ListViewItem([file.Source, "已挂载", $"{file.Bytes / 1048576d:F1} MB", file.Entries.ToString()]));
        foreach (var path in _packages)
            _list.Items.Add(new ListViewItem([Path.GetFileName(path), "待挂载", "", ""]) { Tag = path, ToolTipText = path });
        _list.EndUpdate();
    }
    public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette); BackColor = palette.Surface; ForeColor = palette.TextPrimary;
        _list.BackColor = palette.SurfaceRaised; _list.ForeColor = palette.TextPrimary;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refresh.Dispose(); _lifetime.Cancel(); }
        base.Dispose(disposing);
    }
}
