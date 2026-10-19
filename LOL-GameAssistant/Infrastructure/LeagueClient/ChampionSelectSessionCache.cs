using LOL_GameAssistant.Entity;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>选人事件与轮询共用快照；旧读取不覆盖新事件，取消等待不取消其他消费者。</summary>
internal sealed class ChampionSelectSessionCache
{
    internal static ChampionSelectSessionCache Shared { get; } = new();
    private readonly object _sync = new();
    private ChampSelectSession? _value;
    private DateTimeOffset _updated;
    private bool _fromEvent;
    private long _revision;
    private Task<ChampSelectSession?>? _pending;
    internal long Revision { get { lock (_sync) return _revision; } }
    internal void Clear() { lock (_sync) { _revision++; _value = null; _pending = null; _updated = default; } }
    internal void Observe(string data, string eventType)
    {
        if (eventType.Equals("Delete", StringComparison.OrdinalIgnoreCase) || data == "null") { Clear(); return; }
        try
        {
            var parsed = JsonConvert.DeserializeObject<ChampSelectSession>(data); if (parsed == null) return;
            lock (_sync) { _revision++; _value = parsed; _updated = DateTimeOffset.UtcNow; _fromEvent = true; }
        }
        catch (JsonException) { }
    }
    internal Task<ChampSelectSession?> GetAsync(Func<CancellationToken, Task<ChampSelectSession?>> load, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Task<ChampSelectSession?> request;
        lock (_sync)
        {
            if (_value != null && DateTimeOffset.UtcNow - _updated < TimeSpan.FromMilliseconds(_fromEvent ? 1500 : 250)) return Task.FromResult<ChampSelectSession?>(_value);
            if (_pending is { IsCompleted: false }) request = _pending;
            else { long revision = _revision; request = _pending = Read(load, revision); }
        }
        return request.WaitAsync(token);
    }
    private async Task<ChampSelectSession?> Read(Func<CancellationToken, Task<ChampSelectSession?>> load, long revision)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var loaded = await load(deadline.Token).ConfigureAwait(false);
        lock (_sync)
        {
            if (revision != _revision) return _value;
            _value = loaded; _updated = DateTimeOffset.UtcNow; _fromEvent = false; return _value;
        }
    }
    internal void MarkAction(long actionId, int championId, bool completed)
    {
        lock (_sync)
        {
            var action = _value?.Actions.SelectMany(x => x).FirstOrDefault(x => x.Id == actionId);
            if (action != null) { action.ChampionId = championId; action.Completed = completed; _revision++; }
        }
    }
}
