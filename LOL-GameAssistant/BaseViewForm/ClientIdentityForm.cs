using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Full identity catalog browser, using the same preview-before-apply flow as profile backgrounds.</summary>
internal sealed class ClientIdentityForm : AntdUI.Window, IThemeAware
{
    private const int PageSize = 36;
    private readonly IClientIdentityService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _imageGate = new(4, 4);
    private readonly List<Image> _images = [];
    private readonly AntdUI.Segmented _categories = new() { Name = "IdentityCategories", Dock = DockStyle.Top, Height = 44, Full = true };
    private readonly AntdUI.Input _search = new() { Name = "IdentitySearch", Dock = DockStyle.Left, Width = 270, PlaceholderText = "搜索名称或 ID" };
    private readonly AntdUI.Select _ownership = new() { Name = "IdentityOwnership", Dock = DockStyle.Left, Width = 150, List = true };
    private readonly AntdUI.Select _slot = new() { Name = "IdentityBadgeSlot", Dock = DockStyle.Right, Width = 140, List = true };
    private readonly FlowLayoutPanel _grid = new() { Name = "IdentityGrid", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
    private readonly AntdUI.Label _summary = new() { Dock = DockStyle.Top, Height = 48, TextMultiLine = true, Padding = new Padding(12, 5, 12, 0) };
    private readonly AntdUI.Label _status = new() { Name = "IdentityStatus", Dock = DockStyle.Bottom, Height = 74, TextMultiLine = true, Padding = new Padding(12, 6, 12, 6) };
    private readonly AntdUI.Button _read = new() { Name = "IdentityRead", Text = "刷新目录与状态", Width = 150, Height = 38 };
    private readonly AntdUI.Button _apply = new() { Name = "IdentityApply", Text = "应用所选装饰", Width = 150, Height = 38, Enabled = false };
    private readonly AntdUI.Button _restore = new() { Name = "IdentityRestore", Text = "恢复本次原样", Width = 150, Height = 38, Enabled = false };
    private readonly AntdUI.Button _more = new() { Dock = DockStyle.Bottom, Height = 32, Text = "加载更多", Visible = false };
    private ClientIdentityCatalog? _catalog;
    private ClientIdentityChoice? _selected;
    private bool _busy;
    private int _visibleCount = PageSize, _renderVersion, _closed;

    public ClientIdentityForm(IClientIdentityService service)
    {
        _service = service;Text = "身份装饰 · 边框、勋章、头衔与旗帜";Size = new Size(1120, 790);MinimumSize = new Size(820, 600);
        Font = new Font(UiMetrics.FontFamily, 10F);
        StartPosition = FormStartPosition.CenterParent;
        foreach (string name in new[] { "边框", "勋章", "头衔", "旗帜" })_categories.Items.Add(new AntdUI.SegmentedItem { Text = name });
        _categories.SelectIndex = 0;
        foreach (string name in new[] { "全部（含未获取）", "已拥有", "未获取 / 未知" })_ownership.Items.Add(name);
        _ownership.SelectedIndex = 0;
        foreach (string name in new[] { "勋章槽位 1", "勋章槽位 2", "勋章槽位 3" })_slot.Items.Add(name);
        _slot.SelectedIndex = 0;_slot.Visible = false;
        var filters = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 5, 12, 5) };
        filters.Controls.Add(_slot);filters.Controls.Add(_ownership);filters.Controls.Add(_search);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 5, 12, 5) };
        actions.Controls.AddRange([_read, _apply, _restore]);
        Controls.Add(_grid);Controls.Add(_more);Controls.Add(_summary);Controls.Add(filters);Controls.Add(_categories);
        Controls.Add(_status);Controls.Add(actions);AntdWindowChrome.AddHeader(this);
        _categories.SelectIndexChanged += (_, _) => { _selected = null;_visibleCount = PageSize;Render(); };
        _ownership.SelectedIndexChanged += (_, _) => { _visibleCount = PageSize;Render(); };
        _search.TextChanged += (_, _) => { _visibleCount = PageSize;Render(); };
        _more.Click += (_, _) => { _visibleCount += PageSize;Render(); };
        _read.Click += async (_, _) => await ReadAsync();
        _apply.Click += async (_, _) => await ChangeAsync(false);
        _restore.Click += async (_, _) => await ChangeAsync(true);
        Shown += async (_, _) => { UiTheme.Apply(this);await ReadAsync(); };
        Disposed += (_, _) => { if (Interlocked.Exchange(ref _closed, 1) != 0)return;_lifetime.Cancel();_lifetime.Dispose();++_renderVersion;ClearImages(); };
        _status.Text = "从完整资源目录选择，未获取选项也可提交。点击条目只选择，点击“应用”才写入；结果会按客户端实际回读显示。";
    }
    public void ApplyTheme(ThemePalette palette) {BackColor = palette.Surface;ForeColor = palette.TextPrimary;_grid.BackColor = palette.Surface;}
    private void Buttons()
    {
        _read.Enabled = !_busy;_restore.Enabled = !_busy && _catalog != null;
        _apply.Enabled = !_busy && _catalog != null && _selected != null;
        _categories.Enabled = _search.Enabled = _ownership.Enabled = _slot.Enabled = !_busy;
        _grid.Enabled = _more.Enabled = !_busy;
    }
    private async Task ReadAsync()
    {
        if (_busy || IsDisposed)return;_busy = true;Buttons();_status.Text = "正在读取完整装饰目录与拥有状态…";
        try {
            var catalog = await _service.ReadAsync(_lifetime.Token);
            if (IsDisposed)return;_catalog = catalog;_selected = null;Render();
            _status.Text = catalog.Notices.Count == 0 ? "目录已读取。未获取项保留在列表中，实际能否装备以应用后的回读为准。" : string.Join("\n", catalog.Notices.Take(2));
        } catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsDisposed) { _catalog = null;_status.Text = "读取失败：" + error.Message;RuntimeDiagnostics.Report("身份装饰", "读取失败", error.Message); } }
        finally { _busy = false;if (!IsDisposed)Buttons(); }
    }
    private async Task ChangeAsync(bool restore)
    {
        if (_busy || _catalog == null || !restore && _selected == null)return;
        _busy = true;Buttons();_status.Text = restore ? "正在恢复本次首次读取的装饰…" : "正在应用并核对客户端回读…";
        try {
            var result = restore ? await _service.RestoreAsync(_catalog.State.Session, _lifetime.Token)
                : await _service.ApplyAsync(_catalog.State.Session, _selected!.Category, _selected.Id, Math.Max(0, _slot.SelectedIndex), _lifetime.Token);
            if (IsDisposed)return;
            if (result.Code == "session_changed") { _catalog = null;_selected = null; }
            else _catalog = _catalog with { State = result.State };
            _status.Text = result.Message;
            RuntimeDiagnostics.Report("身份装饰", result.Code, result.Message);Render();
        } catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsDisposed) { _status.Text = error.Message;RuntimeDiagnostics.Report("身份装饰", "操作未完成", error.Message); } }
        finally { _busy = false;if (!IsDisposed)Buttons(); }
    }
    private void Render()
    {
        if (IsDisposed)return;int version = ++_renderVersion;
        ClearImages();ControlLifetime.ClearAndDispose(_grid);
        _slot.Visible = _categories.SelectIndex == 1;
        if (_catalog == null){Buttons();return;}
        var category = (ClientIdentityCategory)Math.Clamp(_categories.SelectIndex, 0, 3);
        string query = _search.Text.Trim();
        var choices = _catalog.Choices.Where(x => x.Category == category &&
            (_ownership.SelectedIndex == 0 || _ownership.SelectedIndex == 1 && x.Owned == true || _ownership.SelectedIndex == 2 && x.Owned != true) &&
            (query.Length == 0 || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        var state = _catalog.State;
        _summary.Text = $"显示 {Math.Min(choices.Length, _visibleCount)} / {choices.Length} 项 · 当前等级边框 {state.BorderLevel} 级 · 头衔 {state.Title} · 旗帜 {state.Banner}\n勋章：{string.Join(" / ", state.Badges)}";
        foreach (var choice in choices.Take(_visibleCount)) {
            bool selected = _selected?.Category == choice.Category && _selected.Id == choice.Id;
            var tile = new AntdUI.Panel { Width = 190, Height = 220, Padding = new Padding(7), Radius = 8, BorderWidth = selected ? 3 : 1,
                Margin = new Padding(6), BackColor = UiTheme.Palette.SurfaceRaised, ForeColor = UiTheme.Palette.TextPrimary };
            var picture = new PictureBox { Dock = DockStyle.Top, Height = 108, SizeMode = PictureBoxSizeMode.Zoom };
            var details = new AntdUI.Label { Dock = DockStyle.Fill, TextMultiLine = true, AutoEllipsis = true,
                Text = choice.Name + "\n" + (choice.Owned == true ? "已拥有" : choice.Owned == false ? "未获取" : "拥有状态未知") + " · ID " + choice.Id,
                Padding = new Padding(2, 4, 2, 0) };
            var select = new AntdUI.Button { Name = $"IdentityChoice_{choice.Category}_{choice.Id}", Dock = DockStyle.Bottom, Height = 30, Text = selected ? "已选择" : "选择", Enabled = !_busy };
            select.Click += (_, _) => { _selected = choice;_status.Text = choice.Name + "\n" + choice.Description + "\n点击“应用所选装饰”后检查实际结果。";Render(); };
            tile.Controls.Add(details);tile.Controls.Add(select);
            if (choice.ImagePath.Length == 0)tile.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 108, Text = choice.Name, TextAlign = ContentAlignment.MiddleCenter, TextMultiLine = true, Font = new Font(UiMetrics.FontFamily, 12F, FontStyle.Bold) });
            else tile.Controls.Add(picture);
            _grid.Controls.Add(tile);
            _ = LoadImageAsync(choice, picture, version, _lifetime.Token);
        }
        _more.Visible = choices.Length > _visibleCount;Buttons();
    }
    private async Task LoadImageAsync(ClientIdentityChoice choice, PictureBox picture, int version, CancellationToken token)
    {
        if (choice.ImagePath.Length == 0)return;bool acquired = false;
        try {
            await _imageGate.WaitAsync(token);acquired = true;
            if (version != _renderVersion || token.IsCancellationRequested)return;
            var bytes = await _service.GetImageAsync(choice, token);if (bytes is not { Length: > 0 })return;
            using var stream = new MemoryStream(bytes);using var source = Image.FromStream(stream);var bitmap = new Bitmap(source);
            if (IsDisposed || picture.IsDisposed || version != _renderVersion || token.IsCancellationRequested){bitmap.Dispose();return;}
            _images.Add(bitmap);picture.Image = bitmap;
        } catch (OperationCanceledException) { }
        catch (Exception) { /* A missing preview never prevents selection/application. */ }
        finally { if (acquired)_imageGate.Release(); }
    }
    private void ClearImages() {foreach (var image in _images)image.Dispose();_images.Clear();}
}
