using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Brief read-only confirmation, rendered in the game frame.</summary>
internal sealed class SkinChangeOverlayPresenter : IDisposable
{
    private readonly IGameOverlayBridge _overlay;
    public SkinChangeOverlayPresenter() : this(AppCompositionRoot.GameOverlay) { }
    internal SkinChangeOverlayPresenter(IGameOverlayBridge overlay)
    {
        _overlay = overlay;
    }
    internal void ShowSkin(string name) => _overlay.PublishPanel(new(GameOverlayPanelKind.SkinNotice,
        Body: $"{UiLanguage.T("已切换皮肤")}：{name}", Deadline: Environment.TickCount64 + 3000));
    public void Hide() => _overlay.HidePanel(GameOverlayPanelKind.SkinNotice);
    public bool IsDisposed { get; private set; }
    public void Dispose()
    {
        if(IsDisposed) return;
        IsDisposed=true;
        _overlay.HidePanel(GameOverlayPanelKind.SkinNotice);
    }
}
