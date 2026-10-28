using Panel = AntdUI.Panel;
using LOL.Lounge;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Public lounge view. The application owns the connection even when this page is hidden.</summary>
public sealed partial class LoungeForm : UserControl, IThemeAware
{
    private readonly LoungeClient _client;
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly AntdUI.Input _server = new() { Dock = DockStyle.Fill, PlaceholderText = "http://服务器IP:5000 或 https://域名" };
    private readonly AntdUI.Checkbox _share = new() { Text = "公开游戏状态与战绩", AutoSize = true, Checked = true };
    private readonly AntdUI.Checkbox _victories = new() { Text = "显示战绩播报", AutoSize = true, Checked = true };
    private readonly AntdUI.Button _connect = new() { Text = "保存并连接", Dock = DockStyle.Fill, Type = AntdUI.TTypeMini.Primary };
    // RichEdit preserves inline sticker images, text selection and the reading anchor when prepending history.
    private readonly AntdUI.Chat.ChatList _history = new() { Dock = DockStyle.Fill, IconLess = true, EnabledClickImage = false };
    private readonly AntdUI.Table _members = CreateLoungeList();
    private readonly AntdUI.Select _memberServerFilter = new() { Name = "memberServerFilter", AccessibleName = "成员服务器筛选",
        Dock = DockStyle.Top, Height = 34, List = true, DropDownArrow = true };
    private readonly AntdUI.Label _memberCount = new() { Dock = DockStyle.Top, Height = 36, Text = "在线成员 · 0", TextAlign = ContentAlignment.MiddleLeft };
    private readonly AntdUI.Input _input = new() { Dock = DockStyle.Fill, Multiline = true, MaxLength = LoungeRules.MaxTextLength,
        PlaceholderText = "登录 LOL 后发言，Enter 发送，Shift+Enter 换行（最多 500 字）" };
    private readonly AntdUI.Button _send = new() { Text = "发送", Dock = DockStyle.Fill, Type = AntdUI.TTypeMini.Primary };
    private readonly AntdUI.Label _feedback = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };
    private LoungeMember[] _visibleMembers = [];
    private LoungeMessage? _selectedHistoryMessage;
    private LoungeMessage? SelectedHistoryMessage => _selectedHistoryMessage ?? _history.FocusedChatItem?.Tag as LoungeMessage;
    private readonly List<Image> _historyImages = [];
    private string _historyKey = "\0";
    private string _memberKey = "";
    private string _pendingId = Guid.NewGuid().ToString("N");
    private string _pendingText = "";
    private bool _sending;
    private bool _resourcesDisposed;

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
        layout.Controls.Add(new AntdUI.Label { Text = "lol休息室", Font = new Font(UiMetrics.FontFamily, 18F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new AntdUI.Label { Text = "所有助手用户共享一个频道 · 昵称与大区公开展示 · 未登录 LOL 时可查看消息", Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 1);
        var connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        connection.Controls.Add(new AntdUI.Label { Text = "服务器地址", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        connection.Controls.Add(_server, 1, 0);
        connection.Controls.Add(_connect, 2, 0);
        layout.Controls.Add(connection, 0, 2);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var clearBlocks = new AntdUI.Button { Text = "清空屏蔽名单", AutoSize = true, Ghost = true, Margin = new Padding(12, 3, 0, 0) };
        options.Controls.AddRange([_share, _victories, clearBlocks]);
        layout.Controls.Add(options, 0, 3);
        layout.Controls.Add(BuildPreferences(), 0, 4);
        var conversation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 8) };
        conversation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        conversation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        conversation.Controls.Add(_history, 0, 0);
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 0, 0) };
        sidebar.Controls.Add(_members);
        _memberServerFilter.Items.AddRange(new object[] { "全部服务器", "国服", "其他服务器", "服务器待确认" });
        _memberServerFilter.SelectedIndex = 0;
        sidebar.Controls.Add(_memberServerFilter);
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
        InitializePresentation(layout, connection, options, conversation);
        Controls.Add(layout);
        InitializeTimeline();

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
        clearBlocks.Click += (_, _) =>
        {
            try { _client.ClearBlocks(); _historyKey = "\0"; RefreshView(); }
            catch (Exception ex) { _feedback.Text = ex.Message; }
        };
        _history.ItemClick += (_, e) =>
        {
            _selectedHistoryMessage = e.Item.Tag as LoungeMessage;
            if (e.Button == MouseButtons.Right) ShowHistoryMenu(_history.PointToScreen(e.Location));
            else if (e.Clicks == 2) OpenSelectedRecruitment();
        };
        _history.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            { ShowHistoryMenu(_history.PointToScreen(Point.Empty)); e.SuppressKeyPress = true; }
        };
        _members.CellClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || _members.DataSource is not LoungeListRow[] rows) return;
            _members.SelectedIndex = Array.IndexOf(rows, e.Record);
            ShowMemberMenu(_members.PointToScreen(e.Location));
        };
        _members.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C) { CopyMember("完整游戏ID"); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            { ShowMemberMenu(_members.PointToScreen(Point.Empty)); e.SuppressKeyPress = true; }
        };
        _refresh.Tick += (_, _) => { if (PageActivity.IsActive(this)) RefreshView(); };
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
        _ = LoadSocialPreferencesAsync();
        _client.SetReading(_regionFilter.SelectedIndex <= 0 && _messageFilter.SelectedIndex != 4 && IsHistoryAtBottom() && _pendingMessages.Count == 0 && (_window.Count == 0 || _window.Keys.Last() >= _newestSeen) && Visible && FindForm() is { Visible: true, ContainsFocus: true, WindowState: not FormWindowState.Minimized });
        UpdateFeatureBar();
        var online = _client.OnlineNotice;
        _onlineNotice.Text = _messageFilter.SelectedIndex != 4 || online == null || !MatchesSelectedRegion(online.Region) ? "" : NormalizeRegion($"{online.Name} 加入休息室 · {online.Region}");
        _status.Text = NormalizeRegion(_client.Status);
        _connectionState.Text = _client.Status.StartsWith("已连接", StringComparison.Ordinal) ? "● 已连接" : "○ 未连接";
        _counter.Text = $"{_input.Text.Length}/{LoungeRules.MaxTextLength}";
        _send.Enabled = !_sending && _client.CanSend;
        var allMembers = _client.Members;
        var members = allMembers.Where(m => MatchesSelectedRegion(m.Region) && (_memberServerFilter.SelectedIndex <= 0 ||
            LeagueRegionNames.GetServerGroup(m.Region) == (_memberServerFilter.SelectedIndex switch
            { 1 => LeagueServerGroup.China, 2 => LeagueServerGroup.Other, _ => LeagueServerGroup.Unknown }))).ToArray();
        string memberKey = SelectedRegion + ":" + _memberServerFilter.SelectedIndex + ":" + string.Join('|', members.Select(m => $"{m.Id}:{m.Name}:{m.Region}:{m.Status}:{m.Rank}:{_client.IsFollowed(m.Id)}"));
        if (_memberKey != memberKey)
        {
            _memberKey = memberKey;
            string? selected = _members.SelectedIndex >= 0 && _members.SelectedIndex < _visibleMembers.Length
                ? _visibleMembers[_members.SelectedIndex].Id : null;
            _visibleMembers = members;
            _members.DataSource = members.Select(member => new LoungeListRow(
                $"{(_socialExperience.FavoriteTeammates.ContainsKey(member.Id) ? "♥" : _client.IsFollowed(member.Id) ? "★" : "●")} {member.Name}\n{MemberServerLabel(member.Region)} · {NormalizeRegion(member.Region)}{(string.IsNullOrWhiteSpace(member.Rank) ? "" : " · " + member.Rank)}\n{member.Status}")).ToArray();
            _members.SelectedIndex = Array.FindIndex(members, m => m.Id == selected);
        }
        _memberCount.Text = SelectedRegion == null && _memberServerFilter.SelectedIndex <= 0 ? $"在线成员 · {members.Length} · 右键复制 ID"
            : $"在线成员 · {members.Length}/{allMembers.Length} · 右键复制 ID";
        RefreshTimeline();
    }

    private static string MemberServerLabel(string region) => UiLanguage.T(LeagueRegionNames.GetServerGroup(region) switch
    { LeagueServerGroup.China => "国服", LeagueServerGroup.Other => "其他服务器", _ => "服务器待确认" });

