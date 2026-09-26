using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class OpggModeSelectionTests
{
    [Theory]
    [InlineData("CLASSIC", 420, "ranked")]
    [InlineData("ARAM", 450, "aram")]
    [InlineData("KIWI", 2400, "aram_mayhem")]
    [InlineData("KIWI_JADE", 0, "aram_mayhem")]
    [InlineData("KIWI", 450, "aram_mayhem")]
    [InlineData("ARAM", 2400, "aram_mayhem")]
    [InlineData("ARAM", 420, "ranked")]
    [InlineData("CHERRY", 1700, "arena")]
    [InlineData("URF", 900, "urf")]
    [InlineData("", 0, "unknown")]
    public void ResolvesCurrentModeWithoutSilentlyUsingRanked(
        string gameMode, int queueId, string expected)
    {
        Assert.Equal(expected, OpggBuildApplyService.NormalizeMode(gameMode, queueId));
    }
}