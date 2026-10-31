using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Bootstrap;

namespace LOL_GameAssistant;

public partial class GameMain
{
    private EnemyJungleRouteController? _jungleRoute;

    private void ApplyJungleRouteSetting(bool enabled)
    {
        if (enabled && _jungleRoute == null) _jungleRoute = new(AppCompositionRoot.CreateEnemyJungleRouteService(), AppCompositionRoot.GameOverlay);
        _jungleRoute?.Apply(enabled);
    }

    public void ShowEnemyJungleRoute()
    {
        if (IsDisposed) return;
        _jungleRoute ??= new(AppCompositionRoot.CreateEnemyJungleRouteService(), AppCompositionRoot.GameOverlay);
        _jungleRoute.Apply(_settingsStore.Load().EnemyJungleRouteEnabled);
        _jungleRoute.Show(this);
    }

    private Task ToggleEnemyJungleRouteAsync()
    {
        if (IsDisposed || gameFlowPhase != Domain.LeagueClient.GameFlowPhase.InProgress ||
            !Helper.WindowHoldController.TryGetForegroundGameBounds(out _)) return Task.CompletedTask;
        var settings = _settingsStore.Load();
        ApplyJungleRouteSetting(settings.EnemyJungleRouteEnabled);
        return _jungleRoute?.ToggleForGameAsync(settings.EnemyJungleRouteHotkey) ?? Task.CompletedTask;
    }
}
