using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Bootstrap;

namespace LOL_GameAssistant;

public partial class GameMain
{
    private EnemyJungleRouteController? _jungleRoute;

    private void ApplyJungleRouteSetting(bool enabled)
    {
        if (enabled && _jungleRoute == null) _jungleRoute = new(AppCompositionRoot.CreateEnemyJungleRouteService());
        _jungleRoute?.Apply(enabled);
    }

    public void ShowEnemyJungleRoute()
    {
        if (IsDisposed) return;
        _jungleRoute ??= new(AppCompositionRoot.CreateEnemyJungleRouteService());
        _jungleRoute.Apply(_settingsStore.Load().EnemyJungleRouteEnabled);
        _jungleRoute.Show(this);
    }
}
