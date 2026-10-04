using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.LeagueClient;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>设置页中的随机词库、预览和主动发送操作。</summary>
public sealed class QuickShoutForm : UserControl, IThemeAware
{
    private sealed record PhraseItem(string Text, bool IsCustom)
    {
        public override string ToString() => $"{(IsCustom ? "自定义" : "默认")} · {Text}";
    }

    private static readonly string[] BuiltInPhrases =
    [
        "我去支援，请注意地图。",
        "这波先稳住，等关键技能。",
        "准备打龙，麻烦先做视野。",
        "可以一起推线后集合。",
        "Nice play，继续保持！"
    ];

    private readonly LcuQuickShoutService _clientChat;
    private readonly Func<string, bool, bool, bool, int, Task<GameShoutSendResult>> _sendGameMessage;
    private readonly Func<IReadOnlyList<string>, bool, bool, bool, int, Task<GameShoutSendResult>> _sendGameBatch;
    private readonly Func<Task<GameShoutSendResult>> _testGameChatOpen;
    private readonly Action _saveSettings;
    private readonly ListBox _phrases = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly AntdUI.Input _selectedPhrase = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly AntdUI.Input _customPhrases = new() { Dock = DockStyle.Fill, Multiline = true };
    private readonly AntdUI.Checkbox _perCharacter = new() { Text = "逐字发送", AutoSize = true };
    private readonly AntdUI.Checkbox _sendToAll = new() { Text = "游戏内发给所有人（/all）", AutoSize = true };
    private readonly AntdUI.Checkbox _useClipboard = new() { Text = "游戏内使用粘贴输入", AutoSize = true };
    private readonly AntdUI.Checkbox _hotkeysEnabled = new() { Text = "启用词库快捷键", AutoSize = true };
    private readonly AntdUI.Input _builtInHotkey = new() { ReadOnly = true, Width = 180, Height = 36, Anchor = AnchorStyles.Left, Text = "F6" };
    private readonly AntdUI.Input _customHotkey = new() { ReadOnly = true, Width = 180, Height = 36, Anchor = AnchorStyles.Left, Text = "F7" };
    private readonly AntdUI.Input _batchHotkey = new() { ReadOnly = true, Width = 180, Height = 36, Anchor = AnchorStyles.Left, Text = "F8" };
    private readonly AntdUI.Checkbox _kdaHotkeyEnabled = new() { Text = "手动发送对局 KDA", AutoSize = true };
    private readonly AntdUI.Input _kdaHotkey = new() { ReadOnly = true, Width = 180, Height = 36, Anchor = AnchorStyles.Left, Text = "F9" };
    private readonly AntdUI.Button _multiSelect = new() { Text = "多选：关", AutoSize = true };
    private readonly AntdUI.Label _previewLabel = new() { Text = "待发送内容预览", Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
    private readonly AntdUI.InputNumber _minimumInterval = new() { Minimum = 2, Maximum = 30, Value = 3, Width = 96, Height = 36 };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(12, 5, 0, 0) };
    private readonly AntdUI.Button _clientSend = new() { Text = "发送到客户端群聊", AutoSize = true };
    private readonly AntdUI.Button _gameSend = new() { Text = "一键发送到游戏", AutoSize = true };
    private readonly AntdUI.Button _testGameEnter = new() { Text = "测试游戏回车", AutoSize = true };
    private DateTime _lastSentAtUtc = DateTime.MinValue;

