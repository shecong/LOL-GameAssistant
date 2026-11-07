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
    private readonly IResourceSkinLoader _loader;
    private readonly IResourceSkinRepository _repository;
    private readonly List<string> _packages = [];
    private readonly AntdUI.Input _path = new() { Name = "ResourceGamePath", Dock = DockStyle.Fill, PlaceholderText = "英雄联盟 Game 目录" };
    private readonly AntdUI.Label _status = new() { Name = "ResourceSkinStatus", Dock = DockStyle.Fill, Text = "先开启资源包换肤，再添加资源包并在进入对局前挂载。" };
    private readonly ListView _list = new() { Name = "ResourcePackages", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly AntdUI.Button _add = Button("添加资源包", "ResourceAdd");
    private readonly AntdUI.Button _remove = Button("移除所选", "ResourceRemove");
    private readonly AntdUI.Button _apply = Button("挂载资源包", "ResourceApply");
    private readonly AntdUI.Button _clear = Button("清理已挂载", "ResourceClear");
    private readonly AntdUI.Button _browse = Button("选择目录", "ResourceBrowse");
    private readonly AntdUI.Button _install = Button("安装加载组件", "ResourceInstallLoader");
    private readonly AntdUI.Button _online = Button("在线资源库", "ResourceOnlineLibrary");
    private readonly AntdUI.Switch _enabled = new() { Name = "ResourceSkinEnabled", Width = 52, Height = 30 };
    private bool _syncingEnabled;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetime = new();
    private ResourceSkinState? _shownState;
    private bool _busy;

    public ResourceSkinForm(IResourceSkinService resources, ISkinEngineCoordinator engines, IApplicationSettingsStore settings,
        IResourceSkinLoader? loader = null, IResourceSkinRepository? repository = null)
    {
        _resources = resources; _engines = engines; _settings = settings;
        _loader = loader ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.ResourceSkinLoader;
        _repository = repository ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.ResourceSkinRepository;
        Dock = DockStyle.Fill; Padding = new Padding(16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 48, 56, 76 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        title.Controls.Add(new AntdUI.Label { Text = "资源包换肤", Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        var toggleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
        toggleRow.Controls.Add(new AntdUI.Label { Text = "启用资源包换肤", Width = 140, Height = 30, Margin = Padding.Empty }); toggleRow.Controls.Add(_enabled);
        title.Controls.Add(toggleRow, 1, 0); layout.Controls.Add(title, 0, 0);
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        pathRow.Controls.Add(_path, 0, 0); pathRow.Controls.Add(_browse, 1, 0); layout.Controls.Add(pathRow, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        actions.Controls.AddRange([_add, _online, _remove, _apply, _clear, _install]); layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(new AntdUI.Label { Dock = DockStyle.Fill, Text = "请在进入对局前准备资源，进入游戏后不能补挂。按资源包说明选择英雄及皮肤，替换基础皮肤的包请选默认皮肤。\n资源准备完成只代表文件已写入；效果以游戏内为准。对局结束后自动清理本助手准备的资源。" }, 0, 3);
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
        _online.Click += (_, _) =>
        {
            using var library = new ResourceOnlineLibraryForm(_repository);
            library.ShowDialog(this);
            foreach (string path in library.DownloadedPaths) if (!_packages.Contains(path, StringComparer.OrdinalIgnoreCase)) _packages.Add(path);
            RefreshList();
            if (library.DownloadedPaths.Count > 0) _status.Text = $"已加入 {library.DownloadedPaths.Count} 个在线资源包，请在进入对局前点击挂载资源包。";
        };
        _install.Click += async (_, _) => await RunAsync(async () =>
        {
            if (!_engines.IsEnabled(SkinEngineMode.Resource)) throw new InvalidOperationException("请先开启资源包换肤开关，再安装加载组件");
            string game = ResourceSkinService.ResolveGame(_path.Text.Trim());
            await _loader.EnsureInstalledAsync(game, _lifetime.Token);
            var latest = _settings.Load(); latest.CslolGamePath = game; _settings.Save(latest);
            _status.Text = "HID.dll 已安装并通过校验；下次启动游戏时加载。";
        });
        _apply.Type = AntdUI.TTypeMini.Primary;
        _apply.Click += async (_, _) => await RunAsync(async () =>
        {
            if (!_engines.IsEnabled(SkinEngineMode.Resource)) throw new InvalidOperationException("请先开启资源包换肤开关");
            string game = ResourceSkinService.ResolveGame(_path.Text.Trim());
            if (_packages.Count == 0) throw new InvalidOperationException("请先添加资源包");
            await _loader.EnsureInstalledAsync(game, _lifetime.Token);
            await _resources.ApplyAsync(game, _packages.ToArray(), _lifetime.Token);
            var latest = _settings.Load(); latest.CslolGamePath = game; _settings.Save(latest);
            _status.Text = $"已准备 {_resources.State.Files.Count} 个资源包，等待下一次游戏启动；尚未验证游戏内效果。";
        });
        _clear.Click += async (_, _) => await RunAsync(async () => { await Task.Run(() => _resources.ClearAsync(_lifetime.Token)); _status.Text = "已清理本助手挂载的资源包。"; });
        _enabled.CheckedChanged += async (_, _) =>
        {
            if (_syncingEnabled) return;
            bool enabled = _enabled.Checked;
            await RunAsync(async () =>
            {
                await _engines.SetEnabledAsync(SkinEngineMode.Resource, enabled, _lifetime.Token);
                _status.Text = enabled ? "资源包换肤已启用，其他换肤功能已关闭。" : "资源包换肤已关闭，挂载资源已清理，已知加载组件已停用并保留备份。";
            });
            if (!IsDisposed) SyncEnabled();
        };
        _engines.Changed += EngineChanged;
        VisibleChanged += (_, _) => { if (!_busy) { SyncEnabled(); SetEnabled(true); } };
        _refresh.Tick += (_, _) =>
        {
            if (_busy || !Visible) return;
            SyncEnabled(); SetEnabled(true);
            if (!ReferenceEquals(_shownState, _resources.State))
            {
                if (_shownState?.Files.Count > 0 && _resources.State.Files.Count == 0) _status.Text = "对局已结束，挂载资源已自动清理。";
                RefreshList();
            }
            if (_resources is ResourceSkinService { CleanupError: { } error }) _status.Text = "自动清理未完成：" + error;
        };
        _refresh.Start(); RefreshList(); SyncEnabled(); SetEnabled(true); UiTheme.Apply(this);
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
        foreach (Control control in new Control[] { _add, _online, _remove, _apply, _clear, _install, _path, _browse }) control.Enabled = enabled;
        _enabled.Enabled = enabled;
        _apply.Enabled = enabled && _engines.IsEnabled(SkinEngineMode.Resource);
        _install.Enabled = enabled && _engines.IsEnabled(SkinEngineMode.Resource);
    }
    private void SyncEnabled()
    {
        _syncingEnabled = true; try { _enabled.Checked = _engines.IsEnabled(SkinEngineMode.Resource); } finally { _syncingEnabled = false; }
    }
    private void EngineChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(EngineChanged); } catch (InvalidOperationException) { } return; }
        SyncEnabled(); if (!_busy) SetEnabled(true);
    }
    private void RefreshList()
    {
        _shownState = _resources.State; _list.BeginUpdate(); _list.Items.Clear();
        foreach (var file in _shownState.Files)
            _list.Items.Add(new ListViewItem([file.Source, "已准备", $"{file.Bytes / 1048576d:F1} MB", file.Entries == 0 ? "加密包" : file.Entries.ToString()]));
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
        if (disposing) { _engines.Changed -= EngineChanged; _refresh.Dispose(); _lifetime.Cancel(); }
        base.Dispose(disposing);
    }
}
