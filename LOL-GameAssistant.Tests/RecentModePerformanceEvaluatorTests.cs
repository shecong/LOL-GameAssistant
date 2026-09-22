using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecentModePerformanceEvaluatorTests
{
    [Fact]
    public void EightComparableGamesWithAggregateKdaAtFourPointFive_AreUpperTier()
    {
        RecentModePerformanceAssessment result = Evaluate(8, kills: 10, deaths: 4, assists: 8);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(4.5, result.Kda, 2);
        Assert.Equal(MatchPerformanceTier.Upper, result.Tier);
        Assert.Equal(RecentPerformanceLabel.Upper, result.Label);
        Assert.InRange(result.Score, 85, 100);
    }

    [Fact]
    public void EightComparableGamesWithAggregateKdaBelowTwoPointTwo_AreLowerTier()
    {
        RecentModePerformanceAssessment result = Evaluate(8, kills: 3, deaths: 4, assists: 3);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(MatchPerformanceTier.Lower, result.Tier);
        Assert.Equal(RecentPerformanceLabel.Lower, result.Label);
    }

    [Fact]
    public void KdaBelowOne_IsHumanTierInsteadOfBotAccount()
    {
        RecentModePerformanceAssessment result = Evaluate(8, kills: 1, deaths: 4, assists: 2);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(MatchPerformanceTier.Lower, result.Tier);
        Assert.Equal(RecentPerformanceLabel.Human, result.Label);
        Assert.Equal("人机", RecentPerformanceLabelFormatter.GetText(result));
    }

    [Fact]
    public void FewerThanEightGames_DoNotProducePerformanceLabel()
    {
        RecentModePerformanceAssessment result = Evaluate(7, kills: 20, deaths: 1, assists: 10);

        Assert.False(result.HasEnoughSample);
        Assert.Equal(MatchPerformanceTier.Medium, result.Tier);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, result.Label);
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

    [Fact]
    public void NineteenGamesAreNotEnoughWhenTwentyAreRequired()
    {
        RecentModePerformanceAssessment result = Evaluate(19, kills: 10, deaths: 4, assists: 8, requiredSampleSize: 20);

        Assert.False(result.HasEnoughSample);
        Assert.Equal(19, result.SampleSize);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, result.Label);
    }

    [Fact]
    public void TwentyGamesAreEnoughWhenTwentyAreRequired()
    {
        RecentModePerformanceAssessment result = Evaluate(20, kills: 10, deaths: 4, assists: 8, requiredSampleSize: 20);

        Assert.True(result.HasEnoughSample);
        Assert.Equal(20, result.SampleSize);
        Assert.Equal(RecentPerformanceLabel.Upper, result.Label);
    }

    [Fact]
    public void WinRateIsReportedOverTheSameSampleAsKda()
    {
        RecentModePerformanceAssessment result = Evaluate(20, kills: 1, deaths: 4, assists: 2, requiredSampleSize: 20);

        Assert.Equal(100, result.WinRate);
    }

    private static RecentModePerformanceAssessment Evaluate(
        int count,
        int kills,
        int deaths,
        int assists,
        int requiredSampleSize = RecentModePerformanceEvaluator.RequiredSampleSize)
    {
        var performances = Enumerable.Range(0, count)
            .Select(index => new MatchPerformanceAssessment(
                MatchPerformanceTier.Medium,
                50,
                "test",
                kills,
                deaths,
                assists));
        return RecentModePerformanceEvaluator.Evaluate("单双排", performances, Enumerable.Repeat(true, count), requiredSampleSize);
    }
}