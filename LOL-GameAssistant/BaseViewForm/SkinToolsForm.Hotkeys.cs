using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    public event Action? HotkeySettingsChanged;
    private AntdUI.Panel? _hotkeyCard;
    private readonly AntdUI.Switch _hotkeyEnabled = new() { Name = "SkinHotkeysEnabled", Width = 52, Height = 30 };
    private readonly AntdUI.Input _previousKey = new() { Name = "SkinPreviousKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Input _nextKey = new() { Name = "SkinNextKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Input _applyKey = new() { Name = "SkinApplyKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Label _hotkeyStatus = new() { Dock = DockStyle.Fill, Text = "仅游戏前台生效；点击输入框后按组合键录制。" };
    private CancellationTokenSource? _hotkeyRequests;
    private long _lastHotkeyAt;
    private bool _savingHotkeys;

    private Control CreateHotkeyCard()
    {
        var settings = _settings.Load();
        _hotkeyEnabled.Checked = settings.SkinHotkeysEnabled;
        _previousKey.Text = settings.SkinPreviousHotkey; _nextKey.Text = settings.SkinNextHotkey; _applyKey.Text = settings.SkinApplyHotkey;
        _hotkeyCard = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 14, BorderWidth = 1, Padding = new Padding(24), Margin = new Padding(0, 14, 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        void Row(int index, string label, Control control)
        {
            layout.Controls.Add(new AntdUI.Label { Dock = DockStyle.Fill, Text = label, TextAlign = ContentAlignment.MiddleLeft }, 0, index);
            layout.Controls.Add(control, 1, index);
        }
        Row(0, "游戏内快捷键", _hotkeyEnabled); Row(1, "上一款皮肤", _previousKey); Row(2, "下一款皮肤", _nextKey); Row(3, "应用所选皮肤", _applyKey);
        var save = new AntdUI.Button { Text = "保存快捷键", Width = 146, Height = 38 }; save.Click += (_, _) => SaveHotkeys();
        Row(4, "", save); layout.Controls.Add(_hotkeyStatus, 0, 5); layout.SetColumnSpan(_hotkeyStatus, 2);
        foreach (var input in new[] { _previousKey, _nextKey, _applyKey })
        {
            input.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true; e.Handled = true;
                if (WindowHoldController.TryParseGameHotkey(WindowHoldController.FormatGameHotkey(e.KeyData), out var key)) input.Text = WindowHoldController.FormatGameHotkey(key);
            };
            input.Enter += (_, _) => { if (Program.GameMain != null) Program.GameMain.SetWindowHotkeyCapturePaused(true); };
            input.Leave += (_, _) => { if (Program.GameMain != null) Program.GameMain.SetWindowHotkeyCapturePaused(false); };
        }
        _hotkeyEnabled.CheckedChanged += (_, _) => SaveHotkeys();
        _surfaceRows.Add(layout); _hotkeyCard.Controls.Add(layout); RenewHotkeyRequests(); return _hotkeyCard;
    }
    private void SaveHotkeys()
    {
        if (_disposing || _savingHotkeys) return;
        _savingHotkeys = true;
        LOL_GameAssistant.Domain.Settings.AssistantSettings? config = null;
        (bool core, bool enabled, string previous, string next, string apply) original = (false, false, "", "", "");
        try
        {
            config = _settings.Load(); bool coreEnabled = config.SkinCoreEnabled;
            original = (coreEnabled, config.SkinHotkeysEnabled, config.SkinPreviousHotkey, config.SkinNextHotkey, config.SkinApplyHotkey);
            if (new[] { _previousKey, _nextKey, _applyKey }.Any(input => !WindowHoldController.TryParseGameHotkey(input.Text, out _)))
                throw new InvalidOperationException("快捷键格式无效，请点击输入框重新录制。");
            config.SkinPreviousHotkey = _previousKey.Text; config.SkinNextHotkey = _nextKey.Text; config.SkinApplyHotkey = _applyKey.Text;
            config.SkinHotkeysEnabled = _hotkeyEnabled.Checked;
            config.SkinCoreEnabled = true;
            var bindings = WindowHoldController.CreateGameHotkeyBindings(config);
            config.SkinCoreEnabled = coreEnabled;
            if (config.SkinHotkeysEnabled && new[] { QuickShoutHotkeyAction.SkinPrevious, QuickShoutHotkeyAction.SkinNext, QuickShoutHotkeyAction.SkinApply }.Any(action => !bindings.ContainsValue(action)))
                throw new InvalidOperationException("快捷键存在冲突，请更换后保存。");
            _settings.Save(config); RenewHotkeyRequests(); HotkeySettingsChanged?.Invoke();
            _hotkeyStatus.Text = UiLanguage.T("快捷键已保存，仅游戏前台生效。");
        }
        catch (Exception error)
        {
            if (config != null)
            {
                config.SkinCoreEnabled = original.core; config.SkinHotkeysEnabled = original.enabled;
                config.SkinPreviousHotkey = original.previous; config.SkinNextHotkey = original.next; config.SkinApplyHotkey = original.apply;
                _hotkeyEnabled.Checked = original.enabled;
            }
            _hotkeyStatus.Text = error.Message;
        }
        finally { _savingHotkeys = false; }
    }
    private void CancelHotkeyRequests()
    {
        var source = _hotkeyRequests; _hotkeyRequests = null;
        if (source == null) return;
        try { source.Cancel(); } finally { source.Dispose(); }
    }
    private void RenewHotkeyRequests()
    {
        CancelHotkeyRequests();
        if (!_disposing) _hotkeyRequests = new CancellationTokenSource();
    }
    public async Task ApplyHotkeyAsync(QuickShoutHotkeyAction action)
    {
        if (_disposing || _busy || _refreshing || !_core.Enabled || !_settings.Load().SkinHotkeysEnabled || _hotkeyRequests == null) return;
        if (action is not (QuickShoutHotkeyAction.SkinPrevious or QuickShoutHotkeyAction.SkinNext or QuickShoutHotkeyAction.SkinApply)) return;
        long now = Environment.TickCount64; if (now - _lastHotkeyAt < 750) return; _lastHotkeyAt = now;
        var previousCatalog = _catalog;
        string? selectedId = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex].EntryId : null;
        CancellationToken token = _hotkeyRequests.Token; _busy = true; UpdateButtons();
        try
        {
            var catalog = await _core.GetCatalogAsync(token); token.ThrowIfCancellationRequested();
            SkinEntry target;
            if (action == QuickShoutHotkeyAction.SkinApply)
            {
                if (previousCatalog?.Session != catalog.Session || selectedId == null) throw new SkinCoreException("stale_session");
                target = catalog.Entries.SingleOrDefault(e => e.EntryId == selectedId) ?? throw new SkinCoreException("invalid_entry");
            }
            else target = SkinEntryCycle.Choose(catalog, action == QuickShoutHotkeyAction.SkinPrevious ? -1 : 1);
            var result = await _core.ApplyAsync(catalog.Session, target.EntryId, token);
            if (_disposing || token.IsCancellationRequested) return;
            _catalog = catalog; FilterEntries(); _entries.SelectedIndex = _filtered.FindIndex(e => e.EntryId == target.EntryId);
            _current.Text = $"{result.Model}  ·  #{result.Skin}  ·  {result.ActiveModel}  ·  {result.Gear}";
            _status.Text = UiLanguage.T(result.StateVerified ? "调用完成，实际状态已核验。画面、特效与技能仍需检查。" : "调用完成，但实际状态核验未通过。请刷新，勿连续重试。");
            if (!result.StateVerified) _catalog = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposing && !token.IsCancellationRequested) { ResetSession(); ShowError(error); } }
        finally { _busy = false; UpdateButtons(); }
    }
}
