using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>AntdUI manual skin selection. Merely creating this page never loads the game module.</summary>
public sealed partial class SkinToolsForm : UserControl, IThemeAware
{
    private readonly ISkinCoreService _core;
    private readonly IApplicationSettingsStore _settings;
    private readonly AntdUI.Switch _enabled = new() { Name = "SkinCoreEnabled", Width = 52, Height = 30 };
    private readonly AntdUI.Input _search = new() { Name = "SkinSearch", Dock = DockStyle.Top, Height = 48, PlaceholderText = "搜索皮肤名称或模型" };
    private readonly AntdUI.Select _entries = new() { Dock = DockStyle.Top, Height = 56, List = true, MaxCount = 12,
        DropDownArrow = true, DropDownRadius = 10, Font = new Font(UiMetrics.FontFamily, 11), PlaceholderText = "请先启用并读取当前英雄" };
    private readonly AntdUI.Button _refresh = new() { Name = "SkinActionRefresh", Text = "读取当前英雄", Width = 146, Height = 42, Margin = new Padding(0, 8, 12, 4) };
    private readonly AntdUI.Button _apply = new() { Name = "SkinActionApply", Text = "应用所选皮肤", Width = 146, Height = 42, Margin = new Padding(0, 8, 12, 4), Type = AntdUI.TTypeMini.Primary };
    private readonly AntdUI.Button _restore = new() { Name = "SkinActionRestore", Text = "恢复基础皮肤", Width = 146, Height = 42, Margin = new Padding(0, 8, 0, 4) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 8, 0, 0), Text = "功能已关闭。启用后可读取当前对局。" };
    private readonly AntdUI.Label _current = new() { Dock = DockStyle.Top, Height = 68, Padding = new Padding(14, 8, 14, 8), Font = new Font(UiMetrics.FontFamily, 10.5f), Text = "尚未连接游戏" };
    private readonly AntdUI.Panel _card = new() { Dock = DockStyle.Top, Height = 472, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
    private readonly List<Control> _surfaceRows = [];
    private readonly List<Control> _pageSurfaces = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private CancellationTokenSource? _pageRequests;
    private SkinCoreReply? _catalog;
    private List<SkinEntry> _filtered = [];
    private bool _busy;
    private bool _refreshing;
    private bool _disposing;
    private long _revision;

    public SkinToolsForm(ISkinCoreService core, IApplicationSettingsStore settings)
    {
        _core = core; _settings = settings; Dock = DockStyle.Fill;
        var viewport = new AntdUI.Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = Padding.Empty, Radius = 0 };
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, Height = 56, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        heading.Controls.Add(new AntdUI.Label { Text = "游戏换肤", Anchor = AnchorStyles.Left, Height = 40, Width = 200, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
        var toggleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 12, 0, 0), Margin = Padding.Empty };
        toggleRow.Controls.Add(new AntdUI.Label { Text = "启用独立核心", Width = 120, Height = 32 }); toggleRow.Controls.Add(_enabled);
        heading.Controls.Add(toggleRow, 1, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, AutoSize = false, WrapContents = true, Margin = Padding.Empty };
        buttons.Controls.AddRange([_refresh, _apply, _restore]);
        var gap = new AntdUI.Label { Dock = DockStyle.Top, Height = 8, Text = "" };
        var selectionLabel = new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Text = "选择皮肤与形态" };
        var stateGap = new AntdUI.Label { Dock = DockStyle.Top, Height = 16, Text = "" };
        var searchGap = new AntdUI.Label { Dock = DockStyle.Top, Height = 8, Text = "" };
        _card.Controls.Add(_status); _card.Controls.Add(buttons); _card.Controls.Add(gap);
        _card.Controls.Add(_entries); _card.Controls.Add(selectionLabel); _card.Controls.Add(searchGap);
        _card.Controls.Add(_search); _card.Controls.Add(stateGap); _card.Controls.Add(_current); _card.Controls.Add(heading);
        _surfaceRows.AddRange([heading, toggleRow, buttons, gap, selectionLabel, stateGap, searchGap]);
        var notice = new AntdUI.Label { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8, 16, 8, 8), Text = "实验性功能：游戏更新后可能需要重新适配。第三方换肤存在账号处罚风险。\n关闭开关停止新的请求，不会卸载已加载的游戏模块或自动恢复皮肤。" };
        var content = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 472));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 314));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        _card.Dock = DockStyle.Fill; _card.Margin = Padding.Empty;
        content.Controls.Add(_card, 0, 0); content.Controls.Add(CreateHotkeyCard(), 0, 1); content.Controls.Add(notice, 0, 2);
        _pageSurfaces.AddRange([viewport, content, notice]);
        void PositionContent()
        {
            int width = Math.Max(360, Math.Min(1040, viewport.ClientSize.Width - 40));
            content.SetBounds(Math.Max(20, (viewport.ClientSize.Width - width) / 2), 20, width, 886);
        }
        viewport.Controls.Add(content); viewport.Resize += (_, _) => PositionContent(); PositionContent(); Controls.Add(viewport);
        _enabled.Checked = settings.Load().SkinCoreEnabled;
        _core.SetEnabled(_enabled.Checked);
        _enabled.CheckedChanged += (_, _) => ToggleEnabled();
        _search.TextChanged += (_, _) => FilterEntries();
        _entries.SelectedIndexChanged += (_, _) => UpdateButtons();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _apply.Click += async (_, _) => await ChangeAsync(false);
        _restore.Click += async (_, _) => await ChangeAsync(true);
        _timer.Tick += async (_, _) => await RefreshAsync();
        VisibleChanged += PageVisibleChanged;
        UiLanguage.Changed += LanguageChanged;
        UiTheme.Apply(this); UpdateButtons();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (!_disposing && !IsDisposed) FilterEntries();
    }
    private void PageVisibleChanged(object? sender, EventArgs e) => UpdateActivity();
    private void CancelPageRequests()
    {
        // Remove the owned reference before cancellation can trigger callbacks.
        var requests = _pageRequests; _pageRequests = null;
        if (requests == null) return;
        try { requests.Cancel(); }
        finally { requests.Dispose(); }
    }
    private void ToggleEnabled()
    {
        try { var settings = _settings.Load(); settings.SkinCoreEnabled = _enabled.Checked; _settings.Save(settings); }
        catch (Exception ex) { _status.Text = $"设置保存失败：{ex.Message}"; }
        _core.SetEnabled(_enabled.Checked); ResetSession(); UpdateActivity();
        RenewHotkeyRequests(); HotkeySettingsChanged?.Invoke();
        _status.Text = UiLanguage.T(_enabled.Checked ? "功能已启用，等待读取当前对局。" : "功能已关闭；已开始的请求可能已执行，当前皮肤不会自动还原。");
    }
    private void UpdateActivity()
    {
        if (_disposing || Disposing || IsDisposed) return;
        _timer.Stop(); CancelPageRequests();
        Interlocked.Increment(ref _revision); _core.InvalidateSession();
        if (Visible && _core.Enabled && !IsDisposed)
        {
            _pageRequests = new CancellationTokenSource(); _timer.Start();
            _ = RefreshAsync();
        }
        UpdateButtons();
    }
    public void OnGamePhaseChanged(bool inProgress)
    {
        if (!inProgress) { ResetSession(); _core.InvalidateSession(); RenewHotkeyRequests(); }
    }
    private void ResetSession()
    {
        Interlocked.Increment(ref _revision); _catalog = null; _filtered.Clear(); _entries.Items.Clear(); _entries.SelectedIndex = -1;
        _current.Text = UiLanguage.T("尚未连接游戏"); UpdateButtons();
    }
    private void FilterEntries()
    {
        string? previous = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex].EntryId : null;
        string query = _search.Text.Trim();
        _filtered = _catalog?.Entries.Where(e => $"{e.Name} {e.Model} {e.SkinNum} {e.GearName}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        _entries.Items.Clear();
        foreach (var entry in _filtered) _entries.Items.Add($"{entry.Name}  ·  {entry.Model}  ·  #{entry.SkinNum}" + (entry.GearName == null ? "" : $"  ·  {entry.GearName}"));
        _entries.SelectedIndex = previous == null ? -1 : _filtered.FindIndex(e => e.EntryId == previous);
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (_disposing || IsDisposed) return;
        bool ready = _core.Enabled && !_busy && Visible && _catalog != null;
        _refresh.Enabled = _core.Enabled && !_busy && !_refreshing && Visible;
        _search.Enabled = _core.Enabled; _entries.Enabled = ready;
        _apply.Enabled = ready && !_refreshing && _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count;
        _restore.Enabled = ready && !_refreshing;
    }
    private async Task RefreshAsync()
    {
        if (_disposing || _busy || _refreshing || !Visible || !_core.Enabled || _pageRequests == null) return;
        _refreshing = true; long revision = Interlocked.Read(ref _revision); CancellationToken token = _pageRequests.Token; UpdateButtons();
        try
        {
            var result = await _core.GetCatalogAsync(token);
            if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
            bool changed = _catalog?.Session != result.Session;
            _catalog = result;
            _current.Text = $"{result.Model}  ·  {result.Entries.Count} {UiLanguage.T("个皮肤与形态")}\n{UiLanguage.T("当前状态")}: #{result.Skin}  ·  {result.ActiveModel}  ·  {UiLanguage.T("形态")} {result.Gear}";
            if (changed) { _entries.SelectedIndex = -1; FilterEntries(); _status.Text = UiLanguage.T("已连接。请选择条目；状态核验不替代画面和技能验收。"); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && revision == Interlocked.Read(ref _revision)) { ResetSession(); ShowError(ex); } }
        finally { _refreshing = false; if (!IsDisposed) UpdateButtons(); }
    }
    private async Task ChangeAsync(bool restore)
    {
        if (_disposing || _busy || _refreshing || _catalog == null || _pageRequests == null || !_core.Enabled) return;
        if (!restore && (_entries.SelectedIndex < 0 || _entries.SelectedIndex >= _filtered.Count)) return;
        string session = _catalog.Session; string entry = restore ? "" : _filtered[_entries.SelectedIndex].EntryId;
        long revision = Interlocked.Read(ref _revision); CancellationToken token = _pageRequests.Token; _busy = true; UpdateButtons();
        _status.Text = UiLanguage.T("请求执行中…");
        try
        {
            var result = restore ? await _core.RestoreAsync(session, token) : await _core.ApplyAsync(session, entry, token);
            if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
            _status.Text = UiLanguage.T(result.StateVerified ? "调用完成，实际状态已核验。画面、特效与技能仍需检查。" : "调用完成，但实际状态核验未通过。请刷新，勿连续重试。");
            _current.Text = $"{result.Model}  ·  #{result.Skin}  ·  {result.ActiveModel}  ·  {result.Gear}";
            if (!result.StateVerified) { _catalog = null; _entries.SelectedIndex = -1; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && revision == Interlocked.Read(ref _revision)) { ResetSession(); ShowError(ex); } }
        finally { _busy = false; if (!IsDisposed) UpdateButtons(); }
    }
    private void ShowError(Exception error)
    {
        string message = error is SkinCoreException uncertain && uncertain.OutcomeUnknown
            ? "请求结果未确认，动作可能已执行。请读取状态，勿自动重试。"
            : error is SkinCoreException core ? core.Code switch
        {
            "core_files_missing" => "核心文件缺失，请重新安装完整运行包。",
            "game_window_not_found" => "未检测到游戏对局，请进入游戏后刷新。",
            "independent_game_version_not_supported" => "当前游戏版本尚未适配，已停止调用。",
            "request_outcome_unknown" => "请求结果未确认，动作可能已执行。请读取状态，勿自动重试。",
            "standard_windows_hook_rejected" => "Windows 拒绝核心连接，未继续加载。",
            "stale_session" => "对局已变化，请重新读取当前英雄。",
            _ => $"核心连接失败：{core.Code}"
        } : $"操作失败：{error.Message}";
        _status.Text = UiLanguage.T(message);
    }
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface; ForeColor = palette.TextPrimary;
        _card.Back = palette.SurfaceRaised; _card.BackColor = palette.SurfaceRaised; _card.ForeColor = palette.TextPrimary; _card.BorderColor = palette.Border;
        foreach (var row in _surfaceRows) row.BackColor = palette.SurfaceRaised;
        foreach (var surface in _pageSurfaces) surface.BackColor = palette.Surface;
        void ThemeCardLabels(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is AntdUI.Label or AntdUI.Switch) child.BackColor = palette.SurfaceRaised;
                ThemeCardLabels(child);
            }
        }
        ThemeCardLabels(_card);
        if (_hotkeyCard != null) ThemeCardLabels(_hotkeyCard);
        _status.BackColor = palette.SurfaceRaised; _status.ForeColor = palette.TextSecondary;
        _current.BackColor = palette.SurfaceMuted; _current.ForeColor = palette.TextPrimary;
        foreach (var input in new[] { _previousKey, _nextKey, _applyKey })
        {
            input.BackColor = palette.SurfaceMuted; input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
        }
        if (_hotkeyCard != null) { _hotkeyCard.Back = palette.SurfaceRaised; _hotkeyCard.BackColor = palette.SurfaceRaised; _hotkeyCard.BorderColor = palette.Border; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposing)
        {
            _disposing = true;
            Interlocked.Increment(ref _revision);
            VisibleChanged -= PageVisibleChanged;
            UiLanguage.Changed -= LanguageChanged;
            _timer.Stop(); CancelPageRequests(); _timer.Dispose();
            CancelHotkeyRequests();
            _core.InvalidateSession();
        }
        base.Dispose(disposing);
    }
}
