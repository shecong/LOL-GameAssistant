using LOL_GameAssistant.Domain.ChampionSelect;

namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>同一流程阶段内保留最后一次有效阵容；阶段变化或对局结束后清空。</summary>
internal sealed class CurrentRosterSnapshotCache
{
    private GameFlowPhase? _phase;
    private LobbySnapshot? _lobby;
    private ChampionSelectionSnapshot? _selection;
    private ActiveGameSnapshot? _session;

    internal void ObservePhase(GameFlowPhase phase)
    {
        if (_phase == phase) return;
        Clear();
        _phase = phase;
    }

    internal LobbySnapshot? ResolveLobby(LobbySnapshot? value)
    {
        if (value != null && (value.Team100?.Count ?? 0) + (value.Team200?.Count ?? 0) > 0) _lobby = value;
        return _lobby;
    }

    internal ChampionSelectionSnapshot? ResolveSelection(ChampionSelectionSnapshot? value)
    {
        if (value != null && (value.MyTeam?.Count ?? 0) + (value.TheirTeam?.Count ?? 0) > 0) _selection = value;
        return _selection;
    }

    internal ActiveGameSnapshot? ResolveSession(ActiveGameSnapshot? value)
    {
        if (value != null && (value.TeamOne?.Count ?? 0) + (value.TeamTwo?.Count ?? 0) > 0) _session = value;
        return _session;
    }

    internal void Clear()
    {
        _phase = null;
        _lobby = null;
        _selection = null;
        _session = null;
    }
}
