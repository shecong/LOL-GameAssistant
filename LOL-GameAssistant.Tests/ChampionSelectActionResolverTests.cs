using LOL_GameAssistant.Entity;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class ChampionSelectActionResolverTests
{
    [Fact]
    public void TeammatePickIntents_ExcludeLocalPlayerOpponentsAndEmptyIntents()
    {
        var session = new ChampSelectSession
        {
            LocalPlayerCellId = 2,
            MyTeam =
            [
                new ChampSelectTeamMember { CellId = 1, ChampionPickIntent = 103 },
                new ChampSelectTeamMember { CellId = 2, ChampionPickIntent = 22 },
                new ChampSelectTeamMember { CellId = 3, ChampionPickIntent = 103 },
                new ChampSelectTeamMember { CellId = 4, ChampionPickIntent = 0 },
                new ChampSelectTeamMember { CellId = 5, ChampionPickIntent = -1 }
            ],
            TheirTeam = [new ChampSelectTeamMember { CellId = 6, ChampionPickIntent = 99 }]
        };

        Assert.Equal(new[] { 103 }, ChampionSelectActionResolver.CollectTeammatePickIntentIds(session));
    }

    [Fact]
    public void TeammatePickIntents_ReflectChangedAndCancelledPreselection()
    {
        var teammate = new ChampSelectTeamMember { CellId = 1, ChampionPickIntent = 103 };
        var session = new ChampSelectSession { LocalPlayerCellId = 2, MyTeam = [teammate] };

        Assert.Contains(103, ChampionSelectActionResolver.CollectTeammatePickIntentIds(session));
        teammate.ChampionPickIntent = 99;
        Assert.Equal(new[] { 99 }, ChampionSelectActionResolver.CollectTeammatePickIntentIds(session));
        teammate.ChampionPickIntent = 0;
        Assert.Empty(ChampionSelectActionResolver.CollectTeammatePickIntentIds(session));
    }

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
