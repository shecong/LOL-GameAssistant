using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchHistoryGameExtensionsTests
{
    [Fact]
    public void AbortedGame_IsNotCounted()
    {
        var game = new MatchHistoryGame { EndOfGameResult = "Abort_TooFewPlayers", GameDuration = 120 };

        Assert.False(game.IsCompletedGame());
    }

    [Fact]
    public void VeryShortGameMarkedComplete_IsNotCounted()
    {
        var game = new MatchHistoryGame { EndOfGameResult = "GameComplete", GameDuration = 66 };

        Assert.False(game.IsCompletedGame());
    }

    [Fact]
    public void NormalCompletedGame_IsCounted()
    {
        var game = new MatchHistoryGame { EndOfGameResult = "GameComplete", GameDuration = 1800 };

        Assert.True(game.IsCompletedGame());
    }

    [Fact]
    public void MissingResultFields_AreCountedSoLabelsDoNotDegradeGlobally()
    {
        var game = new MatchHistoryGame();

        Assert.True(game.IsCompletedGame());
    }

    [Fact]
    public void GetParticipant_ResolvesPlayerThroughIdentity()
    {
        var game = new MatchHistoryGame
        {
            ParticipantIdentities =
            {
                new MatchIdentity { ParticipantId = 7, Player = new MatchPlayer { Puuid = "self" } }
            },
            Participants =
            {
                new MatchParticipant
                {
                    participantId = 7,
                    stats = new MatchParticipantStats { kills = 3, deaths = 1, assists = 5, Win = true }
                }
            }
        };

        MatchParticipant? participant = game.GetParticipant("self");

        Assert.NotNull(participant);
        Assert.Equal(3, participant!.stats!.kills);
        Assert.True(participant.stats.Win);
        Assert.Null(game.GetParticipant("someone-else"));
    }
}
