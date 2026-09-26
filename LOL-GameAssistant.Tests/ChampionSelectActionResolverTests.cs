using LOL_GameAssistant.Entity;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class ChampionSelectActionResolverTests
{
    [Fact]
    public void FindsLocalAction_WhenTeammateActsInSameRound()
    {
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = 2,
            Actions =
            [
                [
                    new ChampSelectAction { Id = 10, ActorCellId = 1, IsAllyAction = true, IsInProgress = true, Type = "pick" },
                    new ChampSelectAction { Id = 11, ActorCellId = 2, IsAllyAction = true, IsInProgress = true, Type = "pick" }
                ]
            ]
        };

        Assert.Equal(11, ChampionSelectActionResolver.FindCurrentLocalAction(session, "pick")?.Id);
        Assert.Null(ChampionSelectActionResolver.FindCurrentLocalAction(session, "ban"));
    }

    [Fact]
    public void IgnoresCompletedAndPendingLocalActions()
    {
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = 2,
            Actions =
            [
                [new ChampSelectAction { Id = 10, ActorCellId = 2, IsInProgress = true, Completed = true, Type = "ban" }],
                [new ChampSelectAction { Id = 11, ActorCellId = 2, IsInProgress = false, Type = "ban" }]
            ]
        };

        Assert.Null(ChampionSelectActionResolver.FindCurrentLocalAction(session, "ban"));
    }
}