    public QuickShoutForm(LcuQuickShoutService clientChat,
        Func<string, bool, bool, bool, int, Task<GameShoutSendResult>> sendGameMessage,
        Func<IReadOnlyList<string>, bool, bool, bool, int, Task<GameShoutSendResult>> sendGameBatch,
        Func<Task<GameShoutSendResult>> testGameChatOpen, Action saveSettings)
    {
        _clientChat = clientChat;
        _sendGameMessage = sendGameMessage;
        _sendGameBatch = sendGameBatch;
        _testGameChatOpen = testGameChatOpen;
        _saveSettings = saveSettings;
        Dock = DockStyle.Fill;

        var header = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 49,
            Padding = new Padding(16, 12, 0, 0),
            Text = "一键喊话  ·  开启多选后点击多句，可按词库顺序发送到客户端或游戏内。"
        };
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 4, 12, 4)
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var randomButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        var randomBuiltIn = new AntdUI.Button { Text = "默认词库随机", AutoSize = true };
        var randomCustom = new AntdUI.Button { Text = "自定义词库随机", AutoSize = true };
        randomBuiltIn.Click += (_, _) => SelectRandom(false);
        randomCustom.Click += (_, _) => SelectRandom(true);
        randomButtons.Controls.Add(randomBuiltIn);
        randomButtons.Controls.Add(randomCustom);
        randomButtons.Controls.Add(_multiSelect);
        left.Controls.Add(randomButtons, 0, 0);
        left.Controls.Add(_phrases, 0, 1);
        left.Controls.Add(_previewLabel, 0, 2);
        left.Controls.Add(_selectedPhrase, 0, 3);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(10, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        right.Controls.Add(new AntdUI.Label { Text = "自定义词库（一行一句）", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) }, 0, 0);
        right.Controls.Add(_customPhrases, 0, 1);
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        Controls.Add(columns);
        Controls.Add(CreateOptionsArea());
        Controls.Add(CreateActionBar());
        Controls.Add(_status);
        Controls.Add(header);

        _phrases.SelectedIndexChanged += (_, _) => UpdateSelectionPreview();
        _multiSelect.Click += (_, _) => SetMultiSelect(_phrases.SelectionMode == SelectionMode.One);
        _customPhrases.TextChanged += (_, _) => RefreshPhrases();
        _clientSend.Click += async (_, _) => await SendClientAsync();
        _gameSend.Click += async (_, _) => await SendGameAsync();
        _testGameEnter.Click += async (_, _) => await TestGameEnterAsync();
        _builtInHotkey.KeyDown += (_, e) => CaptureHotkey(_builtInHotkey, e);
        _customHotkey.KeyDown += (_, e) => CaptureHotkey(_customHotkey, e);
        _batchHotkey.KeyDown += (_, e) => CaptureHotkey(_batchHotkey, e);
        _kdaHotkey.KeyDown += (_, e) => CaptureHotkey(_kdaHotkey, e);
        _status.Text = "从词库选句；多选发送会逐条发送到客户端群聊或游戏内。";
        RefreshPhrases();
        ApplyTheme(UiTheme.Palette);
    }

    public void LoadSettings(AssistantSettings settings)
    {
        _customPhrases.Text = settings.QuickMessageCustomPhrases ?? "";
        _perCharacter.Checked = settings.QuickShoutPerCharacter;
        _sendToAll.Checked = settings.QuickShoutSendToAll;
        _useClipboard.Checked = settings.QuickShoutUseClipboard;
        _hotkeysEnabled.Checked = settings.QuickShoutHotkeysEnabled;
        _builtInHotkey.Text = settings.QuickShoutBuiltInHotkey;
        _customHotkey.Text = settings.QuickShoutCustomHotkey;
        _batchHotkey.Text = settings.QuickShoutBatchHotkey;
        _kdaHotkeyEnabled.Checked = settings.GameKdaHotkeyEnabled;
        _kdaHotkey.Text = settings.GameKdaHotkey;
        _minimumInterval.Value = Math.Clamp(settings.QuickMessageSendIntervalSeconds, 2, 30);
        SetMultiSelect(settings.QuickShoutMultiSelectEnabled);
        RefreshPhrases();
        RestoreSelection(settings.QuickShoutSelectedPhrases);
    }

    public void WriteSettings(AssistantSettings settings)
    {
        settings.QuickMessageCustomPhrases = _customPhrases.Text.Trim();
        settings.QuickShoutPerCharacter = _perCharacter.Checked;
        settings.QuickShoutSendToAll = _sendToAll.Checked;
        settings.QuickShoutUseClipboard = _useClipboard.Checked;
        settings.QuickShoutHotkeysEnabled = _hotkeysEnabled.Checked;
        settings.QuickShoutBuiltInHotkey = _builtInHotkey.Text;
        settings.QuickShoutCustomHotkey = _customHotkey.Text;
        settings.QuickShoutBatchHotkey = _batchHotkey.Text;
        settings.GameKdaHotkeyEnabled = _kdaHotkeyEnabled.Checked;
        settings.GameKdaHotkey = _kdaHotkey.Text;
        settings.QuickShoutMultiSelectEnabled = _phrases.SelectionMode == SelectionMode.MultiSimple;
        settings.QuickShoutSelectedPhrases = SelectedItems().Select(PhraseKey).ToList();
        settings.QuickMessageSendIntervalSeconds = (int)_minimumInterval.Value;
    }

    private void SaveOptions()
    {
        if (SelectedItems().Length > 10)
        {
            _status.Text = "一次最多发送 10 句，请减少选中短句后保存。";
            return;
        }
        if (_customPhrases.Lines.Any(line => line.Trim().Length > 500))
        {
            _status.Text = "自定义词条每句最多 500 字，请缩短后保存。";
            return;
        }
        if (_hotkeysEnabled.Checked && new[] { _builtInHotkey.Text, _customHotkey.Text, _batchHotkey.Text }
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
        {
            _status.Text = "三个喊话快捷键不能重复。";
            return;
        }
        try
        {
            _saveSettings();
            RefreshPhrases();
            _status.Text = "词库与发送选项已保存。";
        }
        catch (Exception ex) { _status.Text = $"保存失败：{ex.Message}"; }
    }

    private void CaptureHotkey(AntdUI.Input target, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (!WindowHoldController.TryParseGameHotkey(WindowHoldController.FormatGameHotkey(e.KeyData), out Keys key))
        {
            _status.Text = "请按一个按键，或按 Ctrl / Alt / Shift 与按键的组合。";
            return;
        }
        target.Text = WindowHoldController.FormatGameHotkey(key);
        _status.Text = $"快捷键已录入：{target.Text}，点击“保存设置”后生效。";
    }

    private Control CreateOptionsArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 228, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(12, 8, 12, 4)
        };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        var sending = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8, 0, 16, 0) };
        sending.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (int i = 0; i < 3; i++) sending.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        sending.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        sending.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sending.Controls.Add(OptionLabel("发送选项"), 0, 0);
        sending.Controls.Add(_perCharacter, 0, 1);
        sending.Controls.Add(_sendToAll, 0, 2);
        sending.Controls.Add(_useClipboard, 0, 3);
        var cooldown = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        cooldown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        cooldown.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        cooldown.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cooldown.Controls.Add(OptionLabel("触发冷却"), 0, 0);
        _minimumInterval.Anchor = AnchorStyles.Left;
        cooldown.Controls.Add(_minimumInterval, 1, 0);
        cooldown.Controls.Add(OptionLabel("秒 · 两组喊话之间的间隔"), 2, 0);
        sending.Controls.Add(cooldown, 0, 4);
        sending.Controls.Add(OptionLabel("多选最多 10 句；逐字批量最多 40 字。"), 0, 5);

        var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12, 0, 8, 0) };
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (int i = 0; i < 4; i++) shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shortcuts.Controls.Add(_hotkeysEnabled, 0, 0);
        shortcuts.SetColumnSpan(_hotkeysEnabled, 2);
        shortcuts.Controls.Add(OptionLabel("默认词库随机"), 0, 1);
        shortcuts.Controls.Add(_builtInHotkey, 1, 1);
        shortcuts.Controls.Add(OptionLabel("自定义词库随机"), 0, 2);
        shortcuts.Controls.Add(_customHotkey, 1, 2);
        shortcuts.Controls.Add(OptionLabel("多选批量发送"), 0, 3);
        shortcuts.Controls.Add(_batchHotkey, 1, 3);
        shortcuts.Controls.Add(_kdaHotkeyEnabled, 0, 4);
        shortcuts.Controls.Add(_kdaHotkey, 1, 4);
        var hint = OptionLabel("点击框后按键录入，支持 Ctrl / Alt / Shift 组合；仅游戏前台触发。KDA 可重复发送。");
        shortcuts.Controls.Add(hint, 0, 5);
        shortcuts.SetColumnSpan(hint, 2);
        area.Controls.Add(sending, 0, 0);
        area.Controls.Add(shortcuts, 1, 0);
        return area;
    }

    private Control CreateActionBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 4, 12, 4) };
        var save = new AntdUI.Button { Text = "保存设置", Dock = DockStyle.Right, Width = 130 };
        save.Click += (_, _) => SaveOptions();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(_clientSend);
        actions.Controls.Add(_gameSend);
        actions.Controls.Add(_testGameEnter);
        bar.Controls.Add(actions);
        bar.Controls.Add(save);
        return bar;
    }

    private static AntdUI.Label OptionLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 0, 3, 0)
    };

    public async Task SendRandomToGameAsync(bool custom)
    {
        PhraseItem[] candidates = _phrases.Items.Cast<PhraseItem>()
            .Where(item => item.IsCustom == custom).ToArray();
        if (candidates.Length == 0)
        {
            _status.Text = custom ? "自定义词库为空，请先填写并保存。" : "默认词库为空。";
            return;
        }
        await SendGameAsync([candidates[Random.Shared.Next(candidates.Length)].Text]);
    }

    public Task SendSelectedBatchToGameAsync() => SendGameAsync(SelectedItems().Select(item => item.Text).ToArray());

    private void RefreshPhrases()
    {
        string[] selected = SelectedItems().Select(PhraseKey).ToArray();
        PhraseItem[] items = BuiltInPhrases.Select(text => new PhraseItem(text, false))
            .Concat(_customPhrases.Lines.Select(text => text.Trim())
                .Where(text => text.Length is > 0 and <= 500)
                .Distinct(StringComparer.CurrentCulture)
                .Select(text => new PhraseItem(text, true)))
            .ToArray();
        _phrases.BeginUpdate();
        try
        {
            _phrases.Items.Clear();
            _phrases.Items.AddRange(items);
            RestoreSelection(selected);
        }
        finally { _phrases.EndUpdate(); }
    }

    private static string PhraseKey(PhraseItem item) => $"{(item.IsCustom ? "C" : "D")}|{item.Text}";

    private PhraseItem[] SelectedItems() => _phrases.SelectedItems.Cast<PhraseItem>().ToArray();

    private void RestoreSelection(IReadOnlyCollection<string>? keys)
    {
        var wanted = new HashSet<string>(keys ?? Array.Empty<string>(), StringComparer.Ordinal);
        _phrases.ClearSelected();
        for (int index = 0; index < _phrases.Items.Count; index++)
        {
            if (_phrases.Items[index] is PhraseItem item && wanted.Contains(PhraseKey(item)))
            {
                _phrases.SetSelected(index, true);
                if (_phrases.SelectionMode == SelectionMode.One) break;
            }
        }
        if (_phrases.SelectedItems.Count == 0 && _phrases.Items.Count > 0)
            _phrases.SetSelected(0, true);
        UpdateSelectionPreview();
    }

    private void SetMultiSelect(bool enabled)
    {
        string[] selected = SelectedItems().Select(PhraseKey).ToArray();
        _phrases.SelectionMode = enabled ? SelectionMode.MultiSimple : SelectionMode.One;
        _multiSelect.Text = enabled ? "多选：开" : "多选：关";
        RestoreSelection(selected);
    }

    private void UpdateSelectionPreview()
    {
        PhraseItem[] selected = SelectedItems();
        _previewLabel.Text = $"待发送内容预览（{selected.Length}/10 句）";
        _selectedPhrase.Text = string.Join(Environment.NewLine, selected.Select(item => item.Text));
        _clientSend.Text = selected.Length > 1 ? $"发送客户端（{selected.Length}）" : "发送到客户端群聊";
        _gameSend.Text = selected.Length > 1 ? $"发送游戏（{selected.Length}）" : "一键发送到游戏";
    }

    private void SelectRandom(bool custom)
    {
        int[] indices = _phrases.Items.Cast<PhraseItem>().Select((item, index) => (item, index))
            .Where(pair => pair.item.IsCustom == custom).Select(pair => pair.index).ToArray();
        if (indices.Length == 0)
        {
            _status.Text = custom ? "自定义词库为空，请先填写并保存。" : "默认词库为空。";
            return;
        }
        _phrases.ClearSelected();
        _phrases.SetSelected(indices[Random.Shared.Next(indices.Length)], true);
    }

    private bool CanSend(IReadOnlyList<string> phrases)
    {
        if (!_clientSend.Enabled || !_gameSend.Enabled)
        {
            _status.Text = "上一组消息仍在发送，请稍候。";
            return false;
        }
        if (phrases.Count == 0) { _status.Text = "请先选择短句。"; return false; }
        if (phrases.Count > 10) { _status.Text = "一次最多发送 10 句，请减少选中短句。"; return false; }
        int seconds = (int)_minimumInterval.Value;
        if ((DateTime.UtcNow - _lastSentAtUtc).TotalSeconds >= seconds) return true;
        _status.Text = $"两次喊话至少间隔 {seconds} 秒，请稍后再试。";
        return false;
    }

    private async Task SendClientAsync()
    {
        string[] phrases = SelectedItems().Select(item => item.Text).ToArray();
        if (!CanSend(phrases)) return;
        _clientSend.Enabled = false;
        _gameSend.Enabled = false;
        _status.Text = "正在发送到客户端群聊…";
        try
        {
            string result = phrases.Length == 1
                ? await _clientChat.SendAsync(phrases[0], _perCharacter.Checked)
                : await _clientChat.SendBatchAsync(phrases, _perCharacter.Checked);
            _status.Text = result;
            if (result.StartsWith("已发送", StringComparison.Ordinal)) _lastSentAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) { _status.Text = $"发送失败：{ex.Message}"; }
        finally { _clientSend.Enabled = true; _gameSend.Enabled = true; }
    }

    private Task SendGameAsync() => SendGameAsync(SelectedItems().Select(item => item.Text).ToArray());

    private async Task SendGameAsync(IReadOnlyList<string> phrases)
    {
        if (!CanSend(phrases)) return;
        _clientSend.Enabled = false;
        _gameSend.Enabled = false;
        _status.Text = phrases.Count > 1
            ? $"正在按顺序发送 {phrases.Count} 句到游戏…"
            : "正在发送到游戏…";
        try
        {
            GameShoutSendResult result = phrases.Count == 1
                ? await _sendGameMessage(phrases[0], _sendToAll.Checked,
                    _useClipboard.Checked, _perCharacter.Checked, (int)_minimumInterval.Value)
                : await _sendGameBatch(phrases, _sendToAll.Checked,
                    _useClipboard.Checked, _perCharacter.Checked, (int)_minimumInterval.Value);
            _status.Text = result.Message;
            if (result.Succeeded || result.SentCount > 0) _lastSentAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) { _status.Text = $"游戏内发送失败：{ex.Message}"; }
        finally { _clientSend.Enabled = true; _gameSend.Enabled = true; }
    }

    private async Task TestGameEnterAsync()
    {
        _testGameEnter.Enabled = false;
        try { _status.Text = (await _testGameChatOpen()).Message; }
        catch (Exception ex) { _status.Text = $"测试游戏回车失败：{ex.Message}"; }
        finally { _testGameEnter.Enabled = true; }
    }

    private static void ApplyLabelColors(Control root, Color color)
    {
        foreach (Control child in root.Controls)
        {
            if (child is AntdUI.Label label) label.ForeColor = color;
            ApplyLabelColors(child, color);
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _phrases.BackColor = palette.SurfaceRaised;
        _phrases.ForeColor = palette.TextPrimary;
        _selectedPhrase.BackColor = palette.SurfaceRaised;
        _selectedPhrase.ForeColor = palette.TextPrimary;
        _customPhrases.BackColor = palette.SurfaceRaised;
        _customPhrases.ForeColor = palette.TextPrimary;
        _selectedPhrase.BorderColor = palette.Border;
        _customPhrases.BorderColor = palette.Border;
        foreach (var checkbox in new[] { _perCharacter, _sendToAll, _useClipboard, _hotkeysEnabled, _kdaHotkeyEnabled })
            checkbox.ForeColor = palette.TextPrimary;
        ApplyLabelColors(this, palette.TextPrimary);
        foreach (var input in new[] { _builtInHotkey, _customHotkey, _batchHotkey, _kdaHotkey })
        {
            input.BackColor = palette.SurfaceRaised;
            input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
            input.BorderHover = palette.Accent;
            input.BorderActive = palette.Accent;
        }
        _minimumInterval.BackColor = palette.SurfaceRaised;
        _minimumInterval.ForeColor = palette.TextPrimary;
        _minimumInterval.BorderColor = palette.Border;
        _status.ForeColor = palette.TextSecondary;
    }
}
