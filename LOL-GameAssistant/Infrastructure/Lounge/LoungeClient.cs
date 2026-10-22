using LOL.Lounge;
using LOL_GameAssistant.Helper;
using Microsoft.AspNetCore.SignalR.Client;

namespace LOL_GameAssistant.Infrastructure.Lounge;

/// <summary>Application-owned background lifetime; UI pages only observe a bounded snapshot.</summary>
public sealed class LoungeClient(LoungeLcuReader local) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _configuration = new(1, 1);
    private readonly SortedDictionary<long, LoungeMessage> _messages = [];
    private readonly Queue<LoungeMessage> _onlineMessages = [];
    private long _onlineSequence;
    private readonly Dictionary<long, LoungeRecruitmentState> _recruitments = [];
    private readonly HashSet<long> _deleted = [];
    private LoungeMember[] _members = [];
    private LoungeSettings _settings = LoungeSettings.Load();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private HubConnection? _hub;
    private string _status = "尚未配置聊天室服务器";
    private LoungePresence? _presence;
    private LoungePresence? _publishedPresence;
    private readonly Dictionary<string, LoungeVictory> _pendingVictories = [];
    private bool _disposed;
    private long _cursor;
    private bool _initialSync;
    private LoungeLocalState? _activeGame;
    private DateTimeOffset? _finishedAt;
    private readonly HashSet<string> _reported = [];
    private readonly LoungeInbox _inbox = new();
    private readonly Dictionary<string, LoungeHighlight> _pendingHighlights = [];
    private LoungeLocalState? _promotionGame;
    private DateTimeOffset _promotionDeadline;
    private bool _memberBaseline;
    private bool _inGame;
    private string _ownSession = "";
    private int _protocolVersion = 1;
    private LoungeOnlineNotice? _onlineNotice;
    private DateTimeOffset _onlineNoticeUntil;
    public LoungeOnlineNotice? OnlineNotice
    {
        get { lock (_gate) return DateTimeOffset.UtcNow < _onlineNoticeUntil ? _onlineNotice : null; }
    }
    public LoungeMessage[] OnlineMessages { get { lock (_gate) return _onlineMessages.OrderBy(m => m.SentAt).ToArray(); } }
    public bool SupportsRichMessages => _protocolVersion >= 2;
    public bool SupportsRecruitment => _protocolVersion >= 3;
    public bool SupportsHistory => _protocolVersion >= 4;
    public bool IsDeleted(long id) { lock (_gate) return _deleted.Contains(id); }
    public async Task<LoungeHistoryPage> LoadOlderAsync(long before, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(before);
        token.ThrowIfCancellationRequested();
        var hub = _hub;
        if (SupportsHistory && hub?.State == HubConnectionState.Connected)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            return await hub.InvokeAsync<LoungeHistoryPage>("History", before, timeout.Token);
        }
        lock (_gate)
        {
            var cached = _messages.Values.Where(m => m.Id < before).TakeLast(LoungeRules.HistoryPageSize + 1).ToArray();
            if (cached.Length == 0) throw new InvalidOperationException(SupportsHistory ? "频道未连接，重连后可继续加载历史消息" : "服务器暂不支持历史分页，请更新 LOL-GameApi 后重连");
            return new(cached.TakeLast(LoungeRules.HistoryPageSize).ToArray(), cached.Length > LoungeRules.HistoryPageSize || !SupportsHistory);
        }
    }
    public LoungeRecruitmentState[] Recruitments { get { lock (_gate) return _recruitments.Values.OrderByDescending(r => r.MessageId).ToArray(); } }

    public event Action? Changed;
    public event Action<string, string>? Notification;
    public int UnreadCount { get { lock (_gate) return _inbox.Count; } }
    public int MentionCount { get { lock (_gate) return _inbox.Mentions; } }
    public bool NotificationsMuted => _settings.DoNotDisturb || (_settings.QuietInGame && _inGame);
    public string CurrentRegion => _presence?.Region ?? "";
    public string CurrentPlayerId => _presence is { } p ? LoungeRules.PlayerId(p) : "";
    public bool IsFollowed(string id) => (_settings.FollowedMembers ?? []).Contains(id);
    public void SetReading(bool reading)
    {
        bool changed;
        lock (_gate) { changed = reading && _inbox.Count > 0; _inbox.Reading = reading; if (reading) _inbox.Clear(); }
        if (changed) Notify();
    }
    public void ToggleFollow(string id)
    {
        var followed = (_settings.FollowedMembers ?? []).ToHashSet();
        if (!followed.Remove(id)) followed.Add(id);
        var updated = _settings with { FollowedMembers = followed.Take(200).ToArray() };
        updated.Save(); _settings = updated; Notify();
    }
    public void SetPreferences(bool dnd, bool quiet, bool pentakills, bool legendary, bool promotions)
    {
        var updated = _settings with { DoNotDisturb = dnd, QuietInGame = quiet,
            AnnouncePentakills = pentakills, AnnounceLegendary = legendary, AnnouncePromotions = promotions };
        updated.Save(); _settings = updated; Notify();
    }
    public LoungeSettings Settings => _settings;
    public string Status { get { lock (_gate) return _status; } }
    public bool CanSend
    {
        get
        {
            var current = _presence;
            var published = _publishedPresence;
            return _hub?.State == HubConnectionState.Connected && current != null && published != null &&
                !string.IsNullOrEmpty(current.Puuid) && published.Puuid == current.Puuid && published.Platform == current.Platform;
        }
    }
    public LoungeMessage[] Messages { get { lock (_gate) return _messages.Values.ToArray(); } }
    public LoungeMember[] Members { get { lock (_gate) return _members.ToArray(); } }

    public void Start()
    {
        if (_worker != null || _disposed) return;
        try
        {
            _settings = _settings.EnsureToken();
            _ownSession = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(SettingSecretProtector.Unprotect(_settings.ProtectedToken))));
            _settings.Save();
            _stop = new();
            _worker = Task.Run(() => RunAsync(_stop.Token));
        }
        catch (Exception ex) { SetStatus("聊天室设置读取失败：" + ex.Message); }
    }

    public async Task ConfigureAsync(string url, bool shareActivity, bool showVictories)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        url = LoungeSettings.ValidateUrl(url);
        await _configuration.WaitAsync();
        try
        {
            var updated = (_settings with { ServerUrl = url, ShareActivity = shareActivity, ShowVictories = showVictories }).EnsureToken();
            updated.Save();
            bool serverChanged = updated.ServerUrl != _settings.ServerUrl;
            _settings = updated;
            _stop?.Cancel();
            if (_worker != null) await _worker;
            _stop?.Dispose();
            _worker = null;
            if (serverChanged)
            {
                lock (_gate) { _messages.Clear(); _onlineMessages.Clear(); _onlineNotice = null; _recruitments.Clear(); _members = []; _deleted.Clear(); _cursor = 0; _initialSync = false; _inbox.Clear(); }
            }
            Start();
        }
        finally { _configuration.Release(); }
    }

    public void BlockSender(string sender)
    {
        _settings = _settings with { BlockedSenders = (_settings.BlockedSenders ?? []).Append(sender).Distinct().ToArray() };
        _settings.Save();
        Notify();
    }

    public void ClearBlocks()
    {
        _settings = _settings with { BlockedSenders = [] };
        _settings.Save();
        Notify();
    }

    public async Task SendPostAsync(LoungeSend post, CancellationToken token = default)
    {
        var hub = _hub;
        if (!CanSend || hub == null) throw new InvalidOperationException("聊天室尚未连接或未登录 LOL。");
        LoungeContent.ValidateSend(post);
        if (post.Kind == "recruit" && !SupportsRecruitment)
            throw new InvalidOperationException("服务器尚未支持完整开黑招募，请更新服务器上的 LOL-GameApi 并重新连接（需要协议 v3）。");
        if (!SupportsRichMessages && (post.Kind != "chat" || (post.Mentions?.Length ?? 0) > 0))
            throw new InvalidOperationException("服务器版本过旧，仅支持普通文字。请更新服务器上的 LOL-GameApi 后重新连接，才能发送表情包、招募和提及。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var message = await hub.InvokeAsync<LoungeMessage>(post.Kind == "recruit" ? "PublishRecruitment" : "SendMessage", post, timeout.Token);
        Receive(message, false);
    }

    public async Task ChangeRecruitmentAsync(long id, string action, CancellationToken token = default)
    {
        var hub = _hub;
        if (!CanSend || hub == null) throw new InvalidOperationException("请先连接频道并登录 LOL。");
        if (!SupportsRecruitment) throw new InvalidOperationException("请更新服务器上的 LOL-GameApi 以启用完整开黑招募。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        ReceiveRecruitment(await hub.InvokeAsync<LoungeRecruitmentState>("ChangeRecruitment", id, action, timeout.Token));
    }

    public static string SendError(Exception error)
    {
        if (error is OperationCanceledException) return "未确认操作结果，保留当前内容后可重试；系统会避免重复发布。";
        if (error is Microsoft.AspNetCore.SignalR.HubException)
        {
            const string marker = "HubException:";
            int start = error.Message.LastIndexOf(marker, StringComparison.Ordinal);
            return start < 0 ? error.Message : error.Message[(start + marker.Length)..].Trim();
        }
        return error.Message;
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observer = ObserveLocalLoopAsync(lifetime.Token);
        try { await ConnectionLoopAsync(lifetime.Token); }
        finally
        {
            lifetime.Cancel();
            try { await observer; } catch (OperationCanceledException) { }
        }
    }

    private async Task ObserveLocalLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var state = await local.ReadAsync(_settings.ShareActivity, timeout.Token);
                bool wasInGame = _inGame;
                _inGame = state.Phase is "InProgress" or "GameStart" or "Reconnect";
                if (wasInGame && !_inGame && UnreadCount > 0)
                    RaiseNotification("对局结束 · 频道未读", $"有 {UnreadCount} 条未读消息，其中 {MentionCount} 条提到了你。");
                lock (_gate) _presence = state.Presence;
                ObservePromotion(state);
                await ObserveGameAsync(state, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                lock (_gate) _presence = null;
                RuntimeDiagnostics.Report("lol休息室", "LOL 状态读取失败", ex.GetType().Name);
            }
            Notify();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }

    private async Task ConnectionLoopAsync(CancellationToken token)
    {
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            HubConnection? hub = null;
            try
            {
                if (string.IsNullOrWhiteSpace(_settings.ServerUrl))
                { SetStatus("尚未配置服务器，请填写地址并保存连接"); return; }
                string server = LoungeSettings.ValidateUrl(_settings.ServerUrl);
                string credential = SettingSecretProtector.Unprotect(_settings.ProtectedToken);
                hub = new HubConnectionBuilder().WithUrl(server + "/hubs/lounge", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(credential);
                    options.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is HttpClientHandler http) http.AllowAutoRedirect = false;
                        return handler;
                    };
                }).Build();
                hub.ServerTimeout = TimeSpan.FromSeconds(45);
                hub.On<LoungeMessage>("Message", message => Receive(message, _initialSync));
                hub.On<LoungeOnlineNotice>("Online", ReceiveOnline);
                hub.On<LoungeRecruitmentState>("Recruitment", state =>
                {
                    LoungeRecruitmentState? previous;
                    lock (_gate) _recruitments.TryGetValue(state.MessageId, out previous);
                    ReceiveRecruitment(state);
                    if (previous != null && state.Revision > previous.Revision && state.OwnerId == CurrentPlayerId)
                    {
                        var arrived = state.Participants.Where(p => !previous.Participants.Any(old => old.Id == p.Id)).ToArray();
                        if (arrived.Length > 0) RaiseNotification("开黑招募有新报名", string.Join("、", arrived.Select(p => p.Name)) + " 已报名，可在招募列表查看。");
                    }
                });
                hub.On<LoungeMember[]>("Members", UpdateMembers);
                hub.On<long>("Deleted", DeleteLocal);
                _hub = hub;
                SetStatus(failures == 0 ? "正在连接聊天室…" : "正在重新连接聊天室…");
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await hub.StartAsync(connectTimeout.Token);
                }
                failures = 0;
                await SyncAsync(hub, token, latest: true);
                int ticks = 0;
                while (hub.State == HubConnectionState.Connected && !token.IsCancellationRequested)
                {
                    using var tickTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    tickTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                    LoungePresence presence;
                    lock (_gate) presence = _presence ?? new("", "未登录玩家", "", "未知大区", "状态未知", false);
                    await hub.InvokeAsync("UpdatePresence", presence, tickTimeout.Token);
                    _publishedPresence = presence;
                    SetStatus(string.IsNullOrEmpty(presence.Puuid) ? "已连接 · 登录 LOL 后可以发言" :
                        $"已连接 · {presence.Name} · {presence.Region} · {presence.Status}");
                    if (!SupportsRichMessages) SetStatus("已连接旧版服务器 · 仅支持文字，请更新 LOL-GameApi 以启用表情包、招募和提及");
                    await FlushVictoriesAsync(hub, presence, tickTimeout.Token);
                    await FlushHighlightsAsync(hub, presence, tickTimeout.Token);
                    if (++ticks % 6 == 0) await SyncAsync(hub, tickTimeout.Token);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Never log the remote URL or bearer credential.
                RuntimeDiagnostics.Report("lol休息室", "连接中断", ex is Microsoft.AspNetCore.SignalR.HubException ? ex.Message : ex.GetType().Name);
                SetStatus(ex is Microsoft.AspNetCore.SignalR.HubException ? ex.Message : "聊天室连接失败，正在自动重试…");
            }
            finally
            {
                _hub = null;
                _publishedPresence = null;
                _protocolVersion = 1;
                lock (_gate) { _members = []; _memberBaseline = false; _onlineNotice = null; }
                if (hub != null)
                {
                    try { await hub.DisposeAsync(); }
                    catch (Exception ex) { RuntimeDiagnostics.Report("lol休息室", "连接清理", ex.GetType().Name); }
                }
                Notify();
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << Math.Min(failures++, 4))), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SyncAsync(HubConnection hub, CancellationToken token, bool latest = false)
    {
        // Cursor advances only through ordered replay, never through live out-of-order broadcasts.
        long after = latest ? 0 : _initialSync ? _cursor : 0;
        while (true)
        {
            var snapshot = await hub.InvokeAsync<LoungeSnapshot>("Sync", after, token);
            _protocolVersion = snapshot.ProtocolVersion;
            foreach (long id in snapshot.DeletedIds ?? []) DeleteLocal(id);
            foreach (var message in snapshot.Messages) Receive(message, _initialSync);
            foreach (var state in snapshot.Recruitments ?? []) ReceiveRecruitment(state);
            UpdateMembers(snapshot.Members);
            if (snapshot.Messages.Length > 0) after = _cursor = snapshot.Messages[^1].Id;
            _initialSync = true;
            Notify();
            if (snapshot.Messages.Length < 200) break;
        }
    }

    private void ReceiveRecruitment(LoungeRecruitmentState state)
    {
        lock (_gate)
        {
            if (_deleted.Contains(state.MessageId)) return;
            if (_recruitments.TryGetValue(state.MessageId, out var old) && old.Revision >= state.Revision) return;
            _recruitments[state.MessageId] = state;
            while (_recruitments.Count > 300) _recruitments.Remove(_recruitments.Keys.Min());
        }
        Notify();
    }

    private void Receive(LoungeMessage message, bool notify)
    {
        // Presence has a separate bounded history and never enters chat or unread counts.
        if (message.Kind == "online")
        {
            lock (_gate)
            {
                if (_onlineMessages.Any(m => m.Id == message.Id)) return;
                RecordOnline(message);
            }
            Notify();
            return;
        }
        bool mentioned;
        lock (_gate)
        {
            bool known = _messages.ContainsKey(message.Id);
            if (_deleted.Contains(message.Id)) message = message with { Kind = "deleted", Text = "此消息已被管理员删除" };
            _messages[message.Id] = message;
            mentioned = _inbox.Accept(message, CurrentPlayerId, _ownSession,
                (_settings.BlockedSenders ?? []).Contains(message.SenderId), notify && !known);
            while (_messages.Count > 300) _messages.Remove(_messages.Keys.First());
            if (_deleted.Count > 1000) _deleted.RemoveWhere(id => id < _messages.Keys.First());
        }
        if (mentioned) RaiseNotification("有人 @ 你", message.Name + "：" + LoungeContent.Display(message));
        Notify();
    }

    private void ReceiveOnline(LoungeOnlineNotice notice)
    {
        lock (_gate)
        {
            _onlineNotice = notice;
            _onlineNoticeUntil = DateTimeOffset.UtcNow.AddSeconds(6);
            RecordOnline(new(--_onlineSequence, "", "", notice.Name, notice.Region, "online", "", DateTimeOffset.UtcNow));
        }
        Notify();
    }

    // Called under _gate; local presence IDs never advance the server history cursor.
    private void RecordOnline(LoungeMessage message)
    {
        _onlineMessages.Enqueue(message);
        while (_onlineMessages.Count > 200) _onlineMessages.Dequeue();
    }

    private void UpdateMembers(LoungeMember[] members)
    {
        LoungeMember[] arrived;
        lock (_gate)
        {
            arrived = _initialSync && _memberBaseline ? members.Where(m => IsFollowed(m.Id) && m.Id != CurrentPlayerId &&
                !_members.Any(old => old.Id == m.Id)).ToArray() : [];
            _members = members;
            _memberBaseline = true;
        }
        foreach (var member in arrived) RaiseNotification("关注的好友上线", $"{member.Name} · {member.Region} · {member.Rank}");
        Notify();
    }

    private void RaiseNotification(string title, string text)
    {
        if (!NotificationsMuted) Notification?.Invoke(title, text);
    }

    private void DeleteLocal(long id)
    {
        lock (_gate)
        {
            _deleted.Add(id);
            _recruitments.Remove(id);
            if (_messages.TryGetValue(id, out var message))
                _messages[id] = message with { Kind = "deleted", Text = "此消息已被管理员删除" };
        }
        Notify();
    }

    private async Task ObserveGameAsync(LoungeLocalState state, CancellationToken token)
    {
        if (!state.Presence.ShareActivity || string.IsNullOrWhiteSpace(state.Presence.Puuid))
        { _activeGame = null; _finishedAt = null; _promotionGame = null;
            lock (_gate) { _pendingVictories.Clear(); _pendingHighlights.Clear(); } return; }
        if (_activeGame != null && _activeGame.Presence.Puuid != state.Presence.Puuid)
        { _activeGame = null; _finishedAt = null; }
        if (state.Phase is "InProgress" or "Reconnect" && state.GameId > 0 && !state.Custom)
        {
            _activeGame = state with { PartyPuuids = _activeGame?.GameId == state.GameId &&
                (_activeGame.PartyPuuids?.Length ?? 0) > 0 ? _activeGame.PartyPuuids : state.PartyPuuids,
                RankScores = _activeGame?.GameId == state.GameId ? _activeGame.RankScores : state.RankScores };
            _finishedAt = null;
            return;
        }
        if (_activeGame is not { } active || state.Phase is "" or "GameStart") return;
        _finishedAt ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - _finishedAt > TimeSpan.FromMinutes(3)) { _activeGame = null; return; }
        if (!LoungeRules.Modes.ContainsKey(active.QueueId) || string.IsNullOrWhiteSpace(active.Presence.Platform))
        { _activeGame = null; return; }
        var detail = await local.MatchAsync(active.GameId, token);
        bool? won = LoungeLcuReader.ReadVictory(detail, active.GameId, active.Presence.Puuid);
        if (won is null) return;
        foreach (string kind in LoungeLcuReader.ReadHighlights(detail, active.GameId, active.Presence.Puuid))
            QueueHighlight(active, kind, "", _finishedAt.Value);
        if (active.QueueId is 420 or 440 && _promotionGame?.GameId != active.GameId)
        { _promotionGame = active; _promotionDeadline = DateTimeOffset.UtcNow.AddMinutes(3); }
        if (won == true)
        {
            string key = active.Presence.Platform + ":" + active.Presence.Puuid + ":" + active.GameId;
            if (!_reported.Contains(key))
            {
                string[] partyNames = LoungeLcuReader.ReadPartyNames(detail, active.GameId, active.Presence.Puuid,
                    active.PartyPuuids ?? []);
                lock (_gate) _pendingVictories[key] = new LoungeVictory(active.GameId, active.Presence.Platform,
                    active.Presence.Puuid, active.QueueId, _finishedAt.Value, partyNames);
                _reported.Add(key);
                if (_reported.Count > 1000) _reported.Clear();
            }
        }
        _activeGame = null;
        _finishedAt = null;
    }

    private bool HighlightEnabled(string kind) => kind switch
    {
        "pentakill" => _settings.AnnouncePentakills,
        "legendary" => _settings.AnnounceLegendary,
        "promotion" => _settings.AnnouncePromotions,
        _ => false
    };

    private void QueueHighlight(LoungeLocalState active, string kind, string detail, DateTimeOffset finished)
    {
        if (!HighlightEnabled(kind)) return;
        string key = $"highlight:{active.Presence.Platform}:{active.Presence.Puuid}:{active.GameId}:{kind}";
        if (!_reported.Add(key)) return;
        lock (_gate) _pendingHighlights[key] = new(active.GameId, active.Presence.Platform, active.Presence.Puuid,
            active.QueueId, finished, kind, detail);
    }

    private void ObservePromotion(LoungeLocalState state)
    {
        if (_promotionGame is not { } active) return;
        if (!state.Presence.ShareActivity || state.Presence.Puuid != active.Presence.Puuid ||
            state.Presence.Platform != active.Presence.Platform || DateTimeOffset.UtcNow > _promotionDeadline)
        { _promotionGame = null; return; }
        string queue = active.QueueId == 420 ? "RANKED_SOLO_5x5" : "RANKED_FLEX_SR";
        if (active.RankScores?.TryGetValue(queue, out int before) == true &&
            state.RankScores?.TryGetValue(queue, out int after) == true && after > before)
        {
            QueueHighlight(active, "promotion", state.Presence.Rank, DateTimeOffset.UtcNow);
            _promotionGame = null;
        }
    }

    private async Task FlushHighlightsAsync(HubConnection hub, LoungePresence presence, CancellationToken token)
    {
        KeyValuePair<string, LoungeHighlight>[] pending;
        lock (_gate) pending = _pendingHighlights.ToArray();
        foreach (var entry in pending)
        {
            var report = entry.Value;
            if (presence.ShareActivity && report.Puuid == presence.Puuid && report.Platform == presence.Platform &&
                HighlightEnabled(report.Kind) && LoungeRules.IsRecent(report.FinishedAt, DateTimeOffset.UtcNow))
            {
                if (!SupportsRichMessages) continue;
                await hub.InvokeAsync("ReportHighlight", report, token);
            }
            lock (_gate) _pendingHighlights.Remove(entry.Key);
        }
    }

    private async Task FlushVictoriesAsync(HubConnection hub, LoungePresence presence, CancellationToken token)
    {
        KeyValuePair<string, LoungeVictory>[] pending;
        lock (_gate) pending = _pendingVictories.ToArray();
        foreach (var entry in pending)
        {
            if (!presence.ShareActivity || entry.Value.Puuid != presence.Puuid || entry.Value.Platform != presence.Platform ||
                !LoungeRules.IsRecent(entry.Value.FinishedAt, DateTimeOffset.UtcNow))
            { lock (_gate) _pendingVictories.Remove(entry.Key); continue; }
            await hub.InvokeAsync("ReportVictory", entry.Value, token);
            lock (_gate) _pendingVictories.Remove(entry.Key);
        }
    }

    private void SetStatus(string value) { lock (_gate) _status = value; Notify(); }
    private void Notify() { try { Changed?.Invoke(); } catch (ObjectDisposedException) { } }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop?.Cancel();
        Changed = null;
        Notification = null;
        // Do not block the UI thread waiting for network shutdown.
        if (_worker != null) _ = _worker.ContinueWith(_ => _stop?.Dispose(), TaskScheduler.Default);
        else _stop?.Dispose();
    }
}
