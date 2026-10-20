using LOL.Lounge;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class LoungeForm
{
    private const int MaximumVisibleMessages = 200;
    private readonly SortedDictionary<long, LoungeMessage> _window = [];
    private readonly HashSet<long> _pendingMessages = [];
    private readonly UiRequestScope _historyLoads = new();
    private readonly ComboBox _messageFilter = new() { Name = "messageFilter", DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Button _older = new() { Name = "loadOlderMessages", Text = "加载更早消息", AutoSize = true };
    private readonly Button _newMessages = new() { Name = "jumpToLatestMessages", Text = "返回最新消息", AutoSize = true, Visible = false };
    private readonly Label _connectionState = new() { Dock = DockStyle.Right, Width = 105, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _counter = new() { Dock = DockStyle.Right, Width = 72, Text = "0/500", TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolTip _memberTips = new();
    private readonly ContextMenuStrip _moreMenu = new();
    private string _timelineServer = "";
    private bool _windowInitialized, _loadingOlder, _hasOlder = true, _renderingHistory;
    private long _newestSeen;
    private string[] _renderedKeys = [];
    private LoungeMessage? _lastRenderedMessage;
    private DateTime? _lastRenderedDate;
    private int _lastScrollY;
    private int _renderSequence, _scrollInteraction;

    private void InitializePresentation(TableLayoutPanel layout, Control connection, Control options, TableLayoutPanel conversation)
    {
        layout.RowStyles[0].Height = 48;
        var oldTitle = layout.GetControlFromPosition(0, 0)!; layout.Controls.Remove(oldTitle); oldTitle.Dispose();
        var heading = new Panel { Dock = DockStyle.Fill };
        var settings = new AntdUI.Button { Name = "loungeSettings", Text = "通知设置 ⚙", Dock = DockStyle.Right, Width = 120 };
        heading.Controls.Add(new Label { Text = "公共休息室", Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill });
        heading.Controls.Add(_connectionState); heading.Controls.Add(settings); layout.Controls.Add(heading, 0, 0);
        var description = layout.GetControlFromPosition(0, 1)!; layout.Controls.Remove(description); description.Dispose();
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        _messageFilter.Items.AddRange(new object[] { "全部", "聊天", "战绩", "招募" }); _messageFilter.SelectedIndex = 0;
        _memberTips.SetToolTip(_messageFilter, "筛选已加载的消息；向上翻页可继续加载更早内容");
        filters.Controls.AddRange([_messageFilter, _older, _newMessages]); layout.Controls.Add(filters, 0, 1); layout.RowStyles[1].Height = 38;
        var preferences = layout.GetControlFromPosition(0, 4)!;
        bool settingsOpen = string.IsNullOrWhiteSpace(_client.Settings.ServerUrl);
        void ShowSettings()
        {
            connection.Visible = options.Visible = preferences.Visible = settingsOpen;
            layout.RowStyles[2].Height = settingsOpen ? 42 : 0;
            layout.RowStyles[3].Height = settingsOpen ? 32 : 0;
            layout.RowStyles[4].Height = settingsOpen ? 36 : 0;
        }
        settings.Click += (_, _) => { settingsOpen = !settingsOpen; ShowSettings(); }; ShowSettings();
        conversation.ColumnStyles[1].Width = 280;
        _members.HorizontalScrollbar = false; _members.DrawMode = DrawMode.OwnerDrawFixed; _members.ItemHeight = 82;
        _members.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= _visibleMembers.Length) return;
            var member = _visibleMembers[e.Index]; var p = UiTheme.Palette;
            using var background = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? p.SurfaceMuted : p.SurfaceRaised);
            e.Graphics.FillRectangle(background, e.Bounds);
            using var bold = new Font(Font, FontStyle.Bold);
            var rect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 8, e.Bounds.Width - 16, 24);
            TextRenderer.DrawText(e.Graphics, (_client.IsFollowed(member.Id) ? "★ " : "● ") + member.Name, bold, rect, p.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            rect.Y += 25; rect.Height = 22;
            TextRenderer.DrawText(e.Graphics, NormalizeRegion(member.Region) + (member.Rank.Length > 0 ? " · " + member.Rank : ""), Font, rect, p.TextSecondary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            rect.Y += 22;
            TextRenderer.DrawText(e.Graphics, member.Status, Font, rect, p.Accent, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        int hovered = -1;
        _members.MouseMove += (_, e) =>
        {
            int index = _members.IndexFromPoint(e.Location); if (index == hovered) return; hovered = index;
            _memberTips.SetToolTip(_members, index >= 0 && index < _visibleMembers.Length ? $"{_visibleMembers[index].Name}\n{NormalizeRegion(_visibleMembers[index].Region)} · {_visibleMembers[index].Rank}\n右键复制 ID、提及或关注" : "");
        };
        var footer = (TableLayoutPanel)layout.GetControlFromPosition(0, 8)!;
        footer.ColumnCount = 3; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); footer.Controls.Add(_counter, 2, 0);
        _input.TextChanged += (_, _) => _counter.Text = $"{_input.TextLength}/{LoungeRules.MaxTextLength}";
        _featureButtons.AddRange([_older, _newMessages]);
    }

    internal static string NormalizeRegion(string text) => Regex.Replace(text ?? "", @"未知大区(?:[（(][^）)]*[）)])?", "地区待确认", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private void InitializeTimeline()
    {
        _messageFilter.SelectedIndexChanged += (_, _) => { _historyKey = "\0"; _renderedKeys = []; RefreshView(); };
        _older.Click += async (_, _) => await LoadOlderMessagesAsync();
        _newMessages.Click += (_, _) => JumpToLatest();
        _history.MouseDown += (_, _) => _scrollInteraction++;
        _history.KeyDown += (_, _) => _scrollInteraction++;
        _history.MouseWheel += async (_, e) => { _scrollInteraction++; if (e.Delta > 0 && ScrollPosition().Y <= 4) await LoadOlderMessagesAsync(); };
        _history.VScroll += async (_, _) =>
        {
            int position = ScrollPosition().Y;
            bool upward = _lastScrollY > 4 && position <= 4;
            _lastScrollY = position;
            if (!_renderingHistory && upward && _history.Focused) await LoadOlderMessagesAsync();
        };
    }
    private Point ScrollPosition() { Point point = default; if (_history.IsHandleCreated) HistoryScrollMessage(_history.Handle, 0x04DD, 0, ref point); return point; }
    private bool IsHistoryAtBottom() => _history.TextLength == 0 || _history.GetPositionFromCharIndex(_history.TextLength - 1).Y <= _history.ClientSize.Height - _history.Font.Height;
    private void ScrollToBottom()
    {
        if (_history.TextLength == 0) return;
        var position = ScrollPosition();
        position.Y = Math.Max(0, position.Y + _history.GetPositionFromCharIndex(_history.TextLength).Y + _history.Font.Height * 2 - _history.ClientSize.Height);
        HistoryScrollMessage(_history.Handle, 0x04DE, 0, ref position);
    }
    private void JumpToLatest()
    {
        _window.Clear(); foreach (var message in _client.Messages.TakeLast(LoungeRules.HistoryPageSize)) _window[message.Id] = message;
        _windowInitialized = _window.Count > 0; _pendingMessages.Clear(); _hasOlder = true; _renderedKeys = []; _historyKey = "\0";
        RefreshTimeline(forceBottom: true);
    }
    private async Task LoadOlderMessagesAsync()
    {
        if (_loadingOlder || !_hasOlder || _window.Count == 0 || IsDisposed) return;
        _loadingOlder = true; _older.Enabled = false; _older.Text = "正在加载…";
        using var request = _historyLoads.Begin(); string server = _client.Settings.ServerUrl; long before = _window.Keys.First();
        try
        {
            var page = await _client.LoadOlderAsync(before, request.Token);
            if (!request.IsCurrent || IsDisposed || server != _client.Settings.ServerUrl) return;
            var older = page.Messages.Where(m => m.Id < before).DistinctBy(m => m.Id).OrderBy(m => m.Id).ToArray();
            foreach (var message in older) _window[message.Id] = message;
            _hasOlder = page.HasMore && older.Length > 0;
            while (_window.Count > MaximumVisibleMessages) _window.Remove(_window.Keys.Last());
            _historyKey = "\0"; RenderTimeline(preserveAnchor: true);
            _feedback.Text = older.Length == 0 ? "没有更早的消息了" : $"已加载更早 {older.Length} 条消息";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed && request.IsCurrent) _feedback.Text = LoungeClient.SendError(e); }
        finally
        {
            _loadingOlder = false;
            if (!IsDisposed) { _older.Enabled = _hasOlder; _older.Text = _hasOlder ? "加载更早消息" : "已到最早消息"; }
        }
    }
    private void RefreshTimeline(bool forceBottom = false)
    {
        string server = _client.Settings.ServerUrl;
        if (_timelineServer != server)
        {
            _historyLoads.Cancel(); _timelineServer = server; _window.Clear(); _pendingMessages.Clear(); _windowInitialized = false;
            _newestSeen = 0; _hasOlder = true; _renderedKeys = []; _historyKey = "\0";
        }
        var latest = _client.Messages;
        if (!_windowInitialized && latest.Length > 0)
        {
            foreach (var message in latest.TakeLast(LoungeRules.HistoryPageSize)) _window[message.Id] = message;
            _windowInitialized = true; _newestSeen = latest[^1].Id; forceBottom = true;
        }
        bool atBottom = forceBottom || IsHistoryAtBottom();
        foreach (var message in latest)
        {
            if (_window.ContainsKey(message.Id)) _window[message.Id] = message;
            else if (message.Id > _newestSeen)
            {
                if (atBottom && (_window.Count == 0 || _window.Keys.Last() >= _newestSeen)) _window[message.Id] = message;
                else if (_pendingMessages.Count < 1000) _pendingMessages.Add(message.Id);
            }
        }
        if (latest.Length > 0) _newestSeen = Math.Max(_newestSeen, latest[^1].Id);
        while (_window.Count > MaximumVisibleMessages) _window.Remove(_window.Keys.First());
        _newMessages.Visible = _pendingMessages.Count > 0 || _window.Count > 0 && _window.Keys.Last() < _newestSeen;
        _newMessages.Text = _pendingMessages.Count > 0 ? $"{_pendingMessages.Count}{(_pendingMessages.Count == 1000 ? "+" : "")} 条新消息 ↓" : "返回最新消息 ↓";
        if (!_loadingOlder) { _older.Enabled = _hasOlder && _window.Count > 0; _older.Text = _hasOlder ? "加载更早消息" : "已到最早消息"; }
        RenderTimeline(forceBottom: forceBottom);
    }
    private void RenderTimeline(bool preserveAnchor = false, bool forceBottom = false)
    {
        var blocked = _client.Settings.BlockedSenders ?? [];
        var online = new Dictionary<string, DateTimeOffset>();
        var messages = _window.Values.Where(m => !blocked.Contains(m.SenderId) && (_victories.Checked || m.Kind is not ("victory" or "highlight")))
            .Where(m => _messageFilter.SelectedIndex switch { 1 => m.Kind is "chat" or "sticker", 2 => m.Kind is "victory" or "highlight", 3 => m.Kind == "recruit", _ => true })
            .Select(m => _client.IsDeleted(m.Id) ? m with { Kind = "deleted", Text = "此消息已被管理员删除" } : m)
            .Where(m => { if (m.Kind != "online") return true; string identity = m.SenderId + "|" + m.Name; bool show = !online.TryGetValue(identity, out var at) || m.SentAt - at > TimeSpan.FromMinutes(5); online[identity] = m.SentAt; return show; }).ToArray();
        string[] keys = messages.Select(m => $"{m.Id}:{m.Kind}:{m.Text}:{_client.Recruitments.FirstOrDefault(r => r.MessageId == m.Id)?.Revision}").ToArray();
        string key = string.Join('|', keys) + ":" + _messageFilter.SelectedIndex;
        if (_historyKey == key && !forceBottom) return;
        _historyKey = key;
        int sequence = ++_renderSequence;
        bool bottom = forceBottom || !preserveAnchor && IsHistoryAtBottom();
        int firstChar = _history.GetCharIndexFromPosition(Point.Empty);
        var anchor = _senders.LastOrDefault(s => s.Start <= firstChar);
        if (anchor.Message == null && _senders.Count > 0) anchor = _senders[0];
        int anchorY = anchor.Message == null ? 0 : _history.GetPositionFromCharIndex(anchor.Start).Y;
        var scroll = ScrollPosition(); int selection = _history.SelectionStart;
        bool append = !preserveAnchor && _renderedKeys.Length > 0 && keys.Length >= _renderedKeys.Length && keys.Take(_renderedKeys.Length).SequenceEqual(_renderedKeys);
        int start = append ? _renderedKeys.Length : 0;
        _renderingHistory = true;
        try
        {
            if (!append) { _history.Clear(); _senders.Clear(); _lastRenderedMessage = null; _lastRenderedDate = null; }
            _history.SelectionStart = _history.TextLength;
            if (messages.Length == 0) _history.AppendText("暂无符合条件的消息；可切换筛选或加载更早消息。\n");
            foreach (var message in messages.Skip(start)) AppendTimelineMessage(message);
            _renderedKeys = keys;
            if (bottom)
            {
                ScrollToBottom();
                int interaction = _scrollInteraction;
                _history.BeginInvoke(() => { if (!_history.IsDisposed && sequence == _renderSequence && interaction == _scrollInteraction) ScrollToBottom(); });
            }
            else
            {
                _history.SelectionStart = Math.Min(selection, _history.TextLength); _history.SelectionLength = 0;
                var found = anchor.Message == null ? default : _senders.FirstOrDefault(s => s.Message.Id == anchor.Message.Id);
                if (found.Message != null) scroll.Y = Math.Max(0, ScrollPosition().Y + _history.GetPositionFromCharIndex(found.Start).Y - anchorY);
                HistoryScrollMessage(_history.Handle, 0x04DE, 0, ref scroll);
            }
        }
        finally { _renderingHistory = false; }
    }
    private void AppendTimelineMessage(LoungeMessage message)
    {
        var p = UiTheme.Palette; var time = message.SentAt.ToLocalTime();
        if (_lastRenderedDate != time.Date)
        {
            _history.SelectionColor = p.TextSecondary; _history.AppendText($"──────── {time:yyyy-MM-dd} ────────\n\n"); _lastRenderedDate = time.Date;
        }
        _senders.Add((_history.TextLength, message.SenderId, message));
        if (message.Kind == "online")
        {
            _history.SelectionColor = p.TextSecondary; _history.AppendText($"· {message.Name} 加入休息室  [{NormalizeRegion(message.Region)}]  {time:HH:mm}\n\n");
        }
        else
        {
            bool group = message.Kind == "chat" && _lastRenderedMessage is { Kind: "chat" } previous && previous.SenderId == message.SenderId && previous.Name == message.Name && previous.Region == message.Region && message.SentAt - previous.SentAt < TimeSpan.FromMinutes(3);
            if (!group)
            {
                _history.SelectionColor = p.Accent;
                string rank = _visibleMembers.FirstOrDefault(m => m.Name == message.Name && m.Region == message.Region)?.Rank ?? "";
                _history.AppendText($"{message.Name}  [{NormalizeRegion(message.Region)}]{(rank.Length > 0 ? " [" + rank + "]" : "")}  {time:HH:mm}\n");
            }
            _history.SelectionColor = LoungeContent.Mentions(message).Contains(_client.CurrentPlayerId) ? p.Accent : p.TextPrimary;
            if (message.Kind == "sticker" && LoungeContent.Sticker(message.Text) is { } sticker) Infrastructure.Lounge.LoungeStickerArt.Append(_history, sticker);
            else if (message.Kind == "recruit" && _client.Recruitments.FirstOrDefault(r => r.MessageId == message.Id) is { } recruitment) _history.AppendText(RecruitmentSummary(recruitment));
            else _history.AppendText(message.Kind is "victory" or "highlight" ? LoungeContent.Display(message).Replace("【" + message.Name + "】", "", StringComparison.Ordinal).Trim() : LoungeContent.Display(message));
            _history.AppendText("\n\n");
        }
        _lastRenderedMessage = message;
    }
}
