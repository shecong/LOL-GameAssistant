namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Shares one bounded credential lookup; a stuck OS probe cannot consume new workers on every retry.</summary>
internal sealed class LcuCredentialProbe(Func<(string? Port, string? Token)> scan,
    TimeSpan? timeout = null, TimeSpan? retryDelay = null, Action? timedOut = null)
{
    private readonly object _gate = new();
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(3);
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    private Task<(string? Port, string? Token)>? _pending;
    private long _started;

    internal async Task<(string? Port, string? Token)> GetAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Task<(string? Port, string? Token)> pending;
        lock (_gate)
        {
            if (_pending == null || _pending.IsCompleted && Environment.TickCount64 - _started >= _retryDelay.TotalMilliseconds)
            {
                _started = Environment.TickCount64;
                // WMI is synchronous and may ignore cancellation; isolate it from the shared thread pool.
                _pending = Task.Factory.StartNew(scan, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            pending = _pending;
        }
        try { return await pending.WaitAsync(_timeout, token).ConfigureAwait(false); }
        catch (TimeoutException) { timedOut?.Invoke(); return (null, null); }
    }
}
