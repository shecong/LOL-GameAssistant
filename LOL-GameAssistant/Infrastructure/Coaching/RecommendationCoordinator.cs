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
    private static readonly TimeSpan CloudMinimumInterval = TimeSpan.FromMinutes(2);
    private readonly IAiCoachingService _aiCoachingService;
    private readonly IAiRecommendationProvider _aiRecommendationProvider;
    private readonly ILocalRecommendationService _localRecommendationService;
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
        IAiRecommendationProvider aiRecommendationProvider,
        ILocalRecommendationService localRecommendationService)
    {
        _aiCoachingService = aiCoachingService;
        _aiRecommendationProvider = aiRecommendationProvider;
        _localRecommendationService = localRecommendationService;
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
                "智能建议已关闭。可在设置中单独开启本地规则建议。",
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
            var recommendations = _localRecommendationService.Create(context, now).ToList();
            bool liveDataUnavailable = string.Equals(context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                                       !context.HasLiveClientData;
            RecommendationStatus status = liveDataUnavailable
                ? RecommendationStatus.DataUnavailable
                : RecommendationStatus.LocalRulesReady;
            string diagnostic = liveDataUnavailable
                ? "未读取到本机实时金币、装备和时间；当前仅保留基础建议，稍后会自动重试。"
                : "本地时间线建议已更新，无需配置 API Key。";
            string fingerprint = BuildContextFingerprint(context);

            if (settings.Ai.Enabled && !liveDataUnavailable)
            {
                if (string.IsNullOrWhiteSpace(settings.Ai.EncryptedApiKey) || string.IsNullOrWhiteSpace(settings.Ai.Model))
                {
                    status = RecommendationStatus.ConfigurationRequired;
                    diagnostic = "本地规则建议已更新；云端增强未配置 API Key 或模型名称。";
                }
                else if (ShouldRequestCloud(fingerprint, now))
                {
                    _lastCloudRequestAt = now;
                    _lastCloudFingerprint = fingerprint;
                    AiRecommendationResult cloudResult = await _aiRecommendationProvider
                        .CreateAsync(settings.Ai, context, refreshToken)
                        .ConfigureAwait(false);
                    if (cloudResult.FromAi && !string.IsNullOrWhiteSpace(cloudResult.Recommendation))
                    {
                        _lastCloudRecommendations = CloudRecommendationParser.Parse(cloudResult.Recommendation, now);
                        recommendations.AddRange(_lastCloudRecommendations);
                        status = RecommendationStatus.EnhancedByAi;
                        diagnostic = $"本地规则已更新，并由 {settings.Ai.Provider} 提供云端补充。";
                    }
                    else
                    {
                        status = RecommendationStatus.LocalRulesReady;
                        diagnostic = "本地规则建议已更新；云端增强不可用：" +
                                     (string.IsNullOrWhiteSpace(cloudResult.Error) ? "服务没有返回可用内容。" : cloudResult.Error);
                    }
                }
                else
                {
                    if (_lastCloudRecommendations.Count > 0)
                    {
                        recommendations.AddRange(_lastCloudRecommendations);
                        status = RecommendationStatus.EnhancedByAi;
                        diagnostic = "本地规则建议已更新；云端补充沿用最近一次有效结果，避免频繁请求。";
                    }
                    else
                    {
                        diagnostic = "本地规则建议已更新；云端增强将于局势发生变化后请求。";
                    }
                }
            }

            var state = new RecommendationState(
                status,
                context,
                recommendations
                    .OrderByDescending(item => item.Priority)
                    .ThenBy(item => item.Category, StringComparer.Ordinal)
                    .ToArray(),
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

    private bool ShouldRequestCloud(string fingerprint, DateTimeOffset now) =>
        fingerprint != _lastCloudFingerprint && now - _lastCloudRequestAt >= CloudMinimumInterval;

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
            Enabled = source.Ai.Enabled,
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
            RecommendationOverlayDurationSeconds = source.Ai.RecommendationOverlayDurationSeconds,
            AnakinEnabled = source.Ai.AnakinEnabled,
            AnakinEncryptedApiKey = source.Ai.AnakinEncryptedApiKey
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