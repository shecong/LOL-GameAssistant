using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LiveGameForm
{
    private LiveHistoryFilter _historyFilter;

    internal void SetHistoryFilter(LiveHistoryFilter filter)
    {
        _historyFilter = Enum.IsDefined(filter) ? filter : LiveHistoryFilter.Latest;
        foreach (var card in panelTeam1.Controls.OfType<LivePlayerForm>().Concat(panelTeam2.Controls.OfType<LivePlayerForm>()))
            card.SetHistoryFilter(_historyFilter);
        UpdateTeamOverview();
    }
}
