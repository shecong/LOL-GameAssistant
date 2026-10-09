using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>AntdUI manual skin selection. Merely creating this page never loads the game module.</summary>
public sealed class SkinToolsForm : UserControl, IThemeAware
{
    private readonly ISkinCoreService _core;
    private readonly IApplicationSettingsStore _settings;
    private readonly AntdUI.Switch _enabled = new() { Width = 52, Height = 30 };
    private readonly AntdUI.Input _search = new() { Dock = DockStyle.Top, Height = 48, PlaceholderText = "搜索皮肤名称或模型" };
    private readonly AntdUI.Select _entries = new() { Dock = DockStyle.Top, Height = 56, List = true, MaxCount = 12,
        DropDownArrow = true, DropDownRadius = 10, Font = new Font(UiMetrics.FontFamily, 11), PlaceholderText = "请先启用并读取当前英雄" };
    private readonly AntdUI.Button _refresh = new() { Text = "读取当前英雄", Width = 146, Height = 42, Margin = new Padding(0, 8, 12, 4) };
    private readonly AntdUI.Button _apply = new() { Text = "应用所选皮肤", Width = 146, Height = 42, Margin = new Padding(0, 8, 12, 4), Type = AntdUI.TTypeMini.Primary };
    private readonly AntdUI.Button _restore = new() { Text = "恢复基础皮肤", Width = 146, Height = 42, Margin = new Padding(0, 8, 0, 4) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 8, 0, 0), Text = "功能已关闭。启用后可读取当前对局。" };
    private readonly AntdUI.Label _current = new() { Dock = DockStyle.Top, Height = 68, Padding = new Padding(14, 8, 14, 8), Font = new Font(UiMetrics.FontFamily, 10.5f), Text = "尚未连接游戏" };
    private readonly AntdUI.Panel _card = new() { Dock = DockStyle.Top, Height = 456, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
    private readonly List<Control> _surfaceRows = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private CancellationTokenSource? _pageRequests;
    private SkinCoreReply? _catalog;
    private List<SkinEntry> _filtered = [];
    private bool _busy;
    private long _revision;

    public SkinToolsForm(ISkinCoreService core, IApplicationSettingsStore settings)
    {
        _core = core; _settings = settings; Dock = DockStyle.Fill;
        var viewport = new AntdUI.Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), Radius = 0 };
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, Height = 56, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        heading.Controls.Add(new AntdUI.Label { Text = "游戏换肤", Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
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
        viewport.Controls.Add(notice); viewport.Controls.Add(_card); Controls.Add(viewport);
        _enabled.Checked = settings.Load().SkinCoreEnabled;
        _core.SetEnabled(_enabled.Checked);
        _enabled.CheckedChanged += (_, _) => ToggleEnabled();
        _search.TextChanged += (_, _) => FilterEntries();
        _entries.SelectedIndexChanged += (_, _) => UpdateButtons();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _apply.Click += async (_, _) => await ChangeAsync(false);
        _restore.Click += async (_, _) => await ChangeAsync(true);
        _timer.Tick += async (_, _) => await RefreshAsync();
        VisibleChanged += (_, _) => UpdateActivity();
        UiLanguage.Changed += LanguageChanged;
        UiTheme.Apply(this); UpdateButtons();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed) FilterEntries();
    }
    private void ToggleEnabled()
    {
        try { var settings = _settings.Load(); settings.SkinCoreEnabled = _enabled.Checked; _settings.Save(settings); }
        catch (Exception ex) { _status.Text = $"设置保存失败：{ex.Message}"; }
        _core.SetEnabled(_enabled.Checked); ResetSession(); UpdateActivity();
        _status.Text = UiLanguage.T(_enabled.Checked ? "功能已启用，等待读取当前对局。" : "功能已关闭；已开始的请求可能已执行，当前皮肤不会自动还原。");
    }
    private void UpdateActivity()
    {
        _timer.Stop(); _pageRequests?.Cancel(); _pageRequests?.Dispose(); _pageRequests = null;
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
        if (!inProgress) { ResetSession(); _core.InvalidateSession(); }
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
        bool ready = _core.Enabled && !_busy && Visible && _catalog != null;
        _refresh.Enabled = _core.Enabled && !_busy && Visible;
        _search.Enabled = _core.Enabled; _entries.Enabled = ready;
        _apply.Enabled = ready && _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count;
        _restore.Enabled = ready;
    }
    private async Task RefreshAsync()
    {
        if (_busy || !Visible || !_core.Enabled || _pageRequests == null) return;
        _busy = true; long revision = Interlocked.Read(ref _revision); CancellationToken token = _pageRequests.Token; UpdateButtons();
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
        finally { _busy = false; if (!IsDisposed) UpdateButtons(); }
    }
    private async Task ChangeAsync(bool restore)
    {
        if (_busy || _catalog == null || _pageRequests == null || !_core.Enabled) return;
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
        _card.BackColor = palette.SurfaceRaised; _card.ForeColor = palette.TextPrimary;
        foreach (var row in _surfaceRows) row.BackColor = palette.SurfaceRaised;
        _status.BackColor = palette.SurfaceRaised; _status.ForeColor = palette.TextSecondary;
        _current.BackColor = palette.SurfaceMuted; _current.ForeColor = palette.TextPrimary;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); _pageRequests?.Cancel(); _pageRequests?.Dispose();
            _core.InvalidateSession(); UiLanguage.Changed -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
