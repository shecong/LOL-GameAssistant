using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>所有窗口共用摘要、详情及在途请求；等待者取消不取消共享网络请求。</summary>
public sealed class PlayerHistoryRepository
{
    private static readonly ConditionalWeakTable<IMatchHistoryService, PlayerHistoryRepository> Instances = new();
    public static PlayerHistoryRepository For(IMatchHistoryService source) => Instances.GetValue(source, value => new(value));
    public static void ClearAll() { foreach (var instance in Instances) instance.Value.Clear(); }
    private readonly IMatchHistoryService _source;
    private readonly TimeProvider _clock;
    private readonly Func<string> _context;
    private readonly ConcurrentDictionary<(string Context, string Puuid), PlayerHistorySnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<(string Context, string Puuid), Lazy<Task<PlayerHistorySnapshot>>> _batches = new();
    private readonly ConcurrentDictionary<(string Context, long Id), Lazy<Task<MatchDetail?>>> _details = new();
    private readonly SemaphoreSlim _detailGate = new(MatchDataPolicy.DetailConcurrency);
    private long _generation;
    public PlayerHistoryRepository(IMatchHistoryService source, TimeProvider? clock = null, Func<string>? context = null)
    { _source = source; _clock = clock ?? TimeProvider.System; _context = context ?? (() => PlayerHistorySession.Key); }
    public event Action<string>? Diagnostic;
    public int AvailableDetailSlots => _detailGate.CurrentCount;

    public PlayerHistorySnapshot? Peek(string puuid) => _snapshots.GetValueOrDefault((_context(), puuid));
    public async Task<PlayerHistorySnapshot> GetBatchAsync(string puuid, CancellationToken token = default, bool refresh = false)
    {
        token.ThrowIfCancellationRequested();
        var key = (_context(), puuid);
        if (!refresh && _snapshots.TryGetValue(key, out var previous) && !previous.IsStale &&
            _clock.GetUtcNow() - previous.AcquiredAt < MatchDataPolicy.Freshness) return previous;
        Lazy<Task<PlayerHistorySnapshot>>? candidate = null;
        long generation = Volatile.Read(ref _generation);
        candidate = new(() => ReadBatchAsync(key, generation, candidate!));
        return await _batches.GetOrAdd(key, candidate).Value.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<PlayerHistorySnapshot> ReadBatchAsync((string Context, string Puuid) key, long generation,
        Lazy<Task<PlayerHistorySnapshot>> request)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(MatchDataPolicy.LoadTimeout);
        PlayerHistorySnapshot result;
        try
        {
            if (_source is IPlayerHistorySnapshotSource snapshots)
                result = await snapshots.ReadSnapshotAsync(key.Puuid, deadline.Token).ConfigureAwait(false);
            else
            {
                var data = await _source.GetPageAsync(key.Puuid, 0, MatchDataPolicy.SummaryLimit - 1, deadline.Token)
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                result = new(key.Puuid, data, _clock.GetUtcNow(), data?.Games == null ? HistoryDataState.Unavailable : HistoryDataState.Ready);
            }
            if (!result.Available) throw new IOException(result.Error ?? "战绩摘要未返回有效数据");
            if (generation == Volatile.Read(ref _generation))
            {
                if (_snapshots.Count >= 128) _snapshots.Clear();
                _snapshots[key] = result;
            }
            Diagnostic?.Invoke($"摘要 {result.State} · 返回 {result.ReturnedCount}/{result.RequestedCount} · {timer.ElapsedMilliseconds}ms");
            return result;
        }
        catch (Exception error)
        {
            Diagnostic?.Invoke($"摘要 {error.GetType().Name} · {timer.ElapsedMilliseconds}ms");
            return _snapshots.TryGetValue(key, out var previous)
                ? previous with { State = HistoryDataState.Cached, Error = error.Message }
                : new(key.Puuid, null, _clock.GetUtcNow(), HistoryDataState.Unavailable, error.Message);
        }
        finally { _batches.TryRemove(new KeyValuePair<(string, string), Lazy<Task<PlayerHistorySnapshot>>>(key, request)); }
    }

    public async Task<MatchDetail?> GetDetailAsync(long id, CancellationToken token = default, TimeSpan? requestTimeout = null)
    {
        token.ThrowIfCancellationRequested();
        var key = (_context(), id);
        Lazy<Task<MatchDetail?>>? candidate = null;
        candidate = new(() => ReadDetailAsync(key, candidate!, requestTimeout ?? MatchDataPolicy.DetailTimeout));
        return await _details.GetOrAdd(key, candidate).Value.WaitAsync(token).ConfigureAwait(false);
    }
    private async Task<MatchDetail?> ReadDetailAsync((string Context, long Id) key, Lazy<Task<MatchDetail?>> request, TimeSpan timeout)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        bool acquired = false;
        try
        {
            using var total = new CancellationTokenSource(MatchDataPolicy.LoadTimeout);
            await _detailGate.WaitAsync(total.Token).ConfigureAwait(false);
            acquired = true;
            long queued = elapsed.ElapsedMilliseconds;
            using var network = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            network.CancelAfter(timeout);
            var result = await _source.GetDetailAsync(key.Id, cancellationToken: network.Token).WaitAsync(network.Token).ConfigureAwait(false);
            Diagnostic?.Invoke($"详情 {(result == null ? "空响应" : "完成")} · 排队 {queued}ms · 请求 {elapsed.ElapsedMilliseconds - queued}ms");
            return result;
        }
        finally
        {
            if (acquired) _detailGate.Release();
            _details.TryRemove(new KeyValuePair<(string, long), Lazy<Task<MatchDetail?>>>(key, request));
        }
    }
    public (int Entries, long Bytes) Measure() => (_snapshots.Count, _snapshots.Values.Sum(value =>
        (long)System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value).Length));
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _snapshots.Clear(); _batches.Clear(); _details.Clear();
    }
}
