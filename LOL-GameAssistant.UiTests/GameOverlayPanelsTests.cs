using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

internal sealed class RecordingGameOverlay : IGameOverlayBridge
{
    public Dictionary<GameOverlayPanelKind, GameOverlayPanel> Panels { get; } = [];
    public GameOverlayPanelCommand? Command { get; set; }
    public int Publish(GameOverlayFrame frame) => 0;
    public void Disable() { }
    public void PublishPanel(GameOverlayPanel panel)
    {
        HidePanel(panel.Kind);
        Panels[panel.Kind] = panel with { Image = panel.Image == null ? null : new Bitmap(panel.Image) };
    }
    public void HidePanel(GameOverlayPanelKind kind) { if (Panels.Remove(kind, out var panel)) panel.Image?.Dispose(); }
    public GameOverlayPanelCommand? ReadPanelCommand(GameOverlayPanelKind kind) { var result = Command; Command = null; return result; }
    public Task<GameOverlayCapture?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<GameOverlayCapture?>(null);
    public void Dispose() { foreach (var panel in Panels.Values) panel.Image?.Dispose(); Panels.Clear(); }
}

public sealed class GameOverlayPanelsTests
{
    [Fact]
    public void AdviceAndSkinNoticeShareRendererWithoutDesktopHandles() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingGameOverlay();
        using var advice = new RecommendationOverlayForm(bridge);
        using var notice = new SkinChangeOverlayForm(bridge);
        advice.ShowRecommendation(new("test", "test", RecommendationPriority.Info, "控龙", "提前布置视野", "小龙即将刷新",
            RecommendationSource.CloudAi, DateTimeOffset.UtcNow.AddSeconds(30)), new CloudAiSettings { RecommendationOverlayPosition = "TopRight" });
        notice.ShowSkin("大元素使");
        Assert.False(advice.Visible); Assert.False(advice.IsHandleCreated);
        Assert.False(notice.Visible); Assert.False(notice.IsHandleCreated);
        Assert.Equal(2, bridge.Panels.Count);
        var card = bridge.Panels[GameOverlayPanelKind.Coaching];
        Assert.Equal(2, card.Position); Assert.Equal("提前布置视野", card.Body);
        Assert.True(card.Deadline > Environment.TickCount64);
        Assert.Contains("大元素使", bridge.Panels[GameOverlayPanelKind.SkinNotice].Body);
        advice.Hide(); Assert.Single(bridge.Panels); Assert.True(bridge.Panels.ContainsKey(GameOverlayPanelKind.SkinNotice));
    });
}
