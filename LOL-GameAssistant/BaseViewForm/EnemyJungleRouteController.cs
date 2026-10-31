using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>仅响应手动分析；局内路线通过原生覆盖层展示，不激活桌面窗口。</summary>
internal sealed class EnemyJungleRouteController(EnemyJungleRouteService service, IGameOverlayBridge? overlay = null,
    Func<bool>? gameForeground = null) : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private CancellationTokenSource? _request;
    private EnemyJungleRouteForm? _window;
    private bool _started, _enabled, _busy, _pending, _disposed;
    private int _revision;
    private string? _selectedEnemy;
    private string _hotkey = "F10";
    internal bool OverlayVisible { get; private set; }
    internal JungleRouteAnalysis Current { get; private set; } = new("功能未开启。请在设置 → AI 与推荐中开启敌方打野路线分析。");
    internal event Action? Changed;

    public void Apply(bool enabled)
    {
        if (_disposed || _started && _enabled == enabled) return;
        if (!_started)
        {
            _timer.Tick += (_, _) =>
            {
                if (!(gameForeground?.Invoke() ?? WindowHoldController.TryGetForegroundGameBounds(out _))) HideForGame();
            };
            _started = true;
        }
        _enabled = enabled;
        _revision++;
        _request?.Cancel();
        _selectedEnemy = null;
        Current = new(enabled ? "等待手动分析。请在游戏中按路线快捷键，或点击重新分析。" : "功能未开启。请在设置 → AI 与推荐中开启敌方打野路线分析。");
        if (!enabled) HideForGame();
        NotifyChanged();
    }

    internal Task ToggleForGameAsync(string hotkey)
    {
        if (_disposed || !_enabled) return Task.CompletedTask;
        if (OverlayVisible) { HideForGame(); return Task.CompletedTask; }
        if (!(gameForeground?.Invoke() ?? WindowHoldController.TryGetForegroundGameBounds(out _))) return Task.CompletedTask;
        _hotkey = hotkey;
        _selectedEnemy = null;
        OverlayVisible = true;
        _timer.Start();
        return RefreshAsync(force: true);
    }

    internal void HideForGame()
    {
        _timer.Stop();
        if (OverlayVisible)
        {
            OverlayVisible = false;
            _revision++;
            _request?.Cancel();
        }
        _pending = false;
        overlay?.HidePanel(GameOverlayPanelKind.JungleRoute);
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
        if (!OverlayVisible || overlay == null || _disposed) return;
        using var image = JungleRouteOverlayRenderer.Render(Current, _hotkey);
        overlay.PublishPanel(new(GameOverlayPanelKind.JungleRoute, Image: image));
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
                NotifyChanged();
            }
            var result = await service.AnalyzeAsync(_selectedEnemy, force, request.Token);
            if (_disposed || !_enabled || revision != _revision) return;
            if (_selectedEnemy != null && !result.Enemies.Any(p => p.Puuid == _selectedEnemy)) _selectedEnemy = null;
            Current = result;
            NotifyChanged();
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && _enabled && revision == _revision)
            { Current = new(request.IsCancellationRequested ? "分析超时，请按快捷键收起后重新分析。" : "客户端会话已变化，请手动重新分析。"); NotifyChanged(); }
        }
        catch (Exception error)
        {
            if (!_disposed && _enabled && revision == _revision)
            {
                RuntimeDiagnostics.WriteException(error);
                Current = new("历史时间线暂不可用，请手动重试；请确认客户端已登录且当前大区支持战绩查询。");
                NotifyChanged();
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
        HideForGame();
        _disposed = true;
        _revision++;
        _timer.Dispose();
        _request?.Cancel();
        _window?.Dispose();
    }
}
