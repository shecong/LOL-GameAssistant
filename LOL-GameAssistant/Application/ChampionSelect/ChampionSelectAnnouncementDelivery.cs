namespace LOL_GameAssistant.Application.ChampionSelect;

/// <summary>从进入选人开始计时三秒；刷新与数据回调不重置截止时间，临时失败后重试。</summary>
internal sealed class ChampionSelectAnnouncementDelivery(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly long _enteredAt = (timeProvider ?? TimeProvider.System).GetTimestamp();
    internal TimeSpan RemainingInitialDelay
    {
        get
        {
            var remaining = TimeSpan.FromSeconds(3) - _clock.GetElapsedTime(_enteredAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
    public bool IsSent { get; private set; }
    public bool IsSending { get; private set; }

    public async Task SendAsync(Func<CancellationToken, Task<bool>> attempt, Func<bool> isCurrent,
        CancellationToken token, TimeSpan? retryDelay = null, TimeSpan? initialDelay = null)
    {
        if (IsSent || IsSending || token.IsCancellationRequested || !isCurrent()) return;
        IsSending = true;
        try
        {
            // 计时起点是阶段进入，不是数据就绪；迟到的触发立即尝试，不再额外等三秒。
            await Task.Delay(initialDelay ?? RemainingInitialDelay, _clock, token);
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
