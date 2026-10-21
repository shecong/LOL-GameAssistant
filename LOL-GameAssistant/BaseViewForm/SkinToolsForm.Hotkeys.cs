using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    public event Action? HotkeySettingsChanged;
    internal event Action<string>? HotkeySkinApplied;
    private SkinChangeOverlayPresenter? _skinNotice;
    private AntdUI.Panel? _hotkeyCard;
    private readonly AntdUI.Switch _hotkeyEnabled = new() { Name = "SkinHotkeysEnabled", Width = 52, Height = 30 };
    private readonly AntdUI.Input _previousKey = new() { Name = "SkinPreviousKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Input _nextKey = new() { Name = "SkinNextKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Input _pickerKey = new() { Name = "SkinPickerKey", Height = 40, Dock = DockStyle.Fill, Font = new Font(UiMetrics.FontFamily, 10) };
    private readonly AntdUI.Label _hotkeyStatus = new() { Dock = DockStyle.Fill, Text = "浮窗快捷键默认 Home，可选择英雄、眼位和小兵皮肤；需启用独立核心。\n上一款/下一款仅切换英雄皮肤，仅游戏前台生效。" };
    private CancellationTokenSource? _hotkeyRequests;
    private long _lastHotkeyAt;
    private bool _savingHotkeys;

    private Control CreateHotkeyCard()
    {
        var settings = _settings.Load();
        _hotkeyEnabled.Checked = settings.SkinHotkeysEnabled;
        _previousKey.Text = settings.SkinPreviousHotkey; _nextKey.Text = settings.SkinNextHotkey;
        _pickerKey.Text = settings.SkinPickerHotkey;
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
        Row(0, "浮窗显示 / 隐藏", _pickerKey); Row(1, "上一款/下一款开关", _hotkeyEnabled); Row(2, "上一款皮肤", _previousKey); Row(3, "下一款皮肤", _nextKey);
        var save = new AntdUI.Button { Text = "保存快捷键", Width = 146, Height = 38 }; save.Click += (_, _) => SaveHotkeys();
        Row(4, "", save); layout.Controls.Add(_hotkeyStatus, 0, 5); layout.SetColumnSpan(_hotkeyStatus, 2);
        foreach (var input in new[] { _previousKey, _nextKey, _pickerKey })
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
        (bool core, bool enabled, string previous, string next, string picker) original = (false, false, "", "", "Home");
        try
        {
            config = _settings.Load(); bool coreEnabled = config.SkinCoreEnabled;
            original = (coreEnabled, config.SkinHotkeysEnabled, config.SkinPreviousHotkey, config.SkinNextHotkey, config.SkinPickerHotkey);
            if (new[] { _previousKey, _nextKey, _pickerKey }.Any(input => !WindowHoldController.TryParseGameHotkey(input.Text, out _)))
                throw new InvalidOperationException("快捷键格式无效，请点击输入框重新录制。");
            config.SkinPreviousHotkey = _previousKey.Text; config.SkinNextHotkey = _nextKey.Text;
            config.SkinPickerHotkey = _pickerKey.Text;
            config.SkinHotkeysEnabled = _hotkeyEnabled.Checked;
            config.SkinCoreEnabled = true;
            var bindings = WindowHoldController.CreateGameHotkeyBindings(config);
            config.SkinCoreEnabled = coreEnabled;
            if (!bindings.ContainsValue(QuickShoutHotkeyAction.SkinPicker) ||
                config.SkinHotkeysEnabled && new[] { QuickShoutHotkeyAction.SkinPrevious, QuickShoutHotkeyAction.SkinNext }.Any(action => !bindings.ContainsValue(action)))
                throw new InvalidOperationException("快捷键存在冲突，请更换后保存。");
            _settings.Save(config); RenewHotkeyRequests(); HotkeySettingsChanged?.Invoke();
            _hotkeyStatus.Text = UiLanguage.T("快捷键已保存，仅游戏前台生效。") + $"\n{config.SkinPickerHotkey} 显示/隐藏英雄、眼位与小兵换肤窗（需启用独立核心）。";
            UpdateSkinPicker();
        }
        catch (Exception error)
        {
            if (config != null)
            {
                config.SkinCoreEnabled = original.core; config.SkinHotkeysEnabled = original.enabled;
                config.SkinPreviousHotkey = original.previous; config.SkinNextHotkey = original.next;
                config.SkinPickerHotkey = original.picker;
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
        if (!_disposing && _inProgress && _core.Enabled) _hotkeyRequests = new CancellationTokenSource();
    }
    public async Task ApplyHotkeyAsync(QuickShoutHotkeyAction action)
    {
        if (action == QuickShoutHotkeyAction.SkinPicker) { await ToggleSkinPickerAsync(); return; }
        if (_disposing || !_inProgress || _busy || _refreshing || !_core.Enabled || !_settings.Load().SkinHotkeysEnabled || _hotkeyRequests == null) return;
        if (action is not (QuickShoutHotkeyAction.SkinPrevious or QuickShoutHotkeyAction.SkinNext)) return;
        long now = Environment.TickCount64; if (now - _lastHotkeyAt < 750) return; _lastHotkeyAt = now;
        CancellationToken token = _hotkeyRequests.Token; _busy = true; UpdateButtons();
        try
        {
            var catalog = await _core.GetCatalogAsync(token); token.ThrowIfCancellationRequested();
            var target = SkinEntryCycle.Choose(catalog, action == QuickShoutHotkeyAction.SkinPrevious ? -1 : 1);
            var result = await _core.ApplyAsync(catalog.Session, target.EntryId, token);
            if (_disposing || token.IsCancellationRequested) return;
            _catalog = catalog; FilterEntries(); _entries.SelectedIndex = _filtered.FindIndex(e => e.EntryId == target.EntryId);
            _current.Text = $"{result.Model}  ·  #{result.Skin}  ·  {result.ActiveModel}  ·  {result.Gear}";
            _status.Text = UiLanguage.T(result.StateVerified ? "调用完成，实际状态已核验。画面、特效与技能仍需检查。" : "调用完成，但实际状态核验未通过。请刷新，勿连续重试。");
            if (!result.StateVerified) _catalog = null;
            else
            {
                RememberSkin(catalog, target);
                string name = GetSkinDisplayName(target);
                HotkeySkinApplied?.Invoke(name);
                (_skinNotice ??= new SkinChangeOverlayPresenter()).ShowSkin(name);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposing && !token.IsCancellationRequested) { ResetSession(); ShowError(error); } }
        finally { _busy = false; UpdateButtons(); }
    }
}
