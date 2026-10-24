using LOL.Lounge;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LOL_GameApi.Lounge;

public sealed class LoungeRoom(IOptionsMonitor<LoungeOptions> settings, TimeProvider? clock = null,
    ILogger<LoungeRoom>? logger = null)
{
    private sealed record Connection(string Session, string Address, LoungePresence Presence,
        DateTimeOffset Seen, Action Abort, bool Closing = false);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, Connection> _connections = [];
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _rates = [];
    private readonly Dictionary<string, DateTimeOffset> _announced = [];

    public bool ShouldAnnounceOnline(string connection)
    {
        lock (_gate)
        {
            var value = Get(connection);
            if (!value.ShareActivity || string.IsNullOrWhiteSpace(value.Puuid)) return false;
            string key = LoungeRules.PlayerId(value);
            var now = _clock.GetUtcNow();
            if (_connections.Any(c => c.Key != connection && !c.Value.Closing && c.Value.Seen >= now.AddSeconds(-45) &&
                LoungeRules.PlayerId(c.Value.Presence) == key)) return false;
            if (_announced.TryGetValue(key, out var last) && last > now.AddMinutes(-10)) return false;
            _announced[key] = now;
            foreach (var stale in _announced.Where(p => p.Value < now.AddDays(-1)).Select(p => p.Key).ToArray())
                _announced.Remove(stale);
            return true;
        }
    }

    public void Connect(string connection, string session, string address, Action abort)
    {
        ArgumentNullException.ThrowIfNull(abort);
        lock (_gate)
        {
            if (_connections.Count >= settings.CurrentValue.MaxConnections ||
                _connections.Values.Count(c => c.Session == session) >= 3 ||
                _connections.Values.Count(c => c.Address == address) >= settings.CurrentValue.MaxConnectionsPerIp)
                throw new HubException("聊天室连接数已达上限，请稍后重试。");
            _connections[connection] = new(session, address, new("", "未登录玩家", "", "未知大区", "未登录 LOL"), _clock.GetUtcNow(), abort);
        }
    }

    public bool Update(string connection, LoungePresence value)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connection, out var old) || old.Closing) throw new HubException("请重新连接聊天室。");
            value = new(LoungeRules.Clean(value.Puuid, 128), LoungeRules.Clean(value.Name, 80),
                LoungeRules.Clean(value.Platform, 40), LoungeRules.Clean(value.Region, 40),
                LoungeRules.Clean(value.Status, 30), value.ShareActivity, LoungeRules.Clean(value.Rank, 80));
            if (string.IsNullOrWhiteSpace(value.Name)) value = value with { Name = "未登录玩家" };
            if (!value.ShareActivity) value = value with { Status = "活动已隐藏" };
            _connections[connection] = old with { Presence = value, Seen = _clock.GetUtcNow() };
            return old.Presence != value;
        }
    }

    public LoungePresence Get(string connection, bool allowStale = false)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connection, out var entry) || entry.Closing || (!allowStale && entry.Seen < _clock.GetUtcNow().AddSeconds(-45)))
                throw new HubException("状态已过期，请等待重新同步。");
            return entry.Presence;
        }
    }

    public LoungeMember[] Members()
    {
        lock (_gate)
        {
            return _connections.Values.Where(c => !c.Closing && c.Seen >= _clock.GetUtcNow().AddSeconds(-45))
                .GroupBy(c => string.IsNullOrEmpty(c.Presence.Puuid) ? c.Session : LoungeRules.PlayerId(c.Presence))
                .Select(g =>
                {
                    var latest = g.OrderByDescending(c => c.Seen).First().Presence;
                    return new LoungeMember(g.Key, latest.Name, latest.Region, latest.Status, latest.Rank);
                }).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
        }
    }

    public void Remove(string connection) { lock (_gate) _connections.Remove(connection); }

    public string[] StaleConnections()
    {
        KeyValuePair<string, Connection>[] stale;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            stale = _connections.Where(c => c.Value.Closing || c.Value.Seen < now.AddSeconds(-45)).ToArray();
            foreach (var entry in stale) _connections[entry.Key] = entry.Value with { Closing = true };
            foreach (var key in _rates.Where(r => r.Value.Start < now.AddMinutes(-5)).Select(r => r.Key).ToArray())
                _rates.Remove(key);
        }
        // Abort outside the lock; OnDisconnectedAsync alone releases the transport's quota.
        foreach (var entry in stale)
        {
            try { entry.Value.Abort(); }
            catch (Exception ex) { logger?.LogWarning(ex, "Failed to abort stale lounge connection {Connection}", entry.Key); }
        }
        return stale.Select(entry => entry.Key).ToArray();
    }

    public void Check(string session, string action, int limit, TimeSpan window, bool writing = false)
    {
        lock (_gate)
        {
            if (settings.CurrentValue.BannedSessions.Contains(session)) throw new HubException("此会话已被封禁。");
            if (writing && settings.CurrentValue.MutedSessions.Contains(session)) throw new HubException("此会话已被禁言。");
            string key = session + ":" + action;
            var now = _clock.GetUtcNow();
            var current = _rates.GetValueOrDefault(key);
            if (now - current.Start >= window) current = (now, 0);
            if (current.Count >= limit) throw new HubException("操作过于频繁，请稍后重试。");
            _rates[key] = (current.Start, current.Count + 1);
        }
    }
}

public sealed class LoungePresenceReaper(LoungeRoom room, IHubContext<LoungeHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            if (room.StaleConnections().Length > 0)
                await hub.Clients.All.SendAsync("Members", room.Members(), stoppingToken);
    }
}
