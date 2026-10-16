using LOL_GameAssistant.Application.LeagueClient;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>战绩列表共享限流；单次预算从取得槽位后开始，临时故障仅重试一次。</summary>
internal sealed class MatchHistoryRequestPolicy
{
    internal static MatchHistoryRequestPolicy Shared { get; } = new();
    private readonly SemaphoreSlim _gate;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<CancellationToken, Task> _backoff;

    internal MatchHistoryRequestPolicy(int concurrency = 3, TimeSpan? requestTimeout = null,
        Func<CancellationToken, Task>? backoff = null)
    {
        _gate = new(concurrency, concurrency);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(12);
        _backoff = backoff ?? (token => Task.Delay(TimeSpan.FromMilliseconds(350), token));
    }

    internal async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            await _gate.WaitAsync(token).ConfigureAwait(false);
            bool retry = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(_requestTimeout);
                try
                {
                    var result = await request(timeout.Token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
                {
                    if (attempt != 0) throw new LcuRequestException(LcuFailure.Timeout, null);
                    retry = true;
                }
                catch (HttpRequestException error) when (!token.IsCancellationRequested && attempt == 0 && IsTransient(error))
                {
                    retry = true;
                }
            }
            finally { _gate.Release(); }
            // Release capacity while backing off so other players can load their lists.
            if (retry) await _backoff(token).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpRequestException error)
    {
        if (error is LcuRequestException lcu && lcu.Failure != LcuFailure.HttpStatus)
            return lcu.Failure is LcuFailure.Timeout or LcuFailure.Network;
        return error.StatusCode == null || (int)error.StatusCode == 429 || (int)error.StatusCode >= 500;
    }
}
