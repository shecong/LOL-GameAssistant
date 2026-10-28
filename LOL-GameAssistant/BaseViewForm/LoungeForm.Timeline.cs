using Panel = AntdUI.Panel;
using ToolTip = LOL_GameAssistant.Helper.UiToolTip;
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
    private readonly AntdUI.Select _messageFilter = new() { Name = "messageFilter", List = true, DropDownArrow = true, Width = 110, Height = 32 };
    private readonly AntdUI.Button _older = new() { Name = "loadOlderMessages", Text = "加载更早消息", AutoSize = true };
    private readonly AntdUI.Button _newMessages = new() { Name = "jumpToLatestMessages", Text = "返回最新消息", AutoSize = true, Visible = false };
    private readonly AntdUI.Label _connectionState = new() { Dock = DockStyle.Right, Width = 105, TextAlign = ContentAlignment.MiddleCenter };
    private readonly AntdUI.Label _counter = new() { Dock = DockStyle.Right, Width = 72, Text = "0/500", TextAlign = ContentAlignment.MiddleRight };
    private readonly ToolTip _memberTips = new();
    private readonly AntdUI.Label _onlineNotice = new() { Name = "onlineNotice", Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
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
        heading.Controls.Add(new AntdUI.Label { Text = "公共休息室", Font = new Font(UiMetrics.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill });
        heading.Controls.Add(_connectionState); heading.Controls.Add(settings); layout.Controls.Add(heading, 0, 0);
        var description = layout.GetControlFromPosition(0, 1)!; layout.Controls.Remove(description); description.Dispose();
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        _messageFilter.Items.AddRange(new object[] { "全部", "聊天", "战绩", "招募", "上线通知" }); _messageFilter.SelectedIndex = 0;
        _memberTips.SetToolTip(_messageFilter, "上线通知单独保留最近 200 条，不计入全部消息；其他消息可向上翻页加载");
        filters.Controls.AddRange([_messageFilter, _older, _newMessages]);
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.AutoSize = true;
        toolbar.Controls.Add(filters, 0, 0); toolbar.Controls.Add(_onlineNotice, 1, 0);
        layout.Controls.Add(toolbar, 0, 1); layout.RowStyles[1].Height = 38;
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
        _members.CellHover += (_, e) =>
        {
            int index = _members.DataSource is LoungeListRow[] rows ? Array.IndexOf(rows, e.Record) : -1;
            _memberTips.SetToolTip(_members, index >= 0 && index < _visibleMembers.Length ? $"{_visibleMembers[index].Name}\n{NormalizeRegion(_visibleMembers[index].Region)} · {_visibleMembers[index].Rank}\n右键复制 ID、提及或关注" : "");
        };
        var footer = (TableLayoutPanel)layout.GetControlFromPosition(0, 8)!;
        footer.ColumnCount = 3; footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); footer.Controls.Add(_counter, 2, 0);
        _input.TextChanged += (_, _) => _counter.Text = $"{_input.Text.Length}/{LoungeRules.MaxTextLength}";
        _featureButtons.AddRange([_older, _newMessages]);
    }

    internal static string NormalizeRegion(string text) => Regex.Replace(text ?? "", @"未知大区(?:[（(][^）)]*[）)])?", "地区待确认", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private void InitializeTimeline()
    {
        _history.HandleCreated += (_, _) => _history.BeginInvoke(() => { if (!_history.IsDisposed) ScrollToBottom(); });
        _messageFilter.SelectedIndexChanged += (_, _) => { _historyKey = "\0"; _renderedKeys = []; RefreshView(); };
        _older.Click += async (_, _) => await LoadOlderMessagesAsync();
        _newMessages.Click += (_, _) => JumpToLatest();
        _history.MouseDown += (_, _) => _scrollInteraction++;
        _history.KeyDown += (_, _) => _scrollInteraction++;
        _history.MouseWheel += async (_, e) => { _scrollInteraction++; if (e.Delta > 0 && ScrollPosition().Y <= 4) await LoadOlderMessagesAsync(); };
        _history.ScrollBar.ValueYChanged += async (_, _) =>
        {
            int position = ScrollPosition().Y;
            bool upward = _lastScrollY > 4 && position <= 4;
            _lastScrollY = position;
            if (!_renderingHistory && upward && _history.Focused) await LoadOlderMessagesAsync();
        };
    }
    private Point ScrollPosition() => new(0, _history.ScrollBar.ValueY);
    private bool IsHistoryAtBottom() => _history.Items.Count == 0 || _history.IsBottom;
    private void ScrollToBottom() => _history.ToBottom();
    private void ClearHistoryItems()
    {
        foreach (var item in _history.Items) item.Dispose();
        _history.Items.Clear();
        foreach (var image in _historyImages) image.Dispose();
        _historyImages.Clear();
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
        _newMessages.Visible = _messageFilter.SelectedIndex != 4 && (_pendingMessages.Count > 0 || _window.Count > 0 && _window.Keys.Last() < _newestSeen);
        _newMessages.Text = _pendingMessages.Count > 0 ? $"{_pendingMessages.Count}{(_pendingMessages.Count == 1000 ? "+" : "")} 条新消息 ↓" : "返回最新消息 ↓";
        _older.Visible = _messageFilter.SelectedIndex != 4;
        if (!_loadingOlder) { _older.Enabled = _hasOlder && _window.Count > 0; _older.Text = _hasOlder ? "加载更早消息" : "已到最早消息"; }
        RenderTimeline(forceBottom: forceBottom);
    }
    private void RenderTimeline(bool preserveAnchor = false, bool forceBottom = false)
    {
        var blocked = _client.Settings.BlockedSenders ?? [];
        IEnumerable<LoungeMessage> source = _messageFilter.SelectedIndex == 4 ? _client.OnlineMessages : _window.Values;
        var messages = source.Where(m => !blocked.Contains(m.SenderId) && (_victories.Checked || m.Kind is not ("victory" or "highlight")))
            .Where(m => _messageFilter.SelectedIndex switch { 1 => m.Kind is "chat" or "sticker", 2 => m.Kind is "victory" or "highlight", 3 => m.Kind == "recruit", 4 => m.Kind == "online", _ => m.Kind != "online" })
            .Select(m => _client.IsDeleted(m.Id) ? m with { Kind = "deleted", Text = "此消息已被管理员删除" } : m).ToArray();
        string[] keys = messages.Select(m => $"{m.Id}:{m.Kind}:{m.Text}:{_client.Recruitments.FirstOrDefault(r => r.MessageId == m.Id)?.Revision}").ToArray();
        string key = string.Join('|', keys) + ":" + _messageFilter.SelectedIndex;
        if (_historyKey == key && !forceBottom) return;
        _historyKey = key;
        int sequence = ++_renderSequence;
        bool bottom = forceBottom || !preserveAnchor && IsHistoryAtBottom();
        int scroll = _history.ScrollBar.ValueY;
        var anchor = _history.Items.FirstOrDefault(item => item.Tag is LoungeMessage && item.rect.Bottom > scroll);
        long? anchorId = (anchor?.Tag as LoungeMessage)?.Id;
        int anchorY = anchor == null ? 0 : anchor.rect.Top - scroll;
        bool append = !preserveAnchor && _renderedKeys.Length > 0 && keys.Length >= _renderedKeys.Length && keys.Take(_renderedKeys.Length).SequenceEqual(_renderedKeys);
        int start = append ? _renderedKeys.Length : 0;
        _renderingHistory = true;
        try
        {
            if (!append) { ClearHistoryItems(); _lastRenderedMessage = null; _lastRenderedDate = null; }

            if (messages.Length == 0) _history.Items.Add(new AntdUI.Chat.TipChatItem("暂无符合条件的消息；可切换筛选或加载更早消息。"));
            foreach (var message in messages.Skip(start)) AppendTimelineMessage(message);
            _renderedKeys = keys;
            if (_selectedHistoryMessage != null && !messages.Any(m => m.Id == _selectedHistoryMessage.Id)) _selectedHistoryMessage = null;
            if (bottom)
            {
                ScrollToBottom();
                int interaction = _scrollInteraction;
                if (_history.IsHandleCreated)
                    _history.BeginInvoke(() => { if (!_history.IsDisposed && sequence == _renderSequence && interaction == _scrollInteraction) ScrollToBottom(); });
            }
            else
            {
                var found = _history.Items.FirstOrDefault(item => item.Tag is LoungeMessage m && m.Id == anchorId);
                _history.ScrollBar.ValueY = Math.Max(0, found == null ? scroll : found.rect.Top - anchorY);
            }
        }
        finally { _renderingHistory = false; }
    }
    private void AppendTimelineMessage(LoungeMessage message)
    {
        var time = message.SentAt.ToLocalTime();
        if (_lastRenderedDate != time.Date)
        {
            _history.Items.Add(new AntdUI.Chat.TipChatItem(time.ToString("yyyy-MM-dd")));
            _lastRenderedDate = time.Date;
        }
        if (message.Kind == "online")
            _history.Items.Add(new AntdUI.Chat.TipChatItem($"{message.Name} 加入休息室  [{NormalizeRegion(message.Region)}]  {time:HH:mm}") { Tag = message });
        else
        {
            bool group = message.Kind == "chat" && _lastRenderedMessage is { Kind: "chat" } previous && previous.SenderId == message.SenderId && previous.Name == message.Name && previous.Region == message.Region && message.SentAt - previous.SentAt < TimeSpan.FromMinutes(3);
            string rank = _visibleMembers.FirstOrDefault(m => m.Name == message.Name && m.Region == message.Region)?.Rank ?? "";
            string name = group ? "" : $"{message.Name}  [{NormalizeRegion(message.Region)}]{(rank.Length > 0 ? " [" + rank + "]" : "")}  {time:HH:mm}";
            if (message.Kind == "sticker" && LoungeContent.Sticker(message.Text) is { } sticker)
            {
                var image = LoungeStickerArt.Create(sticker); _historyImages.Add(image);
                _history.Items.Add(new AntdUI.Chat.ImageChatItem(image, null!, name) { Width = 156, Height = 132, Tag = message });
            }
            else
            {
                string text = message.Kind == "recruit" && _client.Recruitments.FirstOrDefault(r => r.MessageId == message.Id) is { } recruitment
                    ? RecruitmentSummary(recruitment) : message.Kind is "victory" or "highlight"
                        ? LoungeContent.Display(message).Replace("【" + message.Name + "】", "", StringComparison.Ordinal).Trim() : LoungeContent.Display(message);
                _history.Items.Add(new AntdUI.Chat.TextChatItem(text, null!, name) { Tag = message });
            }
        }
        _lastRenderedMessage = message;
    }
}
