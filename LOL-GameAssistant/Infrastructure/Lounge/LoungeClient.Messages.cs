using LOL.Lounge;
using Microsoft.AspNetCore.SignalR.Client;

namespace LOL_GameAssistant.Infrastructure.Lounge;

public sealed partial class LoungeClient
{
    private readonly SemaphoreSlim _messageWake = new(0, 1);
    private volatile bool _messagesActive;
    private long _messageEpoch;
    private DateTimeOffset? _loadedMessageDay;
    private long? _historyBefore;
    private bool? _initialHasOlder;
    public bool MessagesActive => _messagesActive;
    public long MessageEpoch => Interlocked.Read(ref _messageEpoch);
    public long? HistoryBefore { get { lock (_gate) return _historyBefore; } }
    public bool? InitialHasOlder { get { lock (_gate) return _initialHasOlder; } }

    public void SetMessagesActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _messagesActive == active) return;
            _messagesActive = active;
            ResetMessageWindow();
            _inbox.Reading = false;
        }
        try { _messageWake.Release(); } catch (SemaphoreFullException) { }
        Notify();
    }

    private void ResetMessageWindow()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _messageEpoch);
            _messages.Clear(); _recruitments.Clear(); _onlineMessages.Clear(); _deleted.Clear();
            _onlineNotice = null; _cursor = 0; _initialSync = false;
            _loadedMessageDay = null; _historyBefore = null; _initialHasOlder = null; _inbox.Clear();
        }
    }

    private void ReceiveLiveMessage(LoungeMessage message)
    {
        lock (_gate)
        {
            var start = LoungeRules.StartOfDay(DateTimeOffset.Now);
            if (!MessagesActive || message.SentAt < start || message.SentAt >= start.AddDays(1)) return;
            Receive(message, _initialSync);
        }
    }

    private async Task<bool> UpdateMessageSubscriptionAsync(HubConnection hub, bool subscribed, CancellationToken token)
    {
        if (!MessagesActive)
        {
            if (subscribed) await hub.InvokeAsync("PauseMessages", token);
            return false;
        }
        if (_protocolVersion < 6)
        {
            SetStatus("已连接 · 请更新 LOL-GameApi 服务器以启用按需加载消息");
            return false;
        }
        var day = LoungeRules.StartOfDay(DateTimeOffset.Now);
        lock (_gate)
            if (_loadedMessageDay.HasValue && _loadedMessageDay != day) ResetMessageWindow();
        if (_initialSync) return true;
        long epoch = MessageEpoch;
        var snapshot = await hub.InvokeAsync<LoungeSnapshot>("OpenMessages", day, token);
        ApplyTodaySnapshot(snapshot, day, epoch);
        return true;
    }

    internal void ApplyTodaySnapshot(LoungeSnapshot snapshot, DateTimeOffset day, long epoch)
    {
        lock (_gate)
        {
            if (!MessagesActive || epoch != MessageEpoch) return;
            var messages = snapshot.Messages.Where(m => m.SentAt >= day && m.SentAt < day.AddDays(1)).OrderBy(m => m.Id).ToArray();
            foreach (long id in snapshot.DeletedIds ?? []) DeleteLocal(id);
            foreach (var message in messages) Receive(message, false);
            foreach (var state in snapshot.Recruitments ?? []) ReceiveRecruitment(state);
            UpdateMembers(snapshot.Members);
            _protocolVersion = snapshot.ProtocolVersion;
            _cursor = messages.LastOrDefault()?.Id ?? LoungeRules.BeforeFirstMessage;
            _loadedMessageDay = day; _initialSync = true;
            _historyBefore = snapshot.OlderBefore ?? messages.FirstOrDefault()?.Id ?? long.MaxValue;
            _initialHasOlder = snapshot.HasOlder;
        }
        Notify();
    }
}
