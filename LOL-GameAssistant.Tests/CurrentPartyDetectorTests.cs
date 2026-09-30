using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Teams;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CurrentPartyDetectorTests
{
    [Fact]
    public void GroupsPlayersByPartyIdWithinEachSide()
    {
        var result = CurrentPartyDetector.Detect(
            [Member("a", "甲", "room-a", 1), Member("b", "乙", "room-a", 2),
             Member("c", "丙", "room-b", 3)],
            [Member("d", "丁", "room-a", 1), Member("e", "戊", "room-a", 2)]);

        Assert.Equal("双排", result.GetTeamStatus(0));
        Assert.Equal("双排", result.GetTeamStatus(1));
        Assert.Equal(2, result.Groups.Groups.Count);
        Assert.Equal(result.Groups.GroupByPuuid["a"].Index, result.Groups.GroupByPuuid["b"].Index);
        Assert.NotEqual(result.Groups.GroupByPuuid["a"].Index, result.Groups.GroupByPuuid["d"].Index);
        Assert.False(result.Groups.GroupByPuuid.ContainsKey("c"));
    }

    [Fact]
    public void FallsBackToTeamParticipantIdAndIgnoresZero()
    {
        var result = CurrentPartyDetector.Detect(
            [Member("a", "甲", teamParticipantId: 2), Member("b", "乙", teamParticipantId: 2),
             Member("c", "丙", teamParticipantId: 0)],
            []);

        Assert.Single(result.Groups.Groups);
        Assert.Equal("双排", result.GetTeamStatus(0));
        Assert.Contains("其他玩家标识尚不完整", result.GetTeamDetail(0));
        Assert.Equal("未知", result.GetTeamStatus(1));
    }

    [Fact]
    public void MissingIdentifiersNeverMeanSoloQueue()
    {
        var result = CurrentPartyDetector.Detect(
            [Member("a", "甲"), Member("b", "乙")],
            [Member("c", "丙", teamParticipantId: 1), Member("d", "丁", teamParticipantId: 2)]);

        Assert.Equal("未知", result.GetTeamStatus(0));
        Assert.Equal("未见组队", result.GetTeamStatus(1));
        Assert.Empty(result.Groups.Groups);
    }

    [Fact]
    public void HiddenPlayersAndBotsDoNotCreatePremadeGroups()
    {
        var result = CurrentPartyDetector.Detect(
            [Member("a", "甲", teamParticipantId: 3),
             new GameTeamMember { Puuid = "", SummonerName = "隐藏玩家", TeamParticipantId = 3 },
             new GameTeamMember { Puuid = "bot", SummonerName = "机器人", TeamParticipantId = 3, IsBot = true }],
            []);

        Assert.Empty(result.Groups.Groups);
        Assert.Equal("未知", result.GetTeamStatus(0));
    }

    private static GameTeamMember Member(string puuid, string name, string partyId = "", int teamParticipantId = 0) =>
        new() { Puuid = puuid, SummonerName = name, PartyId = partyId, TeamParticipantId = teamParticipantId };
}
