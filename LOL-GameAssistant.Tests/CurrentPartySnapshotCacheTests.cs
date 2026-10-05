using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Teams;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CurrentPartySnapshotCacheTests
{
    [Fact]
    public void ConfirmedLobbyPartySurvivesMissingGamePartyIdsOnRedSide()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveLobby(Lobby("me", "friend"));
        var red = cache.Restore([Player("me"), Player("friend"), Player("random")]);
        var result = CurrentPartyDetector.Detect(cache.Restore([Player("enemy")]), red);
        Assert.Equal("双排", result.GetTeamStatus(1));
        Assert.Equal("未知", result.GetTeamStatus(0));
        Assert.False(result.Groups.GroupByPuuid.ContainsKey("random"));
    }

    [Fact]
    public void PartialLivePartyIdJoinsMissingLobbyTeammate()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveLobby(Lobby("me", "friend"));
        var players = new[] { Player("me", "real-party"), Player("friend") };
        cache.ObserveSession(players);
        Assert.Equal("双排", CurrentPartyDetector.Detect(cache.Restore(players), []).GetTeamStatus(0));
    }

    [Fact]
    public void NewLobbyMembershipReplacesDepartedFriends()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveLobby(Lobby("me", "old-friend"));
        cache.ObserveSession([Player("me", "old-party"), Player("old-friend", "old-party")]);
        cache.ObserveLobby(Lobby("me", "new-friend"));
        var restored = cache.Restore([Player("me"), Player("old-friend"), Player("new-friend")]);
        var result = CurrentPartyDetector.Detect(restored, []);
        Assert.Equal(2, Assert.Single(result.Groups.Groups).Puuids.Count);
        Assert.False(result.Groups.GroupByPuuid.ContainsKey("old-friend"));
    }

    [Fact]
    public void ClearingAtGameEndPreventsCarryingPartyToNextGame()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveLobby(Lobby("me", "friend"));
        cache.Clear();
        Assert.Equal("未知", CurrentPartyDetector.Detect(cache.Restore([Player("me"), Player("friend")]), []).GetTeamStatus(0));
    }

    [Fact]
    public void CustomLobbyAndOppositeTeamNeverBecomeAParty()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveLobby(new LobbySnapshot { IsCustom = true, LocalPlayerPuuid = "me", PartyMembers = [Player("me"), Player("friend")] });
        Assert.Empty(CurrentPartyDetector.Detect(cache.Restore([Player("me"), Player("friend")]), []).Groups.Groups);
        cache.ObserveLobby(Lobby("me", "friend", "third"));
        var result = CurrentPartyDetector.Detect(cache.Restore([Player("me")]), cache.Restore([Player("friend"), Player("third")]));
        Assert.Empty(result.Groups.Groups);
    }

    [Fact]
    public void ConfirmedEnemySessionPartySurvivesFieldDisappearing()
    {
        var cache = new CurrentPartySnapshotCache();
        cache.ObserveSession([Player("enemy1", "enemy-party"), Player("enemy2", "enemy-party")]);
        var result = CurrentPartyDetector.Detect([], cache.Restore([Player("enemy1"), Player("enemy2")]));
        Assert.Equal("双排", result.GetTeamStatus(1));
    }

    private static LobbySnapshot Lobby(params string[] names) => new()
    {
        LocalPlayerPuuid = names[0], PartyMembers = names.Select(name => Player(name)).ToArray()
    };

    private static GameTeamMember Player(string puuid, string party = "") => new() { Puuid = puuid, SummonerName = puuid, PartyId = party };
}
