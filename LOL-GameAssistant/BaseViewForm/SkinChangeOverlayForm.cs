using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Brief read-only confirmation, rendered in the game frame.</summary>
internal sealed class SkinChangeOverlayForm : Form
{
    private readonly IGameOverlayBridge _overlay;
    public SkinChangeOverlayForm() : this(AppCompositionRoot.GameOverlay) { }
    internal SkinChangeOverlayForm(IGameOverlayBridge overlay)
    {
        _overlay = overlay;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = TransparencyKey = Color.Fuchsia;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x080000A0; return p; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }
    internal void ShowSkin(string name) => _overlay.PublishPanel(new(GameOverlayPanelKind.SkinNotice,
        Body: $"{UiLanguage.T("已切换皮肤")}：{name}", Deadline: Environment.TickCount64 + 3000));
    protected override void Dispose(bool disposing)
    {
        if (disposing) _overlay.HidePanel(GameOverlayPanelKind.SkinNotice);
        base.Dispose(disposing);
    }
}
