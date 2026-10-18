using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Publishes timeline advice to the game renderer without creating a desktop popup.</summary>
internal sealed class RecommendationOverlayForm : AntdUI.Window
{
    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private readonly IGameOverlayBridge _overlay;
    internal bool OverlayVisible { get; private set; }
    public RecommendationOverlayForm() : this(AppCompositionRoot.GameOverlay) { }
    internal RecommendationOverlayForm(IGameOverlayBridge overlay)
    {
        _overlay = overlay;
        AntdWindowChrome.Configure(this, fitWorkingArea: false);
        EnableHitTest = false; ShowInTaskbar = false; TopMost = true;
        _hideTimer.Tick += (_, _) => Hide();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x080800A0; return p; }
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
    public new void Hide()
    {
        OverlayVisible = false; _hideTimer.Stop(); _overlay.HidePanel(GameOverlayPanelKind.Coaching); base.Hide();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Hide(); _hideTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
