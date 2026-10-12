namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>唯一的连接探测循环；断线后重新发现凭据，停止时取消进行中的连接。</summary>
internal sealed class LeagueClientConnectionMonitor : IDisposable, IAsyncDisposable
{
    private readonly ILeagueClientEventStream _stream;
    private readonly Action<Exception> _reportError;
    private readonly TimeSpan _retryDelay;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private Task? _runTask;
    private volatile bool _connected;
    private bool _disposed;

    internal LeagueClientConnectionMonitor(ILeagueClientEventStream stream, Action<Exception> reportError,
        TimeSpan? retryDelay = null)
    {
        _stream = stream;
        _reportError = reportError;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(10);
        if (_retryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));
        _stream.ConnectionChanged += ConnectionChanged;
    }

    internal void Start()
    {
        lock (_sync)
        {
            if (_disposed || _runTask != null) return;
            _runTask = Task.Run(() => RunAsync(_lifetime.Token));
        }
    }

    private void ConnectionChanged(bool connected) => _connected = connected;

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_connected)
                        await _stream.ConnectAsync(forceRefresh: true, cancellationToken: token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { _reportError(ex); }
                await Task.Delay(_retryDelay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            // 与 Dispose 中的 Cancel 串行，避免后台退出时并发释放同一个 CTS。
            lock (_sync) _lifetime.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _stream.ConnectionChanged -= ConnectionChanged;
            _lifetime.Cancel();
            if (_runTask == null) _lifetime.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task? task;
        lock (_sync) task = _runTask;
        if (task != null) await task.ConfigureAwait(false);
    }
}
