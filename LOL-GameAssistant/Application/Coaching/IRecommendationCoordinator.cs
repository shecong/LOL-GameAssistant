using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>独立于页面生命周期的建议调度器。</summary>
public interface IRecommendationCoordinator : IDisposable
{
    RecommendationState Current { get; }

    event Action<RecommendationState>? StateChanged;

    void Start(AssistantSettings settings);

    void UpdateSettings(AssistantSettings settings);

    void NotifyGamePhaseChanged(string? phase);

    Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default);
}