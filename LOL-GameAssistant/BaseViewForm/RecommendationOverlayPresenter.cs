using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Publishes timeline advice to the game renderer without creating a desktop popup.</summary>
internal sealed class RecommendationOverlayPresenter : IDisposable
{
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private readonly IGameOverlayBridge _overlay;
    internal bool OverlayVisible { get; private set; }
    public RecommendationOverlayPresenter() : this(AppCompositionRoot.GameOverlay) { }
    internal RecommendationOverlayPresenter(IGameOverlayBridge overlay)
    {
        _overlay = overlay;
        _hideTimer.Tick += (_, _) => Hide();
    }
    public void ShowRecommendation(CoachRecommendation recommendation, CloudAiSettings settings)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(recommendation.Body)) return;
        int position = settings.RecommendationOverlayPosition switch { "TopLeft" => 1, "TopRight" => 2, "BottomRight" => 3, "Center" => 4, _ => 0 };
        int duration = Math.Clamp(settings.RecommendationOverlayDurationSeconds, 3, 30) * 1000;
        OverlayVisible = true;
        _overlay.PublishPanel(new(GameOverlayPanelKind.Coaching, Clip(recommendation.Title,40), Clip(recommendation.Body,240),
            string.IsNullOrWhiteSpace(recommendation.Evidence) ? "" : "依据：" + Clip(recommendation.Evidence,120),
            Deadline: Environment.TickCount64 + duration, Position: position,
            OffsetX: settings.RecommendationOverlayOffsetX, OffsetY: settings.RecommendationOverlayOffsetY));
        _hideTimer.Stop(); _hideTimer.Interval = duration; _hideTimer.Start();
    }
    private static string Clip(string text,int max) => text.Trim().Length <= max ? text.Trim() : text.Trim()[..max] + "…";
    public void Hide()
    {
        OverlayVisible = false; _hideTimer.Stop(); _overlay.HidePanel(GameOverlayPanelKind.Coaching);
    }
    public bool IsDisposed { get; private set; }
    public void Dispose()
    {
        if(IsDisposed) return;
        IsDisposed=true;
        Hide(); _hideTimer.Dispose();
    }
}
