using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>独立于页面生命周期的建议调度器。</summary>
public interface IRecommendationCoordinator : IDisposable
{
    RecommendationState Current { get; }

    event Action<RecommendationState>? StateChanged;

    /// <summary>启动当前服务或组件的运行流程。</summary>
    void Start(AssistantSettings settings);

    /// <summary>同步最新设置并调整运行策略。</summary>
    void UpdateSettings(AssistantSettings settings);

    /// <summary>通知服务游戏阶段已变化，以更新后续处理。</summary>
    void NotifyGamePhaseChanged(string? phase);

    /// <summary>异步刷新当前服务或界面负责的数据。</summary>
    Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default);
}