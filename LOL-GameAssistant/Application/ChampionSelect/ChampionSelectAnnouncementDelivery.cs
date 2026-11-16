namespace LOL_GameAssistant.Application.ChampionSelect;

/// <summary>战绩齐备后等待四秒；并发回调不重复计时，临时失败后重试。</summary>
internal sealed class ChampionSelectAnnouncementDelivery(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public bool IsSent { get; private set; }
    public bool IsSending { get; private set; }

    public async Task SendAsync(Func<CancellationToken, Task<bool>> attempt, Func<bool> isCurrent,
        CancellationToken token, TimeSpan? retryDelay = null, TimeSpan? initialDelay = null)
    {
        if (IsSent || IsSending || token.IsCancellationRequested || !isCurrent()) return;
        IsSending = true;
        try
        {
            // 调用方确认战绩齐备后才启动，等待期间的重复调用不会重置计时。
            await Task.Delay(initialDelay ?? TimeSpan.FromSeconds(4), _clock, token);
            while (!token.IsCancellationRequested && isCurrent())
            {
                if (await attempt(token))
                {
                    IsSent = true;
                    return;
                }
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(3), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { IsSending = false; }
    }
}
