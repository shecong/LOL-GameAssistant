using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Lobby;

namespace LOL_GameAssistant.Application.Automation;

public sealed record AutomationStep(string Name, string State, string Detail, DateTimeOffset UpdatedAt);

/// <summary>一局内的可暂停步骤；重试只针对失败项，成功项不会重复提交。</summary>
public sealed class PostGameAutomation(IClientFeatureService features, IPartyService party)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, AutomationStep> _steps = new();
    private CancellationTokenSource _session = new();
    private (bool Honor, bool Return, bool Search, string Strategy)? _plan;
    private volatile bool _paused;
    private string _phase = "";
    private bool _sawEnd;
    private long _generation;
    public bool IsPaused => _paused;
    public event Action<AutomationStep>? Changed;
    public IReadOnlyList<AutomationStep> Snapshot() { lock (_sync) return _steps.Values.ToArray(); }
    public void NotifyPhase(string phase)
    {
        if (phase == "ChampSelect" && _phase != phase && _sawEnd) Reset();
        if (phase is "EndOfGame" or "WaitingForStats") _sawEnd = true;
        _phase = phase;
    }
    public void Reset()
    {
        lock (_sync) { _generation++; _session.Cancel(); _session = new(); _steps.Clear(); _plan = null; _paused = false; _sawEnd = false; }
    }
    public void Pause()
    {
        lock (_sync) { _paused = true; _session.Cancel(); }
        Set("自动化", "已暂停", "本局暂停；可手动恢复，下一场选人恢复");
    }
    public void Resume()
    {
        lock (_sync) { if (_session.IsCancellationRequested) _session = new(); _paused = false; }
        Set("自动化", "已恢复", "后续动作重新核对客户端当前阶段；已成功步骤不重复执行");
    }
    public Task RetryFailedAsync(CancellationToken token = default) => _plan is { } p ? RunAsync(p.Honor, p.Return, p.Search, token, true, p.Strategy) : Task.CompletedTask;
    public async Task RunAsync(bool honor, bool returnToLobby, bool search, CancellationToken token = default, bool retry = false, string strategy = "random")
    {
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            CancellationToken sessionToken; long generation;
            lock (_sync) { _plan = (honor, returnToLobby, search, strategy); sessionToken = _session.Token; generation = _generation; }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, sessionToken);
            void Publish(string name, string state, string detail) { lock (_sync) { if (generation == _generation) Set(name, state, detail); } }
            async Task<bool> Step(string name, bool enabled, Func<CancellationToken, Task<ClientFeatureResult>> action)
            {
                AutomationStep? old; lock (_sync) old = _steps.GetValueOrDefault(name);
                if (old?.State == "成功") return true;
                if (!enabled) { Publish(name, "跳过", "设置未启用"); return true; }
                if (_paused) { Publish(name, "暂停", "等待用户恢复"); return false; }
                lifetime.Token.ThrowIfCancellationRequested(); Publish(name, "等待", "正在核对当前阶段和可执行条件");
                try
                {
                    var result = await action(lifetime.Token).ConfigureAwait(false);
                    Publish(name, result.Succeeded ? "成功" : "失败", result.Message); return result.Succeeded;
                }
                catch (OperationCanceledException) { Publish(name, "暂停", "操作等待已取消；不会直接重发已被客户端接受的动作"); throw; }
                catch (Exception e) { Publish(name, "失败", e.Message); return false; }
            }
            if (honor && strategy == "manual")
            {
                Publish("赛后点赞", "跳过", "保留手动投票");
                Publish("返回大厅", "等待", "等待用户完成荣誉投票；请手动返回大厅");
                return;
            }
            bool honored = await Step("赛后点赞", honor, t => features.HonorEligibleAllyAsync(strategy, t)).ConfigureAwait(false);
            if (!honored) { Publish("返回大厅", "等待", "等待荣誉步骤完成，避免打断投票"); return; }
            bool returned = await Step("返回大厅", returnToLobby, t => features.ReturnToLobbyAsync(false, t)).ConfigureAwait(false);
            if (returned)
                await Step("继续匹配", returnToLobby && search, async t =>
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var state = await party.GetSnapshotAsync(t).ConfigureAwait(false);
                        if (state.CanStart) { var result = await party.StartSearchAsync(t).ConfigureAwait(false); return new(result.Succeeded, result.Message); }
                        Publish("继续匹配", "等待", state.Message); await Task.Delay(500, t).ConfigureAwait(false);
                    }
                    return ClientFeatureResult.Failure("队伍尚未满足匹配条件；可在开黑大厅核对后手动重试");
                }).ConfigureAwait(false);
            else Publish("继续匹配", "等待", "等待返回大厅步骤成功");
        }
        catch (OperationCanceledException) { }
        finally { _gate.Release(); }
    }
    private void Set(string name, string state, string detail)
    {
        var entry = new AutomationStep(name, state, detail, DateTimeOffset.UtcNow); lock (_sync) _steps[name] = entry; Changed?.Invoke(entry);
    }
}
