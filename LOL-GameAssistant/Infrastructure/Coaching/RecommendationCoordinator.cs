using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Ai;

namespace LOL_GameAssistant.Infrastructure.Coaching;

/// <summary>
/// 应用级建议协调器：不依赖任何页面是否打开，负责采集、去重、云端限流与可诊断状态发布。
/// </summary>
public sealed class RecommendationCoordinator : IRecommendationCoordinator
{
    private static readonly TimeSpan AiMinimumInterval = TimeSpan.FromSeconds(30);
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IAiRecommendationProvider _aiRecommendationProvider;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private System.Threading.Timer? _timer;
    private AssistantSettings _settings = new();
    private RecommendationState _current = RecommendationState.Initial;
    private DateTimeOffset _lastCloudRequestAt = DateTimeOffset.MinValue;
    private string _lastCloudFingerprint = "";
    private IReadOnlyList<CoachRecommendation> _lastCloudRecommendations = Array.Empty<CoachRecommendation>();
    private string _lastPublishedFingerprint = "";
    private CancellationTokenSource? _activeRefreshCts;
    private bool _started;
    private bool _disposed;

    public RecommendationCoordinator(
        IAiCoachingService aiCoachingService,
        IAiRecommendationProvider aiRecommendationProvider)
    {
        _aiCoachingService = aiCoachingService;
        _aiRecommendationProvider = aiRecommendationProvider;
    }

    public RecommendationState Current
    {
        get { lock (_sync) return _current; }
    }

    public event Action<RecommendationState>? StateChanged;

    public void Start(AssistantSettings settings)
    {
        if (_disposed) return;
        _started = true;
        _settings = Clone(settings);
        ConfigureTimer();
        _ = RefreshAsync(force: true, _lifetime.Token);
    }

    public void UpdateSettings(AssistantSettings settings)
    {
        if (_disposed) return;
        CancelActiveRefresh();
        _settings = Clone(settings);
        ConfigureTimer();
        if (!_settings.Ai.RecommendationEnabled)
        {
            Publish(new RecommendationState(
                RecommendationStatus.Disabled,
                Current.Context,
                Array.Empty<CoachRecommendation>(),
                "AI 时间线建议已关闭。",
                DateTimeOffset.Now,
                null), force: true);
            return;
        }

        if (_started) _ = RefreshAsync(force: true, _lifetime.Token);
    }

    public void NotifyGamePhaseChanged(string? phase)
    {
        if (_disposed || !_started) return;
        if (string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "EndOfGame", StringComparison.OrdinalIgnoreCase))
        {
            // 选人、开局和结算都是新的决策上下文；允许新阶段立即获得一次云端补充。
            _lastCloudFingerprint = "";
            _lastCloudRequestAt = DateTimeOffset.MinValue;
            _lastCloudRecommendations = Array.Empty<CoachRecommendation>();
            CancelActiveRefresh();
        }

