using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>后台调度独立于地图窗是否打开；关闭开关立即取消请求并清除展示。</summary>
internal sealed class EnemyJungleRouteController(EnemyJungleRouteService service) : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10000 };
    private CancellationTokenSource? _request;
    private EnemyJungleRouteForm? _window;
    private bool _started, _enabled, _busy, _pending, _disposed;
    private int _revision;
    private string? _selectedEnemy;
    internal JungleRouteAnalysis Current { get; private set; } = new("功能未开启。请在设置 → AI 与推荐中开启敌方打野路线分析。");
    internal event Action? Changed;

    public void Apply(bool enabled)
    {
        if (_disposed || _started && _enabled == enabled) return;
        if (!_started) { _timer.Tick += async (_, _) => await RefreshAsync(); _started = true; }
        _enabled = enabled;
        _revision++;
        _request?.Cancel();
        _selectedEnemy = null;
        Current = new(enabled ? "等待峡谷对局，正在识别敌方打野…" : "功能未开启。请在设置 → AI 与推荐中开启敌方打野路线分析。");
        Changed?.Invoke();
        if (enabled) { _timer.Start(); _ = RefreshAsync(); }
        else { _timer.Stop(); _pending = false; }
    }

    public void Show(Form owner)
    {
        if (_disposed) return;
        if (_window == null || _window.IsDisposed) _window = new(this);
        if (!_window.Visible) _window.Show(owner);
        else _window.Activate();
    }

    internal void SelectEnemy(string? puuid)
    {
        if (_selectedEnemy == puuid) return;
        _selectedEnemy = puuid;
        _revision++;
        _request?.Cancel();
        _ = RefreshAsync(force: true);
    }

    internal async Task RefreshAsync(bool force = false)
    {
        if (_disposed || !_enabled) return;
        if (_busy) { if (force) _pending = true; return; }
        _busy = true;
        int revision = _revision;
        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _request = request;
        try
        {
            if (force || Current.ContextKey.Length == 0)
            {
                Current = new("正在读取近期战绩与时间线，首次分析可能需要一些时间…") { Enemies = Current.Enemies, Enemy = Current.Enemy };
                Changed?.Invoke();
            }
            var result = await service.AnalyzeAsync(_selectedEnemy, force, request.Token);
            if (_disposed || !_enabled || revision != _revision) return;
            if (_selectedEnemy != null && !result.Enemies.Any(p => p.Puuid == _selectedEnemy)) _selectedEnemy = null;
            Current = result;
            Changed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && _enabled && revision == _revision)
            { Current = new(request.IsCancellationRequested ? "分析超时，稍后自动重试；也可点击重新分析。" : "客户端会话已变化，等待重新分析。"); Changed?.Invoke(); }
        }
        catch (Exception error)
        {
            if (!_disposed && _enabled && revision == _revision)
            {
                RuntimeDiagnostics.WriteException(error);
                Current = new("历史时间线暂不可用，稍后自动重试；请确认客户端已登录且当前大区支持战绩查询。");
                Changed?.Invoke();
            }
        }
        finally
        {
            _request = null;
            _busy = false;
            if (!_disposed && _enabled && _pending) { _pending = false; _ = RefreshAsync(force: true); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revision++;
        _timer.Dispose();
        _request?.Cancel();
        _window?.Dispose();
    }
}
