using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant;

public partial class GameMain
{
    private void PoroShopExchangeChanged(PoroShopExchangeStatus status)
    {
        if (status.IsRunning || status.Result is not { } result) return;
        if (RunOnUiThread(() => PoroShopExchangeChanged(status))) return;
        bool succeeded = result.Items.Count > 0 && result.Items.All(item =>
            item.State == PoroShopExchangeState.Succeeded && item.Succeeded == item.Requested);
        string title = succeeded ? "魄罗商店兑换成功" : "魄罗商店兑换未全部完成";
        infoMsg.AddMsg(title + "：" + result.Message);
        if (Visible)
        {
            if (succeeded) UiMessage.success(this, result.Message);
            else UiMessage.warn(this, result.Message);
        }
        if (_trayIcon == null) return;
        try
        {
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(5000, title, result.Message,
                succeeded ? ToolTipIcon.Info : ToolTipIcon.Warning);
            _ = Task.Delay(6000).ContinueWith(_ => RunOnUiThread(UpdateTrayVisibility));
        }
        catch (Exception ex) { RuntimeDiagnostics.WriteException(ex); }
    }
}
