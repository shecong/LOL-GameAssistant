using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecentModePerformanceEvaluatorTests
{
    [Fact]
    public void FiveComparableGamesWithAggregateKdaAtThreePointFive_AreUpperTier()
    {
        RecentModePerformanceAssessment result = Evaluate(5, kills: 10, deaths: 4, assists: 4);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(3.5, result.Kda, 2);
        Assert.Equal(MatchPerformanceTier.Upper, result.Tier);
    }

    [Fact]
    public void FiveComparableGamesWithAggregateKdaBelowOnePointFive_AreLowerTier()
    {
        RecentModePerformanceAssessment result = Evaluate(5, kills: 3, deaths: 5, assists: 4);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(MatchPerformanceTier.Lower, result.Tier);
    }

    [Fact]
    public void FewerThanFiveGames_DoNotProduceHorseLabel()
    {
        RecentModePerformanceAssessment result = Evaluate(4, kills: 20, deaths: 1, assists: 10);

        Assert.False(result.HasEnoughSample);
        Assert.Equal(MatchPerformanceTier.Medium, result.Tier);
    }

    [Fact]
    public void PlayerAtTeamAverageIsMediumForSingleGameExplanation()
    {
        var player = new MatchPerformanceSnapshot("self", 100, false, 5, 5, 5, 1000, 1000, 10);
        var teammate = new MatchPerformanceSnapshot("ally", 100, false, 5, 5, 5, 1000, 1000, 10);

        MatchPerformanceAssessment result = MatchPerformanceEvaluator.Evaluate(player, new[] { player, teammate });

        Assert.Equal(MatchPerformanceTier.Medium, result.Tier);
        Assert.Equal(50, result.Score);
    }

    private static RecentModePerformanceAssessment Evaluate(int count, int kills, int deaths, int assists)
    {
        var performances = Enumerable.Range(0, count)
            .Select(index => new MatchPerformanceAssessment(
                MatchPerformanceTier.Medium,
                50,
                "test",
                kills,
                deaths,
                assists));
        return RecentModePerformanceEvaluator.Evaluate("单双排", performances, Enumerable.Repeat(true, count));
    }
}