        if (string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "EndOfGame", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshAsync(force: true, _lifetime.Token);
        }
    }

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        CancellationTokenSource? refreshCts = null;
        try
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            bool disposedAfterAcquire;
            lock (_sync)
            {
                disposedAfterAcquire = _disposed;
                if (!disposedAfterAcquire) _activeRefreshCts = refreshCts;
            }
            if (disposedAfterAcquire)
            {
                refreshCts.Dispose();
                _refreshGate.Release();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            CancellationToken refreshToken = refreshCts.Token;
            AssistantSettings settings = _settings;
            if (!settings.Ai.RecommendationEnabled)
            {
                Publish(new RecommendationState(
                    RecommendationStatus.Disabled,
                    Current.Context,
                    Array.Empty<CoachRecommendation>(),
                    "智能建议已关闭。",
                    DateTimeOffset.Now,
                    null));
                return;
            }

            Publish(new RecommendationState(
                RecommendationStatus.Collecting,
                Current.Context,
                Current.Recommendations,
                "正在读取当前可见的对局信息…",
                DateTimeOffset.Now,
                GetNextRefreshAt(settings)));

            AiGameContext context;
            try
            {
                context = await _aiCoachingService.CollectContextAsync(refreshToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Publish(new RecommendationState(
                    RecommendationStatus.DataUnavailable,
                    Current.Context,
                    Current.Recommendations,
                    "无法读取对局数据：" + ToFriendlyMessage(ex),
                    DateTimeOffset.Now,
                    GetNextRefreshAt(settings)), force: true);
                return;
            }

            if (!IsRecommendationPhase(context.Phase))
            {
                Publish(new RecommendationState(
                    RecommendationStatus.NoActiveGame,
                    context,
                    Array.Empty<CoachRecommendation>(),
                    "等待进入英雄选择或对局。",
                    DateTimeOffset.Now,
                    GetNextRefreshAt(settings)), force: true);
                return;
            }

            DateTimeOffset now = DateTimeOffset.Now;
            bool liveDataUnavailable = string.Equals(context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                                       !context.HasLiveClientData;
            string fingerprint = BuildContextFingerprint(context);

            if (string.IsNullOrWhiteSpace(settings.Ai.EncryptedApiKey) ||
                string.IsNullOrWhiteSpace(settings.Ai.Model))
            {
                Publish(new RecommendationState(
                    RecommendationStatus.ConfigurationRequired,
                    context,
                    Array.Empty<CoachRecommendation>(),
                    "请在设置中填写 AI 服务商、模型名称和 API Key；未配置时不会生成本地替代建议。",
                    now,
                    GetNextRefreshAt(settings)), force: true);
                return;
            }

            if (liveDataUnavailable)
            {
                Publish(new RecommendationState(
                    RecommendationStatus.DataUnavailable,
                    context,
                    Array.Empty<CoachRecommendation>(),
                    "尚未读取到本机实时金币、装备和时间；不会发送不完整的局内数据给 AI，稍后自动重试。",
                    now,
                    GetNextRefreshAt(settings)), force: true);
                return;
            }

            IReadOnlyList<CoachRecommendation> recommendations;
            RecommendationStatus status;
            string diagnostic;
            if (ShouldRequestAi(fingerprint, now, force))
            {
                _lastCloudRequestAt = now;
                _lastCloudFingerprint = fingerprint;
                AiRecommendationResult aiResult = await _aiRecommendationProvider
                    .CreateAsync(settings.Ai, context, refreshToken)
                    .ConfigureAwait(false);
                if (!aiResult.FromAi || string.IsNullOrWhiteSpace(aiResult.Recommendation))
                {
                    _lastCloudRecommendations = Array.Empty<CoachRecommendation>();
                    status = RecommendationStatus.Failed;
                    recommendations = Array.Empty<CoachRecommendation>();
                    diagnostic = "AI 未返回可用建议：" +
                                 (string.IsNullOrWhiteSpace(aiResult.Error) ? "服务没有返回内容。" : aiResult.Error);
                }
                else
                {
                    _lastCloudRecommendations = CloudRecommendationParser.Parse(aiResult.Recommendation, now);
                    status = RecommendationStatus.AiReady;
                    recommendations = _lastCloudRecommendations;
                    diagnostic = $"已将当前可见对局数据发送给 {settings.Ai.Provider} 并生成 AI 时间线建议。";
                }
            }
            else
            {
                status = RecommendationStatus.AiReady;
                recommendations = _lastCloudRecommendations;
                diagnostic = "当前局势未发生需要重新请求的变化，显示最近一次 AI 时间线建议。";
            }

            var state = new RecommendationState(
                status,
                context,
                recommendations,
                diagnostic,
                now,
                GetNextRefreshAt(settings));
            Publish(state, force || fingerprint != _lastPublishedFingerprint);
            _lastPublishedFingerprint = fingerprint;
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            // 应用退出或更新配置时停止旧一轮，不发布错误状态。
        }
        catch (Exception ex)
        {
            Publish(new RecommendationState(
                RecommendationStatus.Failed,
                Current.Context,
                Current.Recommendations,
                "建议生成失败：" + ToFriendlyMessage(ex),
                DateTimeOffset.Now,
                GetNextRefreshAt(_settings)), force: true);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeRefreshCts, refreshCts)) _activeRefreshCts = null;
            }
            refreshCts.Dispose();
            _refreshGate.Release();
        }
    }

    private void CancelActiveRefresh()
    {
        lock (_sync)
        {
            _activeRefreshCts?.Cancel();
        }
    }

    private void ConfigureTimer()
    {
        _timer ??= new System.Threading.Timer(_ => _ = RefreshAsync(cancellationToken: _lifetime.Token));
        if (!_settings.Ai.RecommendationEnabled || !_settings.Ai.DynamicRefreshEnabled)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Clamp(_settings.Ai.DynamicRefreshSeconds, 15, 600));
        _timer.Change(interval, interval);
    }

    private bool ShouldRequestAi(string fingerprint, DateTimeOffset now, bool force) =>
        force || ((fingerprint != _lastCloudFingerprint || _lastCloudRecommendations.Count == 0) &&
                  now - _lastCloudRequestAt >= AiMinimumInterval);

    private static bool IsRecommendationPhase(string phase) =>
        string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase);

    private DateTimeOffset? GetNextRefreshAt(AssistantSettings settings) =>
        settings.Ai.RecommendationEnabled && settings.Ai.DynamicRefreshEnabled
            ? DateTimeOffset.Now.AddSeconds(Math.Clamp(settings.Ai.DynamicRefreshSeconds, 15, 600))
            : null;

    private void Publish(RecommendationState state, bool force = false)
    {
        if (_disposed) return;
        string displayFingerprint = $"{state.Status}|{state.Diagnostic}|{string.Join('|', state.Recommendations.Select(item => item.Id))}";
        lock (_sync)
        {
            if (!force && displayFingerprint == BuildDisplayFingerprint(_current)) return;
            _current = state;
        }

        Action<RecommendationState>? handlers = StateChanged;
        if (handlers == null) return;
        foreach (Action<RecommendationState> handler in handlers.GetInvocationList().Cast<Action<RecommendationState>>())
        {
            try { handler(state); }
            catch { /* 展示层订阅失败不能阻断调度器。 */ }
        }
    }

    private static string BuildDisplayFingerprint(RecommendationState state) =>
        $"{state.Status}|{state.Diagnostic}|{string.Join('|', state.Recommendations.Select(item => item.Id))}";

    private static string BuildContextFingerprint(AiGameContext context) =>
        string.Join("|", new[]
        {
            context.Phase,
            context.Mode,
            context.MyChampionId.ToString(),
            context.MyRole,
            (context.GameTimeSeconds / 60).ToString(),
            (context.CurrentGold / 350).ToString(),
            string.Join(",", context.CurrentItems.OrderBy(item => item, StringComparer.Ordinal)),
            string.Join(",", context.EnemyChampions.OrderBy(item => item, StringComparer.Ordinal))
        });

    private static AssistantSettings Clone(AssistantSettings source) => new()
    {
            Ai = new CloudAiSettings
            {
                RecommendationEnabled = source.Ai.RecommendationEnabled,
                Provider = source.Ai.Provider,
            Model = source.Ai.Model,
            BaseUrl = source.Ai.BaseUrl,
            EncryptedApiKey = source.Ai.EncryptedApiKey,
            DynamicRefreshEnabled = source.Ai.DynamicRefreshEnabled,
            DynamicRefreshSeconds = source.Ai.DynamicRefreshSeconds,
            ShowRecommendationPopup = source.Ai.ShowRecommendationPopup,
            RecommendationOverlayEnabled = source.Ai.RecommendationOverlayEnabled,
            RecommendationOverlayPosition = source.Ai.RecommendationOverlayPosition,
            RecommendationOverlayOffsetX = source.Ai.RecommendationOverlayOffsetX,
            RecommendationOverlayOffsetY = source.Ai.RecommendationOverlayOffsetY,
                RecommendationOverlayDurationSeconds = source.Ai.RecommendationOverlayDurationSeconds
        }
    };

    private static string ToFriendlyMessage(Exception ex) => ex switch
    {
        HttpRequestException => "本机客户端连接失败。",
        TaskCanceledException => "读取超时。",
        _ => ex.Message
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelActiveRefresh();
        _lifetime.Cancel();
        _timer?.Dispose();
        _refreshGate.Dispose();
        _lifetime.Dispose();
    }
}
