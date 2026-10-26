using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Application.Skins;

public sealed record RankBorderLoadingUpdate(string Status, string Message);

/// <summary>Attempt once per game; only confirmed pre-write loading preconditions may be polled again.</summary>
public sealed class RankBorderLoadingCoordinator(ISkinCoreService core, Func<AssistantSettings> settings,
    Func<int?> gameProcess, TimeProvider? clock = null) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private int _pid, _started, _disposed;
    private bool _finished;
    private DateTimeOffset _firstSeen;
    private string _lastStatus = "";
    public event Action<RankBorderLoadingUpdate>? Changed;

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(LoopAsync);
    }
    private async Task LoopAsync()
    {
        var token = _lifetime.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await TickAsync(gameProcess(), settings(), token).ConfigureAwait(false);
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { Publish("已停止", "加载边框后台任务异常，已停止自动操作；请重新读取状态。"); }
        finally { if (Volatile.Read(ref _disposed) != 0) _lifetime.Dispose(); }
    }
    internal async Task TickAsync(int? pid, AssistantSettings config, CancellationToken token = default)
    {
        if (_pid != (pid ?? 0))
        {
            _pid = pid ?? 0;_finished = false;_firstSeen = _clock.GetUtcNow();_lastStatus = "";
        }
        if (_pid == 0 || _finished || !config.SkinCoreEnabled || !config.RankBorderAutoApplyEnabled) return;
        if (!RankBorderPresets.IsValid(config.PreferredRankBorderId) || config.PreferredRankBorderProfile != RankBorderPresets.Profile)
        { _finished = true;Publish("需重新设置", "保存的边框或适配版本已变化，请重新选择加载边框。");return; }
        if (_clock.GetUtcNow() - _firstSeen > TimeSpan.FromSeconds(45))
        { _finished = true;Publish("本局未应用", "已错过加载阶段或资源未就绪，本局停止尝试；下局会重新检测。");return; }
        token.ThrowIfCancellationRequested();
        if (!core.Enabled) core.SetEnabled(true);
        try
        {
            var result = await core.ApplyRankBorderForLoadingAsync(config.PreferredRankBorderProfile, config.PreferredRankBorderId, token).ConfigureAwait(false);
            _finished = true;
            Publish(result.StateVerified ? "接口核验通过" : "结果未核验", result.StateVerified
                ? "已在加载阶段更新自己的段位边框，资源状态回读一致；实际画面仍需确认。"
                : "本局边框结果未确认，停止自动操作，不重复提交。");
        }
        catch (SkinCoreException error) when (!error.OutcomeUnknown && error.Code is "game_window_not_found" or "rank_border_not_ready" or "rank_border_loading_assets_unavailable")
        { Publish("等待加载资源", "等待自己的加载卡片与段位资源就绪。"); }
        catch (SkinCoreException error)
        { _finished = true;Publish("本局已停止", $"加载边框未完成：{error.Code}。未自动重试，请查看核心诊断日志。"); }
        catch (OperationCanceledException) { _finished = true;throw; }
    }
    private void Publish(string status, string message)
    {
        if (_lastStatus == status) return;_lastStatus = status;Changed?.Invoke(new(status, message));
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();if (Volatile.Read(ref _started) == 0) _lifetime.Dispose();
    }
}
