using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Domain.Teams;

/// <summary>保存本轮实际大厅成员及明确的会话 PartyId，补足游戏阶段缺失的组队字段。</summary>
public sealed class CurrentPartySnapshotCache
{
    private readonly Dictionary<string, string> _sessionParties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lobbyMembers = new(StringComparer.Ordinal);
    private string _localPuuid = "";

    public void Clear()
    {
        _sessionParties.Clear();
        _lobbyMembers.Clear();
        _localPuuid = "";
    }

    public void ObserveLobby(LobbySnapshot lobby)
    {
        foreach (string puuid in _lobbyMembers) _sessionParties.Remove(puuid);
        _lobbyMembers.Clear();
        _localPuuid = lobby.LocalPlayerPuuid;
        if (lobby.IsCustom || string.IsNullOrWhiteSpace(_localPuuid)) return;
        foreach (var member in lobby.PartyMembers.Where(member => !member.IsBot && !string.IsNullOrWhiteSpace(member.Puuid)))
            _lobbyMembers.Add(member.Puuid);
        if (!_lobbyMembers.Contains(_localPuuid)) _lobbyMembers.Clear();
    }

    public void ObserveSession(IEnumerable<GameTeamMember> members)
    {
        foreach (var member in members.Where(member => !member.IsBot && !string.IsNullOrWhiteSpace(member.Puuid)))
            if (CurrentPartyDetector.IsValidPartyId(member.PartyId))
                _sessionParties[member.Puuid] = member.PartyId.Trim();
    }

    public IReadOnlyList<GameTeamMember> Restore(IReadOnlyList<GameTeamMember> team)
    {
        bool containsLocal = team.Any(member => member.Puuid == _localPuuid);
        string lobbyParty = team.Where(member => _lobbyMembers.Contains(member.Puuid) &&
                CurrentPartyDetector.IsValidPartyId(member.PartyId))
            .Select(member => member.PartyId.Trim()).FirstOrDefault()
            ?? _sessionParties.GetValueOrDefault(_localPuuid, $"confirmed-lobby:{_localPuuid}");
        return team.Select(member =>
        {
            string party = member.PartyId;
            if (!CurrentPartyDetector.IsValidPartyId(party))
            {
                party = _sessionParties.GetValueOrDefault(member.Puuid, "");
                if (!CurrentPartyDetector.IsValidPartyId(party) && containsLocal && _lobbyMembers.Contains(member.Puuid))
                    party = lobbyParty;
            }
            return new GameTeamMember
            {
                Puuid = member.Puuid, SummonerName = member.SummonerName, ChampionId = member.ChampionId,
                Position = member.Position, SecondaryPosition = member.SecondaryPosition, IsBot = member.IsBot,
                PartyId = party, TeamParticipantId = member.TeamParticipantId
            };
        }).ToArray();
    }
}