public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette);
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _history.BackColor = palette.SurfaceRaised;
        _history.ForeColor = palette.TextPrimary; _history.ForeBubble = palette.TextPrimary;
        _history.BackBubble = palette.SurfaceMuted; _history.BackActiveBubble = palette.SurfaceMuted;
        _members.BackColor = palette.SurfaceRaised;
        _members.ForeColor = palette.TextPrimary;
        _members.RowSelectedBg = palette.SurfaceMuted;
        _members.RowSelectedFore = palette.TextPrimary;
        _onlineNotice.ForeColor = palette.TextSecondary;
        foreach (var button in _featureButtons)
        {
            button.DefaultBorderColor = palette.Border;
            button.DefaultBack = palette.SurfaceRaised;
            button.ForeColor = palette.TextPrimary;
        }
        _historyKey = "\0";
        _renderedKeys = [];
        _members.Invalidate();
    }

    private sealed record LoungeListRow(string Text);

    private static AntdUI.Table CreateLoungeList()
    {
        var table = new AntdUI.Table
        {
            Dock = DockStyle.Fill, VisibleHeader = false,
            MultipleRows = false, LostFocusClearSelection = false, ClipboardCopy = false
        };
        table.Columns.Add(new AntdUI.Column(nameof(LoungeListRow.Text), "") { Width = "100%", LineBreak = true });
        return table;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        { _resourcesDisposed = true; ClearHistoryItems(); _socialLifetime.Cancel(); _socialLifetime.Dispose(); _historyLoads.Dispose(); _memberTips.Dispose(); _client.SetReading(false); _refresh.Dispose(); }
        base.Dispose(disposing);
    }
}
