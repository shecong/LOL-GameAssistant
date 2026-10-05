using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class HistoricalPremadeTests
{
    [Fact]
    public void RelationshipChainDoesNotBecomeThreePlayers()
    {
        var result = Detect(Game(1, "a", "b"), Game(2, "a", "b"), Game(3, "b", "c"), Game(4, "b", "c"));
        Assert.Equal(2, Assert.Single(result.Groups).Puuids.Count);
    }

    [Fact]
    public void WholeGroupMustShareAtLeastTwoDistinctMatches()
    {
        Assert.Empty(Detect(Game(1, "a", "b", "c")).Groups);
        var result = Detect(Game(1, "a", "b", "c"), Game(2, "a", "b", "c"));
        Assert.Equal(3, Assert.Single(result.Groups).Puuids.Count);
        Assert.Empty(Detect(Game(1, "a", "b"), Game(1, "a", "b")).Groups);
    }

    private static PremadeDetector.PremadeResult Detect(params GameHeadModel.GameInfo[] games) =>
        PremadeDetector.DetectCore([("a", "甲"), ("b", "乙"), ("c", "丙")], [],
            [("a", new GameHeadModel.MatchHistoryResponse { Games = new GameHeadModel.GamesContainer { Games = games.ToList() } })]);

    private static GameHeadModel.GameInfo Game(long id, params string[] players) => new()
    {
        GameId = id,
        Participants = players.Select((_, index) => new GameHeadModel.Participant { ParticipantId = index + 1, TeamId = 100 }).ToList(),
        ParticipantIdentities = players.Select((puuid, index) => new GameHeadModel.ParticipantIdentity
        {
            ParticipantId = index + 1, Player = new GameHeadModel.PlayerInfo { Puuid = puuid }
        }).ToList()
    };
}
