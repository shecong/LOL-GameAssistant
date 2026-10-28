using Panel = AntdUI.Panel;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>显示可重建缓存的磁盘大小与内存数据量，按类别清理。</summary>
internal sealed class LocalCacheToolsControl : UserControl, IThemeAware
{
    private readonly ILocalCacheManagementService _service;
    private readonly UiRequestScope _requests = new();
    private readonly TableLayoutPanel _rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
    private readonly AntdUI.Label _message = new() { Dock = DockStyle.Bottom, Height = 52, AutoEllipsis = true };
    private readonly AntdUI.Button _refresh = new() { Text = "刷新缓存大小", AutoSize = true };
    private readonly List<AntdUI.Button> _clearButtons = [];
    private bool _operationInProgress;

    internal LocalCacheToolsControl(ILocalCacheManagementService service)
    {
        _service = service;
        Dock = DockStyle.Fill;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, WrapContents = false };
        toolbar.Controls.Add(_refresh);
        var note = new AntdUI.Label
        {
            Dock = DockStyle.Top, Height = 68,
            Text = "分别清理可重新获取的数据。设置、密钥、收藏、符文及游戏设置备份保留。\n内存大小为数据量估算；清理不关闭当前页面，后续查询会重新获取缓存。"
        };
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        viewport.Controls.Add(_rows);
        Controls.Add(viewport); Controls.Add(_message); Controls.Add(note); Controls.Add(toolbar);
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _refresh.Click += async (_, _) => await RefreshUsageAsync();
        VisibleChanged += async (_, _) => { if (Visible && IsHandleCreated) await RefreshUsageAsync(); };
        Load += async (_, _) => await RefreshUsageAsync();
        Disposed += (_, _) => _requests.Dispose();
        ApplyTheme(UiTheme.Palette);
    }

    internal async Task RefreshUsageAsync()
    {
        // Load and VisibleChanged may fire together. Reuse the active operation
        // rather than repeatedly cancelling a disk scan that has just started.
        if (IsDisposed || Disposing || _operationInProgress) return;
        _operationInProgress = true;
        using var request = _requests.Begin();
        SetBusy(true);
        try
        {
            var usage = await _service.GetUsageAsync(request.Token);
            if (request.IsCurrent) Render(usage);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex) { if (request.IsCurrent) _message.Text = $"读取缓存失败：{ex.Message}"; }
        finally
        {
            _operationInProgress = false;
            if (request.IsCurrent) SetBusy(false);
        }
    }

    internal async Task ClearCategoryAsync(LocalCacheCategory category)
    {
        if (IsDisposed || Disposing || _operationInProgress) return;
        _operationInProgress = true;
        using var request = _requests.Begin();
        SetBusy(true);
        _message.Text = "正在清理本地缓存…";
        try
        {
            var result = await _service.ClearAsync(category, request.Token);
            var usage = await _service.GetUsageAsync(request.Token);
            if (!request.IsCurrent) return;
            Render(usage);
            _message.Text = result.Message;
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex) { if (request.IsCurrent) _message.Text = $"清理失败：{ex.Message}"; }
        finally
        {
            _operationInProgress = false;
            if (request.IsCurrent) SetBusy(false);
        }
    }

    private void Render(IReadOnlyList<LocalCacheUsage> usage)
    {
        _rows.SuspendLayout();
        try
        {
            ControlLifetime.ClearAndDispose(_rows);
            _rows.RowStyles.Clear(); _rows.RowCount = 0; _clearButtons.Clear();
            foreach (var item in usage)
            {
                var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var title = new AntdUI.Label { Text = UiLanguage.T(item.Name), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill };
                string detail = $"磁盘 {SizeText(item.DiskBytes)} · {item.FileCount} 个文件    内存数据约 {SizeText(item.MemoryDataBytes)} · {item.MemoryEntries} 条";
                if (item.UnavailableFiles > 0) detail += $" · {item.UnavailableFiles} 项不可读取";
                var size = new AntdUI.Label { Text = detail, AutoSize = true, Dock = DockStyle.Fill };
                var location = new AntdUI.Label { Text = item.Location, AutoSize = true, MaximumSize = new Size(Math.Max(220, Width - 150), 0), Dock = DockStyle.Fill };
                var clear = new AntdUI.Button { Text = "清除此类缓存", AutoSize = true, Height = 36, Tag = item.Category };
                clear.Click += async (_, _) => await ClearCategoryAsync(item.Category);
                _clearButtons.Add(clear);
                row.Controls.Add(title, 0, 0); row.Controls.Add(clear, 1, 0);
                row.Controls.Add(size, 0, 1); row.SetColumnSpan(size, 2);
                row.Controls.Add(location, 0, 2); row.SetColumnSpan(location, 2);
                int index = _rows.RowCount++;
                _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _rows.Controls.Add(row, 0, index);
            }
            ApplyTheme(UiTheme.Palette);
        }
        finally { _rows.ResumeLayout(true); }
    }
    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        foreach (var button in _clearButtons) button.Enabled = !busy;
    }
    internal static string SizeText(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.SurfaceRaised;
        ForeColor = palette.TextPrimary;
        void Visit(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is AntdUI.Label label) label.ForeColor = palette.TextPrimary;
                Visit(child);
            }
        }
        Visit(this);
    }
}
