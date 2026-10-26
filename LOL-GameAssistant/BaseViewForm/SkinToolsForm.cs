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
    private readonly AntdUI.Input _search = new() { Name = "SkinSearch", Dock = DockStyle.Top, Height = 48, PlaceholderText = "搜索皮肤、品质或模型" };
    private readonly AntdUI.Select _entries = new() { Name = "HeroSkinEntries", Dock = DockStyle.Top, Height = 56, List = true, MaxCount = 12,
        DropDownArrow = true, DropDownRadius = 10, AutoText = false, Font = new Font(UiMetrics.FontFamily, 11), PlaceholderText = "请先启用并读取当前英雄" };
    private readonly AntdUI.Button _refresh = new() { Name = "SkinActionRefresh", Text = "读取当前英雄", Width = 146, Height = 42, Margin = new Padding(0, 8, 12, 4) };
    private readonly AntdUI.Button _restore = new() { Name = "SkinActionRestore", Text = "恢复基础皮肤", Width = 146, Height = 42, Margin = new Padding(0, 8, 0, 4) };
    private readonly AntdUI.Button _stop = new() { Name = "SkinActionStop", Text = "恢复原皮并停用", Width = 146, Height = 42, Margin = new Padding(12, 8, 0, 4) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(0, 8, 0, 0), Text = "功能已关闭。启用后可读取当前对局。" };
    private readonly AntdUI.Label _current = new() { Dock = DockStyle.Top, Height = 68, Padding = new Padding(14, 8, 14, 8), Font = new Font(UiMetrics.FontFamily, 10.5f), Text = "尚未连接游戏" };
    private readonly AntdUI.Panel _card = new() { Dock = DockStyle.Top, Height = 472, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
    private readonly List<Control> _surfaceRows = [];
    private readonly List<Control> _pageSurfaces = [];
    private CancellationTokenSource? _pageRequests;
    private SkinCoreReply? _catalog;
    private List<SkinEntry> _filtered = [];
    private bool _busy;
    private bool _updatingSelection;
    private bool _refreshing;
    private bool _disposing;
    private long _revision;
    private bool _inProgress;
    private readonly HashSet<string> _rememberedSessions = [];

    public SkinToolsForm(ISkinCoreService core, IApplicationSettingsStore settings,
        IChampionSkinResourceService? skinResources = null, LOL_GameAssistant.Application.GameData.IGameOverlayBridge? cameraOverlay = null,
        IClientRegaliaService? clientRegalia = null)
    {
        _core = core; _settings = settings; _skinResources = skinResources ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.ChampionSkinResourceService; Dock = DockStyle.Fill;
        _clientRegalia = clientRegalia ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.ClientRegaliaService;
        _cameraOverlay = cameraOverlay ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.GameOverlay;
        var viewport = new AntdUI.Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = Padding.Empty, Radius = 0 };
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, Height = 56, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        heading.Controls.Add(new AntdUI.Label { Text = "游戏换肤", Anchor = AnchorStyles.Left, Height = 40, Width = 200, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
        var toggleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 12, 0, 0), Margin = Padding.Empty };
        toggleRow.Controls.Add(new AntdUI.Label { Text = "启用独立核心", Width = 120, Height = 32 }); toggleRow.Controls.Add(_enabled);
        heading.Controls.Add(toggleRow, 1, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
        buttons.Controls.AddRange([_refresh, _restore, _stop]);
        var gap = new AntdUI.Label { Dock = DockStyle.Top, Height = 8, Text = "" };
        var selectionLabel = new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Text = "选择皮肤与形态" };
        var stateGap = new AntdUI.Label { Dock = DockStyle.Top, Height = 16, Text = "" };
        var searchGap = new AntdUI.Label { Dock = DockStyle.Top, Height = 8, Text = "" };
        _card.Controls.Add(_status); _card.Controls.Add(buttons); _card.Controls.Add(gap);
        _card.Controls.Add(_entries); _card.Controls.Add(selectionLabel); _card.Controls.Add(searchGap);
        _card.Controls.Add(_search); _card.Controls.Add(stateGap); _card.Controls.Add(_current); _card.Controls.Add(CreateCoreModeRow()); _card.Controls.Add(heading);
        _surfaceRows.AddRange([heading, toggleRow, buttons, gap, selectionLabel, stateGap, searchGap]);
        var notice = new AntdUI.Label { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8, 16, 8, 8), Text = "实验性功能：游戏更新后可能需要重新适配。第三方换肤存在账号处罚风险。\n关闭开关停止新的请求，不会卸载已加载的游戏模块或自动恢复皮肤。" };
        var content = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 592));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 356));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        _card.Dock = DockStyle.Fill; _card.Margin = Padding.Empty;
        content.Controls.Add(_card, 0, 0); content.Controls.Add(CreateHotkeyCard(), 0, 1); content.Controls.Add(notice, 0, 2);
        _pageSurfaces.AddRange([viewport, content, notice]);
        void PositionContent()
        {
            int width = Math.Max(360, viewport.ClientSize.Width - 24);
            int skinHeight = width < 560 ? 648 : 592;
            content.RowStyles[0].Height = skinHeight;
            content.SetBounds(12, 12, width, skinHeight + 456);
        }
        viewport.Controls.Add(content); viewport.Resize += (_, _) => PositionContent(); PositionContent(); BuildSkinNavigation(viewport);
        var initialSettings = settings.Load();
        if (initialSettings.UnlimitedZoomEnabled && !initialSettings.SkinCoreEnabled)
        {
            initialSettings.SkinCoreEnabled = true;
            settings.Save(initialSettings);
        }
        _enabled.Checked = initialSettings.SkinCoreEnabled;
        _core.SetEnabled(_enabled.Checked);
        InitializeCameraAutomation();
        _enabled.CheckedChanged += (_, _) => ToggleEnabled();
        _search.TextChanged += (_, _) => FilterEntries();
        _entries.DrawItem += (_, e) => { e.Fore = e.Item.Fore; e.ForeSub = UiTheme.Palette.TextPrimary; };
        _entries.SelectedIndexChanged += async (_, _) =>
        {
            UpdateSkinQualityColors(UiTheme.Palette);
            UpdateButtons();
            if (!_updatingSelection) { await ChangeAsync(false); await UpdatePreviewAsync(); }
        };
        _refresh.Click += async (_, _) => await RefreshAsync();
        _restore.Click += async (_, _) => await ChangeAsync(true);
        _stop.Click += async (_, _) => await StopAsync();
        VisibleChanged += PageVisibleChanged;
        UiLanguage.Changed += LanguageChanged;
        UiTheme.Apply(this); UpdateButtons();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (!_disposing && !IsDisposed) FilterEntries();
    }
    private void PageVisibleChanged(object? sender, EventArgs e) { UpdateButtons(); if (Visible) _ = UpdatePreviewAsync(); }
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
        try
        {
            var settings = _settings.Load(); settings.SkinCoreEnabled = _enabled.Checked;
            if (!_enabled.Checked) { settings.UnlimitedZoomEnabled = false; SetCameraToggle(false); }
            _settings.Save(settings);
        }
        catch (Exception ex) { _status.Text = $"设置保存失败：{ex.Message}"; }
        _skinNotice?.Hide(); _core.SetEnabled(_enabled.Checked); ResetSession(); UpdateActivity();
        HotkeySettingsChanged?.Invoke();
        _status.Text = UiLanguage.T(_enabled.Checked ? "功能已启用，等待读取当前对局。" : "功能已关闭；已开始的请求可能已执行，当前皮肤不会自动还原。");
    }
    private void UpdateActivity()
    {
        if (_disposing || Disposing || IsDisposed) return;
        CancelPageRequests();
        Interlocked.Increment(ref _revision); _core.InvalidateSession();
        RenewHotkeyRequests();
        if (_inProgress && _core.Enabled)
        {
            _pageRequests = new CancellationTokenSource();
            _ = RefreshAsync();
        }
        UpdateButtons();
    }
    public void OnGamePhaseChanged(bool inProgress)
    {
        if (_inProgress == inProgress) return;
        _inProgress = inProgress;
        if (!inProgress) { _skinNotice?.Hide(); ResetSession(); }
        UpdateActivity();
        if (!inProgress && _core.Enabled) _status.Text = UiLanguage.T("当前不在对局中，等待读取下一次对局状态。");
    }
    private void ResetSession()
    {
        _previewCancellation?.Cancel();
        _previewKey = "";
        _clientSkins.Clear();
        _poster.Image?.Dispose(); _poster.Image = null;
        _posterCaption.Text = "连接游戏后显示当前英雄皮肤海报";
        Interlocked.Increment(ref _revision); _catalog = null; _filtered.Clear(); _entries.Items.Clear(); _entries.SelectedIndex = -1;
        _objectCatalog = null;
        ResetRankBorder();
        ResetCameraSelection();
        ResetObjectSelection(); _skinPickerCategory = SkinPickerCategory.Hero;
        _current.Text = UiLanguage.T("尚未连接游戏"); UpdateButtons();
    }
    private void FilterEntries()
    {
        string? previous = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex].EntryId : null;
        string query = _search.Text.Trim();
        _filtered = _catalog?.Entries.Where(e => $"{GetSkinOptionText(e)} {e.Name} {e.Model} {e.SkinNum} {e.GearName}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        bool wasUpdating = _updatingSelection;
        _updatingSelection = true;
        try
        {
            bool unchanged = _entries.Items.Count == _filtered.Count && _filtered.Select((entry, i) =>
                _entries.Items[i] is AntdUI.SelectItem item && Equals(item.Tag, entry.EntryId)
                && item.Text == GetSkinQualityPrefix(entry) && item.SubText == GetSkinDisplayName(entry)).All(match => match);
            if (!unchanged)
            {
                _entries.Items.Clear();
                foreach (var entry in _filtered) _entries.Items.Add(new AntdUI.SelectItem(GetSkinQualityPrefix(entry), entry.EntryId)
                    { SubText = GetSkinDisplayName(entry), Fore = SkinQualityStyle.Foreground(GetSkinQuality(entry), UiTheme.Palette), ForeSub = UiTheme.Palette.TextPrimary });
                _entries.SelectedIndex = previous == null ? -1 : _filtered.FindIndex(e => e.EntryId == previous);
            }
        }
        finally { _updatingSelection = wasUpdating; }
        UpdateSkinQualityColors(UiTheme.Palette);
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (_disposing || IsDisposed) return;
        UpdateSkinPicker();
        bool ready = _inProgress && _core.Enabled && !_busy && Visible && _catalog != null;
        _refresh.Enabled = _inProgress && _core.Enabled && !_busy && !_refreshing && Visible;
        _search.Enabled = _core.Enabled; _entries.Enabled = ready;
        _restore.Enabled = ready && !_refreshing;
        _stop.Enabled = ready && !_refreshing;
        UpdateObjectButtons();
        UpdateCameraButtons();
        UpdateCoreModeControls();
        UpdateRankBorderButtons();
    }
    private async Task StopAsync()
    {
        if (_disposing || !_inProgress || _busy || _refreshing || _catalog == null || _pageRequests == null || !_core.Enabled) return;
        string session = _catalog.Session; CancellationToken token = _pageRequests.Token;
        _busy = true; _enabled.Enabled = false; CancelHotkeyRequests(); UpdateButtons();
        _status.Text = UiLanguage.T("正在恢复并停用…");
        string outcome = "功能已停用，但恢复结果未确认。请退出对局释放驻留核心。";
        try {
            var result = await _core.StopAsync(session, token);
            if (result.StateVerified) RememberSkin(_catalog, null);
            outcome = BuildStopMessage(result);
        }
        catch (Exception error) {
            if (!_disposing && !IsDisposed) { ShowError(error); outcome = _status.Text + "\n" + UiLanguage.T("功能已停用，但恢复结果未确认。请退出对局释放驻留核心。"); }
        }
        finally {
            _core.SetEnabled(false);
            if (!_disposing && !IsDisposed) {
                _enabled.Checked = false; _enabled.Enabled = true; _busy = false;
                ResetSession(); UpdateButtons(); _status.Text = UiLanguage.T(outcome);
            }
        }
    }
    internal static string BuildStopMessage(SkinCoreReply result)
    {
        bool hero = result.HeroRestoreVerified ?? result.StateVerified;
        bool objects = result.ObjectRestoreVerified ?? true;
        string restored = hero && objects ? "本局原皮恢复已核验，功能已停用。"
            : hero ? "英雄原皮已恢复，对象原皮恢复未核验；功能已停用。"
            : "英雄原皮恢复未核验；功能已停用，请检查游戏画面。";
        if (result.CameraRestoreVerified == false) restored += "视距恢复未核验，请检查游戏镜头。";
        if (result.RankBorderRestoreVerified == false) restored += "段位边框恢复未核验，请检查游戏画面。";
        if (result.ModuleUnloadVerified == true && result.ModuleLoaded == false)
            return restored + "游戏内核心已确认卸载。";
        if (result.ModulePinned == true)
            return restored + "核心已固定驻留，请退出对局释放。";
        if (result.ModuleLoaded == true)
            return restored + "核心仍在游戏进程内，请退出对局释放。";
        return restored + "游戏内核心卸载尚未核验。";
    }
    private async Task RefreshAsync()
    {
        if (_disposing || !_inProgress || _busy || _refreshing || !_core.Enabled || _pageRequests == null) return;
        _refreshing = true; long revision = Interlocked.Read(ref _revision); CancellationToken token = _pageRequests.Token; UpdateButtons();
        try
        {
            var result = await _core.GetCatalogAsync(token);
            if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
            bool changed = _catalog?.Session != result.Session;
            _catalog = result;
            _current.Text = $"{result.Model}  ·  {result.Entries.Count} {UiLanguage.T("个皮肤与形态")}\n{UiLanguage.T("当前状态")}: #{result.Skin}  ·  {result.ActiveModel}  ·  {UiLanguage.T("形态")} {result.Gear}";
            if (changed) { _entries.SelectedIndex = -1; FilterEntries(); _status.Text = UiLanguage.T("已连接。请选择条目；状态核验不替代画面和技能验收。"); }
            await ApplyRememberedSkinAsync(result, revision, token);
            _ = UpdatePreviewAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && revision == Interlocked.Read(ref _revision)) { ResetSession(); ShowError(ex); } }
        finally { _refreshing = false; if (!IsDisposed) UpdateButtons(); }
    }
    private async Task ChangeAsync(bool restore)
    {
        if (_disposing || !_inProgress || _busy || _refreshing || _catalog == null || _pageRequests == null || !_core.Enabled) return;
        if (!restore && (_entries.SelectedIndex < 0 || _entries.SelectedIndex >= _filtered.Count)) return;
        string session = _catalog.Session; string entry = restore ? "" : _filtered[_entries.SelectedIndex].EntryId;
        var catalog = _catalog;
        SkinEntry? selected = restore ? null : _filtered[_entries.SelectedIndex];
        long revision = Interlocked.Read(ref _revision); CancellationToken token = _pageRequests.Token; _busy = true; UpdateButtons();
        _status.Text = UiLanguage.T("请求执行中…");
        try
        {
            var result = restore ? await _core.RestoreAsync(session, token) : await _core.ApplyAsync(session, entry, token);
            if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
            _status.Text = UiLanguage.T(result.StateVerified ? "调用完成，实际状态已核验。画面、特效与技能仍需检查。" : "调用完成，但实际状态核验未通过。请刷新，勿连续重试。");
            _current.Text = $"{result.Model}  ·  #{result.Skin}  ·  {result.ActiveModel}  ·  {result.Gear}";
            if (result.StateVerified)
            {
                RememberSkin(catalog, selected);
                _catalog = result with { Entries = catalog.Entries };
                if (restore)
                {
                    _updatingSelection = true;
                    try { _entries.SelectedIndex = -1; }
                    finally { _updatingSelection = false; }
                }
                _ = UpdatePreviewAsync();
            }
            if (!result.StateVerified) { _catalog = null; _entries.SelectedIndex = -1; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && revision == Interlocked.Read(ref _revision)) { ResetSession(); ShowError(ex); } }
        finally { _busy = false; if (!IsDisposed) UpdateButtons(); }
    }
    private void RememberSkin(SkinCoreReply catalog, SkinEntry? entry)
    {
        _rememberedSessions.Add(catalog.Session);
        try
        {
            var settings = _settings.Load();
            if (entry == null) settings.RememberedHeroSkins.Remove(catalog.Model);
            else settings.RememberedHeroSkins[catalog.Model] = entry;
            _settings.Save(settings);
        }
        catch (Exception error) { _status.Text += $"\n皮肤记忆保存失败：{error.Message}"; }
    }

    private async Task ApplyRememberedSkinAsync(SkinCoreReply catalog, long revision, CancellationToken token)
    {
        // Attempt once per game, including uncertain outcomes; polling must never repeat a write.
        if (!_rememberedSessions.Add(catalog.Session)) return;
        if (!_settings.Load().RememberedHeroSkins.TryGetValue(catalog.Model, out var remembered) || remembered == null) return;
        // Entry indices may change between game versions. Match the actual skin and form instead.
        var entry = catalog.Entries.FirstOrDefault(e => e.Model == remembered.Model && e.SkinNum == remembered.SkinNum && e.Gear == remembered.Gear);
        if (entry == null) { _status.Text = "上次选择的皮肤已不在当前目录中，请重新选择。"; return; }
        _updatingSelection = true;
        try { _search.Text = ""; FilterEntries(); _entries.SelectedIndex = _filtered.FindIndex(e => e.EntryId == entry.EntryId); }
        finally { _updatingSelection = false; }
        if (catalog.ActiveModel == entry.Model && catalog.Skin == entry.SkinNum && catalog.Gear == entry.Gear) return;
        var result = await _core.ApplyAsync(catalog.Session, entry.EntryId, token);
        if (_disposing || IsDisposed || token.IsCancellationRequested || revision != Interlocked.Read(ref _revision)) return;
        _current.Text = $"{result.Model}  ·  #{result.Skin}  ·  {result.ActiveModel}  ·  {result.Gear}";
        _status.Text = result.StateVerified ? $"已自动应用上次选择：{GetSkinDisplayName(entry)}" : "上次皮肤的应用状态未核验，请读取状态后检查。";
    }

    private void ShowError(Exception error)
    {
        string message = error is SkinCoreException uncertain && uncertain.OutcomeUnknown
            ? "请求结果未确认，动作可能已执行。请读取状态，勿自动重试。"
            : error is SkinCoreException core ? core.Code switch
        {
            "core_files_missing" => "核心文件缺失，请重新安装完整运行包。",
            "hybrid_files_missing" => "混合模式文件缺失，请先构建并部署 SkinCoreHybrid 运行文件。",
            "kernel_driver_unavailable" => "驱动通道不可用。请确认 SkinCoreRelay 已安装并启动，且以管理员身份运行助手；可在退出对局并关闭功能后切回经典模式。",
            "kernel_protocol_mismatch" => "驱动协议版本不匹配，请安装配套版本的驱动。",
            "kernel_submit_failed" => "驱动未接受请求，本次未执行换肤。",
            "game_window_not_found" => "未检测到游戏对局，请进入游戏后刷新。",
            "independent_game_version_not_supported" => "当前游戏版本尚未适配，已停止调用。",
            "independent_skin_layer_not_verified" => "当前皮肤层或模型不兼容，已停止换肤。请恢复正常形态后重新读取。",
            "request_outcome_unknown" => "请求结果未确认，动作可能已执行。请读取状态，勿自动重试。",
            "standard_windows_hook_rejected" => "Windows 拒绝核心连接，未继续加载。",
            "message_post_failed" when core.Details?.Win32 == 5 => "核心消息被拒绝访问。请检查助手与游戏的权限级别，以及网吧管控策略。",
            "message_post_failed" => "向游戏线程发送消息失败，请查看诊断日志中的 Win32 错误码。",
            "core_response_timeout" => "游戏端未在期限内返回响应，请待对局加载稳定后读取；若持续出现，请检查诊断日志。",
            "host_response_timeout" => "核心宿主超过 9 秒未完成，请检查机器负载与核心运行情况。",
            "response_wait_failed" => "等待核心响应失败，请查看诊断日志。",
            "request_expired" => "核心请求已过期，请待游戏加载稳定后重新读取。",
            "host_start_failed" when core.Details?.Win32 is 193 or 216 => "核心宿主无法启动，文件格式或系统架构不兼容，请检查完整运行包与 Windows 位数。",
            "host_start_failed" => "核心宿主启动失败，请检查运行包和诊断日志。",
            "load_own_bridge_failed" => "核心 DLL 加载失败，请检查运行包完整性、文件位数与访问权限。",
            "stale_session" => "对局已变化，请重新读取当前英雄。",
            "hero_baseline_unavailable" => "没有可恢复的本局原始皮肤记录；功能已停用，请退出对局释放核心。",
            _ => $"核心连接失败：{core.Code}"
        } : $"操作失败：{error.Message}";
        if (error is SkinCoreException { Details: { } details } diagnosed)
        {
            if (diagnosed.Code == "game_window_not_found" && details.WindowDiscovery is { } discovery)
                message = RuntimeDiagnostics.DescribeGameWindow(discovery);
            if (!diagnosed.OutcomeUnknown && details.TargetProbe is { } probe) message += $"\n自动检查：{probe.Assessment}";
            message += $"\n诊断：{diagnosed.Code}";
            if (!string.IsNullOrEmpty(details.Stage)) message += $" · {details.Stage}";
            if (details.Win32 is int win32) message += $" · Win32={win32}";
            message += $" · {details.ElapsedMs}ms";
            message += "\n详情已记录，请打开软件「日志 → 运行日志」，点击「复制日志」反馈。";
        }
        _status.Text = UiLanguage.T(message);
    }
    public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette);
        BackColor = palette.Surface; ForeColor = palette.TextPrimary;
        _skinNavigation.BackColor = palette.SurfaceMuted;
        _skinNavigation.ForeColor = palette.TextPrimary;
        foreach (var button in new[] { _heroSkinTab, _objectSkinTab, _rankBorderTab, _otherSettingsTab })
        {
            button.BackColor = palette.Accent;
            button.DefaultBack = palette.SurfaceRaised;
            button.DefaultBorderColor = palette.Border;
            button.ForeColor = button.Type == AntdUI.TTypeMini.Primary ? Color.White : palette.TextPrimary;
        }
        _posterCaption.ForeColor = palette.TextSecondary;
        _posterCaption.BackColor = _poster.BackColor = palette.SurfaceRaised;
        _card.Back = palette.SurfaceRaised; _card.BackColor = palette.SurfaceRaised; _card.ForeColor = palette.TextPrimary; _card.BorderColor = palette.Border;
        foreach (var row in _surfaceRows) row.BackColor = palette.SurfaceRaised;
        foreach (var surface in _pageSurfaces) surface.BackColor = palette.Surface;
        void ThemeCardLabels(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is AntdUI.Label or AntdUI.Switch) child.BackColor = palette.SurfaceRaised;
                if (child is AntdUI.Label label) label.ForeColor = palette.TextPrimary;
                ThemeCardLabels(child);
            }
        }
        ThemeCardLabels(_card);
        if (_hotkeyCard != null) ThemeCardLabels(_hotkeyCard);
        _status.BackColor = palette.SurfaceRaised; _status.ForeColor = palette.TextSecondary;
        _current.BackColor = palette.SurfaceMuted; _current.ForeColor = palette.TextPrimary;
        UpdateSkinQualityColors(palette);
        foreach (var input in new[] { _previousKey, _nextKey, _pickerKey })
        {
            input.BackColor = palette.SurfaceMuted; input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
        }
        if (_hotkeyCard != null) { _hotkeyCard.Back = palette.SurfaceRaised; _hotkeyCard.BackColor = palette.SurfaceRaised; _hotkeyCard.BorderColor = palette.Border; }
        if (_objectCard != null) { _objectCard.Back = palette.SurfaceRaised; _objectCard.BackColor = palette.SurfaceRaised; _objectCard.BorderColor = palette.Border; ThemeCardLabels(_objectCard); }
        if (_otherSettingsCard != null) { _otherSettingsCard.Back = palette.SurfaceRaised; _otherSettingsCard.BackColor = palette.SurfaceRaised; _otherSettingsCard.BorderColor = palette.Border; ThemeCardLabels(_otherSettingsCard); }
        if (_borderCard != null) { _borderCard.Back = palette.SurfaceRaised; _borderCard.BackColor = palette.SurfaceRaised; _borderCard.BorderColor = palette.Border; ThemeCardLabels(_borderCard); }
        if (_clientBorderCard != null) { _clientBorderCard.Back = palette.SurfaceRaised; _clientBorderCard.BackColor = palette.SurfaceRaised; _clientBorderCard.BorderColor = palette.Border; ThemeCardLabels(_clientBorderCard); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposing)
        {
            _disposing = true;
            _clientRegaliaLifetime.Cancel();_clientRegaliaLifetime.Dispose();
            _borderLifetime.Cancel();_borderLifetime.Dispose();
            Interlocked.Increment(ref _revision);
            VisibleChanged -= PageVisibleChanged;
            UiLanguage.Changed -= LanguageChanged;
            CancelPageRequests();
            CancelHotkeyRequests();
            _cameraWatch.Dispose();
            _cameraOverlay.HidePanel(LOL_GameAssistant.Application.GameData.GameOverlayPanelKind.Camera);
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _poster.Image?.Dispose();
            _skinNotice?.Dispose();
            _skinPicker?.Dispose();
            _core.InvalidateSession();
        }
        base.Dispose(disposing);
    }
}
