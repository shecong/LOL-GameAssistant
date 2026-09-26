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
    private readonly Func<Task<GameShoutSendResult>> _testGameChatOpen;
    private readonly Action _saveSettings;
    private readonly ListBox _phrases = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _selectedPhrase = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true };
    private readonly TextBox _customPhrases = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _perCharacter = new() { Text = "逐字发送", AutoSize = true };
    private readonly CheckBox _sendToAll = new() { Text = "游戏内发给所有人（/all）", AutoSize = true };
    private readonly CheckBox _useClipboard = new() { Text = "游戏内使用粘贴输入", AutoSize = true };
    private readonly CheckBox _hotkeysEnabled = new() { Text = "启用游戏内快捷键", AutoSize = true };
    private readonly TextBox _builtInHotkey = new() { ReadOnly = true, Width = 48, Text = "F6" };
    private readonly TextBox _customHotkey = new() { ReadOnly = true, Width = 48, Text = "F7" };
    private readonly NumericUpDown _minimumInterval = new() { Minimum = 2, Maximum = 30, Value = 3, Width = 56 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12, 9, 0, 0) };
    private readonly AntdUI.Button _clientSend = new() { Text = "发送到客户端群聊", AutoSize = true };
    private readonly AntdUI.Button _gameSend = new() { Text = "一键发送到游戏", AutoSize = true };
    private readonly AntdUI.Button _testGameEnter = new() { Text = "测试游戏回车", AutoSize = true };
    private DateTime _lastSentAtUtc = DateTime.MinValue;

    public QuickShoutForm(LcuQuickShoutService clientChat,
        Func<string, bool, bool, bool, int, Task<GameShoutSendResult>> sendGameMessage,
        Func<Task<GameShoutSendResult>> testGameChatOpen, Action saveSettings)
    {
        _clientChat = clientChat;
        _sendGameMessage = sendGameMessage;
        _testGameChatOpen = testGameChatOpen;
        _saveSettings = saveSettings;
        Dock = DockStyle.Fill;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 49,
            Padding = new Padding(16, 12, 0, 0),
            Text = "一键喊话  ·  从默认或自定义词库随机选句，预览后主动发送到客户端或游戏内。"
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

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var randomButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var randomBuiltIn = new AntdUI.Button { Text = "默认词库随机", AutoSize = true };
        var randomCustom = new AntdUI.Button { Text = "自定义词库随机", AutoSize = true };
        randomBuiltIn.Click += (_, _) => SelectRandom(false);
        randomCustom.Click += (_, _) => SelectRandom(true);
        randomButtons.Controls.Add(randomBuiltIn);
        randomButtons.Controls.Add(randomCustom);
        left.Controls.Add(randomButtons, 0, 0);
        left.Controls.Add(_phrases, 0, 1);
        left.Controls.Add(new Label { Text = "待发送内容预览", Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
        left.Controls.Add(_selectedPhrase, 0, 3);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 10, 0, 0) };
        options.Controls.Add(_perCharacter);
        options.Controls.Add(_sendToAll);
        options.Controls.Add(_useClipboard);
        options.Controls.Add(new Label { Text = "最小间隔", AutoSize = true, Padding = new Padding(10, 3, 0, 0) });
        options.Controls.Add(_minimumInterval);
        options.Controls.Add(new Label { Text = "秒", AutoSize = true, Padding = new Padding(0, 3, 0, 0) });
        left.Controls.Add(options, 0, 4);
        var hotkeys = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        hotkeys.Controls.Add(_hotkeysEnabled);
        hotkeys.Controls.Add(new Label { Text = "默认词库", AutoSize = true, Padding = new Padding(9, 3, 0, 0) });
        hotkeys.Controls.Add(_builtInHotkey);
        hotkeys.Controls.Add(new Label { Text = "自定义词库", AutoSize = true, Padding = new Padding(9, 3, 0, 0) });
        hotkeys.Controls.Add(_customHotkey);
        left.Controls.Add(hotkeys, 0, 5);
        var sendButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        sendButtons.Controls.Add(_clientSend);
        sendButtons.Controls.Add(_gameSend);
        sendButtons.Controls.Add(_testGameEnter);
        left.Controls.Add(sendButtons, 0, 6);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        right.Controls.Add(new Label { Text = "自定义词库（一行一句）", Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) }, 0, 0);
        right.Controls.Add(_customPhrases, 0, 1);
        var save = new AntdUI.Button { Text = "保存词库与发送选项", AutoSize = true };
        save.Click += (_, _) => SaveOptions();
        right.Controls.Add(save, 0, 2);
        right.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "逐字发送会把每个字作为独立消息；“所有人”仅对游戏生效。快捷键只在游戏前台触发。若文字未出现，勾选“游戏内使用粘贴输入”并保存后重试。"
        }, 0, 3);
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        Controls.Add(columns);
        Controls.Add(_status);
        Controls.Add(header);

        _phrases.SelectedIndexChanged += (_, _) =>
            _selectedPhrase.Text = (_phrases.SelectedItem as PhraseItem)?.Text ?? "";
        _customPhrases.TextChanged += (_, _) => RefreshPhrases();
        _clientSend.Click += async (_, _) => await SendClientAsync();
        _gameSend.Click += async (_, _) => await SendGameAsync();
        _testGameEnter.Click += async (_, _) => await TestGameEnterAsync();
        _builtInHotkey.KeyDown += (_, e) => CaptureHotkey(_builtInHotkey, e);
        _customHotkey.KeyDown += (_, e) => CaptureHotkey(_customHotkey, e);
        _status.Text = "从词库选一句，或用随机按钮选句；客户端发送仅面向当前队伍群聊。";
        RefreshPhrases();
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
        _minimumInterval.Value = Math.Clamp(settings.QuickMessageSendIntervalSeconds, 2, 30);
        RefreshPhrases();
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
        settings.QuickMessageSendIntervalSeconds = (int)_minimumInterval.Value;
    }

    private void SaveOptions()
    {
        if (_customPhrases.Lines.Any(line => line.Trim().Length > 500))
        {
            _status.Text = "自定义词条每句最多 500 字，请缩短后保存。";
            return;
        }
        if (_hotkeysEnabled.Checked && _builtInHotkey.Text == _customHotkey.Text)
        {
            _status.Text = "默认与自定义词库不能使用同一个快捷键。";
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

    private void CaptureHotkey(TextBox target, KeyEventArgs e)
    {
        if (e.KeyCode is >= Keys.F2 and <= Keys.F12) target.Text = e.KeyCode.ToString();
        else _status.Text = "喊话快捷键请选择 F2–F12。";
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    public async Task SendRandomToGameAsync(bool custom)
    {
        SelectRandom(custom);
        if ((_phrases.SelectedItem as PhraseItem)?.IsCustom != custom) return;
        await SendGameAsync();
    }

    private void RefreshPhrases()
    {
        string? selected = (_phrases.SelectedItem as PhraseItem)?.Text;
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
            _phrases.SelectedIndex = Math.Max(0, Array.FindIndex(items, item => item.Text == selected));
        }
        finally { _phrases.EndUpdate(); }
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
        _phrases.SelectedIndex = indices[Random.Shared.Next(indices.Length)];
    }

    private bool CanSend()
    {
        if (_phrases.SelectedItem is not PhraseItem) { _status.Text = "请先选择一句。"; return false; }
        int seconds = (int)_minimumInterval.Value;
        if ((DateTime.UtcNow - _lastSentAtUtc).TotalSeconds >= seconds) return true;
        _status.Text = $"发送间隔至少 {seconds} 秒，请稍后再试。";
        return false;
    }

    private async Task SendClientAsync()
    {
        if (!CanSend()) return;
        string phrase = ((PhraseItem)_phrases.SelectedItem!).Text;
        _clientSend.Enabled = false;
        _gameSend.Enabled = false;
        _status.Text = "正在发送到客户端群聊…";
        try
        {
            string result = await _clientChat.SendAsync(phrase, _perCharacter.Checked);
            _status.Text = result;
            if (result.StartsWith("已发送", StringComparison.Ordinal)) _lastSentAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) { _status.Text = $"发送失败：{ex.Message}"; }
        finally { _clientSend.Enabled = true; _gameSend.Enabled = true; }
    }

    private async Task SendGameAsync()
    {
        if (!CanSend()) return;
        string phrase = ((PhraseItem)_phrases.SelectedItem!).Text;
        _clientSend.Enabled = false;
        _gameSend.Enabled = false;
        try
        {
            GameShoutSendResult result = await _sendGameMessage(phrase, _sendToAll.Checked,
                _useClipboard.Checked,
                _perCharacter.Checked, (int)_minimumInterval.Value);
            _status.Text = result.Message;
            if (result.Succeeded) _lastSentAtUtc = DateTime.UtcNow;
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
        _status.ForeColor = palette.TextSecondary;
    }
}