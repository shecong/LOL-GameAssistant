namespace LOL_GameAssistant.Application.ChampionSelect;

/// <summary>名单就绪后延迟三秒发送；同一名单只允许一个任务，临时失败后重试。</summary>
internal sealed class ChampionSelectAnnouncementDelivery
{
    public bool IsSent { get; private set; }
    public bool IsSending { get; private set; }

    public async Task SendAsync(Func<CancellationToken, Task<bool>> attempt, Func<bool> isCurrent,
        CancellationToken token, TimeSpan? retryDelay = null, TimeSpan? initialDelay = null)
    {
        if (IsSent || IsSending || token.IsCancellationRequested || !isCurrent()) return;
        IsSending = true;
        try
        {
            // 留出队友进入选人聊天的时间；等待期间名单失效或退出选人时不再发送。
            await Task.Delay(initialDelay ?? TimeSpan.FromSeconds(3), token);
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
