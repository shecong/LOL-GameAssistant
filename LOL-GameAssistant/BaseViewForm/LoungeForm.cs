using LOL.Lounge;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Public lounge view. The application owns the connection even when this page is hidden.</summary>
public sealed partial class LoungeForm : UserControl, IThemeAware
{
    private readonly LoungeClient _client;
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox _server = new() { Dock = DockStyle.Fill, PlaceholderText = "http://服务器IP:5000 或 https://域名" };
    private readonly CheckBox _share = new() { Text = "公开游戏状态与战绩", AutoSize = true, Checked = true };
    private readonly CheckBox _victories = new() { Text = "显示战绩播报", AutoSize = true, Checked = true };
    private readonly AntdUI.Button _connect = new() { Text = "保存并连接", Dock = DockStyle.Fill, Type = AntdUI.TTypeMini.Primary };
    private readonly RichTextBox _history = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None };
    private readonly ListBox _members = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Label _memberCount = new() { Dock = DockStyle.Top, Height = 36, Text = "在线成员 · 0", TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox _input = new() { Dock = DockStyle.Fill, Multiline = true, MaxLength = LoungeRules.MaxTextLength,
        ScrollBars = ScrollBars.Vertical, PlaceholderText = "登录 LOL 后发言，Enter 发送，Shift+Enter 换行（最多 500 字）" };
    private readonly AntdUI.Button _send = new() { Text = "发送", Dock = DockStyle.Fill, Type = AntdUI.TTypeMini.Primary };
    private readonly Label _feedback = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private readonly ContextMenuStrip _context = new();
    private readonly ContextMenuStrip _memberContext = new();
    private LoungeMember[] _visibleMembers = [];
    private readonly List<(int Start, string Sender, LoungeMessage Message)> _senders = [];
    private string _historyKey = "\0";
    private string _memberKey = "";
    private string _pendingId = Guid.NewGuid().ToString("N");
    private string _pendingText = "";
    private bool _sending;

    public LoungeForm(LoungeClient client)
    {
        Name = "LoungeForm";
        _client = client;
        Dock = DockStyle.Fill;
        Padding = new Padding(20);
        Font = new Font(UiMetrics.FontFamily, 10F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.Controls.Add(new Label { Text = "lol休息室", Font = new Font(UiMetrics.FontFamily, 18F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new Label { Text = "所有助手用户共享一个频道 · 昵称与大区公开展示 · 未登录 LOL 时可查看消息", Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 1);
        var connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        connection.Controls.Add(new Label { Text = "服务器地址", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        connection.Controls.Add(_server, 1, 0);
        connection.Controls.Add(_connect, 2, 0);
        layout.Controls.Add(connection, 0, 2);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var clearBlocks = new LinkLabel { Text = "清空屏蔽名单", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
        options.Controls.AddRange([_share, _victories, clearBlocks]);
        layout.Controls.Add(options, 0, 3);
        layout.Controls.Add(BuildPreferences(), 0, 4);
        var conversation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 8) };
        conversation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        conversation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        conversation.Controls.Add(_history, 0, 0);
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0) };
        sidebar.Controls.Add(_members);
        sidebar.Controls.Add(_memberCount);
        conversation.Controls.Add(sidebar, 1, 0);
        layout.Controls.Add(conversation, 0, 5);
        layout.Controls.Add(BuildFeatureBar(), 0, 6);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        composer.Controls.Add(_input, 0, 0);
        composer.Controls.Add(_send, 1, 0);
        layout.Controls.Add(composer, 0, 7);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_feedback, 1, 0);
        layout.Controls.Add(footer, 0, 8);
        Controls.Add(layout);

        _server.Text = client.Settings.ServerUrl;
        _share.Checked = client.Settings.ShareActivity;
        _victories.Checked = client.Settings.ShowVictories;
        _connect.Click += async (_, _) => await SaveConnectionAsync();
        _send.Click += async (_, _) => await SendAsync();
        _input.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            { e.SuppressKeyPress = true; await SendAsync(); }
        };
        _input.TextChanged += (_, _) =>
        {
            if (!_sending && _input.Text != _pendingText) { _pendingId = Guid.NewGuid().ToString("N"); _pendingText = _input.Text; }
        };
        _victories.CheckedChanged += (_, _) => { _historyKey = "\0"; RefreshView(); };
        clearBlocks.LinkClicked += (_, _) =>
        {
            try { _client.ClearBlocks(); _historyKey = "\0"; RefreshView(); }
            catch (Exception ex) { _feedback.Text = ex.Message; }
        };
        _context.Items.Add("屏蔽此消息发送者", null, (_, _) =>
        {
            var sender = _senders.LastOrDefault(s => s.Start <= _history.SelectionStart).Sender;
            if (string.IsNullOrEmpty(sender)) return;
            try { _client.BlockSender(sender); _historyKey = "\0"; RefreshView(); }
            catch (Exception ex) { _feedback.Text = ex.Message; }
        });
        _history.ContextMenuStrip = _context;
        _history.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) _history.SelectionStart = _history.GetCharIndexFromPosition(e.Location); };
        _context.Items.Add("复制玩家完整游戏 ID", null, (_, _) =>
        {
            var message = _senders.LastOrDefault(s => s.Start <= _history.SelectionStart).Message;
            if (message != null) TryCopyText(message.Name);
        });
        _context.Items.Add("@此玩家", null, (_, _) =>
        {
            var message = _senders.LastOrDefault(s => s.Start <= _history.SelectionStart).Message;
            var member = _client.Members.FirstOrDefault(m => m.Name == message?.Name && m.Region == message?.Region);
            if (member != null) Mention(member); else _feedback.Text = "该玩家不在线，暂时无法提及";
        });
        var recruitmentDetails = _context.Items.Add("查看开黑招募 / 加入", null, (_, _) => OpenSelectedRecruitment());
        _context.Opening += (_, _) => recruitmentDetails.Enabled = SelectedRecruitmentId() != null;
        _history.MouseDoubleClick += (_, _) => OpenSelectedRecruitment();
        foreach (string field in new[] { "名称", "编号", "完整游戏ID", "频道标识" })
            _memberContext.Items.Add("复制" + field, null, (_, _) => CopyMember(field));
        _memberContext.Items.Add("@此成员", null, (_, _) => WithMember(Mention));
        var follow = _memberContext.Items.Add("关注 / 取消关注", null, (_, _) => WithMember(m =>
        {
            try { _client.ToggleFollow(m.Id); _memberKey = ""; RefreshView(); }
            catch (Exception ex) { _feedback.Text = ex.Message; }
        }));
        _members.ContextMenuStrip = _memberContext;
        _members.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) _members.SelectedIndex = _members.IndexFromPoint(e.Location);
        };
        _memberContext.Opening += (_, e) =>
        {
            e.Cancel = _members.SelectedIndex < 0;
            if (!e.Cancel) follow.Text = _client.IsFollowed(_visibleMembers[_members.SelectedIndex].Id) ? "取消关注" : "关注上线提醒";
        };
        _members.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C) { CopyMember("完整游戏ID"); e.SuppressKeyPress = true; }
        };
        _refresh.Tick += (_, _) => RefreshView();
        _refresh.Start();
        ApplyTheme(UiTheme.Palette);
        RefreshView();
    }

    internal static string MemberCopyText(LoungeMember member, string field)
    {
        int separator = member.Name.LastIndexOf('#');
        return field switch
        {
            "名称" => separator > 0 ? member.Name[..separator] : member.Name,
            "编号" => separator >= 0 ? member.Name[(separator + 1)..] : "",
            "频道标识" => member.Id,
            _ => member.Name
        };
    }

    private void CopyMember(string field)
    {
        int index = _members.SelectedIndex;
        if (index < 0 || index >= _visibleMembers.Length) return;
        string text = MemberCopyText(_visibleMembers[index], field);
        if (string.IsNullOrWhiteSpace(text)) { _feedback.Text = "该成员暂无" + field; return; }
        try { Clipboard.SetText(text); _feedback.Text = "已复制" + field; }
        catch (System.Runtime.InteropServices.ExternalException) { _feedback.Text = "剪贴板忙，请重试"; }
    }

    private async Task SaveConnectionAsync()
    {
        _connect.Enabled = false;
        try
        {
            await _client.ConfigureAsync(_server.Text, _share.Checked, _victories.Checked);
            if (!IsDisposed) _feedback.Text = "设置已保存";
        }
        catch (Exception ex) { if (!IsDisposed) _feedback.Text = ex.Message; }
        finally { if (!IsDisposed) { _connect.Enabled = true; RefreshView(); } }
    }

    private async Task SendAsync()
    {
        if (_sending || string.IsNullOrWhiteSpace(_input.Text)) return;
        _sending = true;
        _send.Enabled = _input.Enabled = false;
        _feedback.Text = "发送中…";
        try
        {
            var mentioned = _mentionTargets.Where(m => _input.Text.Contains("@" + m.Value, StringComparison.Ordinal)).Select(m => m.Key).ToArray();
            await _client.SendPostAsync(new LoungeSend(_pendingId, _pendingSticker ?? _input.Text,
                _pendingSticker == null ? "chat" : "sticker", _pendingSticker == null ? mentioned : null));
            if (!IsDisposed) { ClearSticker(); _mentionTargets.Clear(); _pendingId = Guid.NewGuid().ToString("N"); _feedback.Text = "已发送"; }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _feedback.Text = ex is OperationCanceledException ? "未确认发送结果，可原样重试" : "发送失败：" + ex.Message;
        }
        finally
        {
            _sending = false;
            if (!IsDisposed) { _input.Enabled = true; _send.Enabled = _client.CanSend; _input.Focus(); }
        }
    }

    private void RefreshView()
    {
        if (IsDisposed) return;
        _client.SetReading(Visible && FindForm() is { Visible: true, ContainsFocus: true, WindowState: not FormWindowState.Minimized });
        UpdateFeatureBar();
        _status.Text = _client.Status;
        _send.Enabled = !_sending && _client.CanSend;
        var members = _client.Members;
        string memberKey = string.Join('|', members.Select(m => $"{m.Id}:{m.Name}:{m.Region}:{m.Status}:{m.Rank}:{_client.IsFollowed(m.Id)}"));
        if (_memberKey != memberKey)
        {
            _memberKey = memberKey;
            string? selected = _members.SelectedIndex >= 0 && _members.SelectedIndex < _visibleMembers.Length
                ? _visibleMembers[_members.SelectedIndex].Id : null;
            _visibleMembers = members;
            _members.BeginUpdate();
            _members.Items.Clear();
            foreach (var member in members) _members.Items.Add($"{(_client.IsFollowed(member.Id) ? "★" : "●")} {member.Name} · {member.Region} · {member.Status}" +
                (string.IsNullOrWhiteSpace(member.Rank) ? "" : " · " + member.Rank));
            _members.SelectedIndex = Array.FindIndex(members, m => m.Id == selected);
            _members.EndUpdate();
        }
        _memberCount.Text = $"在线成员 · {members.Length} · 右键复制 ID";
        var blocked = _client.Settings.BlockedSenders ?? [];
        var messages = _client.Messages.Where(m => (_victories.Checked || m.Kind is not ("victory" or "highlight")) && !blocked.Contains(m.SenderId)).ToArray();
        string key = string.Join('|', messages.Select(m => $"{m.Id}:{m.Kind}")) + string.Join('|',
            _client.Recruitments.Select(r => $"{r.MessageId}:{r.Revision}:{r.Status}"));
        if (_historyKey == key) return;
        _historyKey = key;
        int selection = _history.SelectionStart;
        bool atBottom = _history.TextLength == 0 || _history.GetPositionFromCharIndex(_history.TextLength).Y <= _history.ClientSize.Height;
        Point scroll = default;
        HistoryScrollMessage(_history.Handle, 0x04DD, 0, ref scroll); // EM_GETSCROLLPOS
        _history.Clear();
        _senders.Clear();
        if (messages.Length == 0)
        {
            _history.SelectionColor = UiTheme.Palette.TextSecondary;
            _history.AppendText("暂无消息。连接统一服务器后，即可在这里查看公共聊天。\n");
        }
        foreach (var message in messages)
        {
            _senders.Add((_history.TextLength, message.SenderId, message));
            _history.SelectionColor = message.Kind is "victory" or "online" or "highlight" or "recruit" ? UiTheme.Palette.Accent : UiTheme.Palette.TextSecondary;
            _history.AppendText($"{message.Name} · {message.Region}    {message.SentAt.ToLocalTime():MM-dd HH:mm:ss}\n");
            _history.SelectionColor = LoungeContent.Mentions(message).Contains(_client.CurrentPlayerId)
                ? UiTheme.Palette.Accent : UiTheme.Palette.TextPrimary;
            if (message.Kind == "sticker" && LoungeContent.Sticker(message.Text) is { } sticker)
                LoungeStickerArt.Append(_history, sticker);
            else if (message.Kind == "recruit" && _client.Recruitments.FirstOrDefault(r => r.MessageId == message.Id) is { } recruitment)
                _history.AppendText(RecruitmentSummary(recruitment));
            else _history.AppendText(LoungeContent.Display(message));
            _history.AppendText("\n\n");
        }
        _history.SelectionStart = atBottom ? _history.TextLength : Math.Min(selection, _history.TextLength);
        if (atBottom)
        {
            Point current = default;
            HistoryScrollMessage(_history.Handle, 0x04DD, 0, ref current);
            scroll.Y = Math.Max(0, current.Y + _history.GetPositionFromCharIndex(_history.TextLength).Y + _history.Font.Height * 2 - _history.ClientSize.Height);
        }
        HistoryScrollMessage(_history.Handle, 0x04DE, 0, ref scroll); // EM_SETSCROLLPOS
        if (atBottom) _history.BeginInvoke(() =>
        {
            if (_history.IsDisposed) return;
            Point position = default;
            HistoryScrollMessage(_history.Handle, 0x04DD, 0, ref position);
            position.Y = Math.Max(0, position.Y + _history.GetPositionFromCharIndex(_history.TextLength).Y + _history.Font.Height * 2 - _history.ClientSize.Height);
            HistoryScrollMessage(_history.Handle, 0x04DE, 0, ref position);
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint HistoryScrollMessage(nint window, uint message, nint parameter, ref Point point);

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _history.BackColor = _members.BackColor = palette.SurfaceRaised;
        _history.ForeColor = _members.ForeColor = palette.TextPrimary;
        foreach (var button in _featureButtons)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = palette.Border;
            button.UseVisualStyleBackColor = false;
            button.BackColor = palette.SurfaceRaised;
            button.ForeColor = palette.TextPrimary;
        }
        _historyKey = "\0";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _client.SetReading(false); _refresh.Dispose(); _context.Dispose(); _memberContext.Dispose(); }
        base.Dispose(disposing);
    }
}
