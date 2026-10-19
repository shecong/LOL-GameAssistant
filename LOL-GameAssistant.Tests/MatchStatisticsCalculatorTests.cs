using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchStatisticsCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void TakesNewestFiftySameModeGamesBeforeComputingAndDoesNotLimitByAge()
    {
        var history = Enumerable.Range(1, 120).Select(id =>
        {
            var game = Game(id);
            game.GameCreation = Now.AddDays(-400 - id).ToUnixTimeMilliseconds();
            if (id % 2 == 0) game.QueueId = 420;
            if (id > 100) game.Participants[0].stats!.kills = 9999;
            return game;
        }).Reverse().ToArray();
        var result = Calculate(history);
        Assert.Equal(50, result.Assessment.SampleSize);
        Assert.Equal(50, result.Assessment.WinSampleSize);
        Assert.Equal(6, result.Assessment.Kda);
        Assert.Equal(99, result.Games.Last().GameId);
        Assert.False(result.Assessment.UsesAllModes);
        var fallback = MatchStatisticsCalculator.Calculate(history, "self", 900, "URF", "无限火力", 22, Now);
        Assert.True(fallback.Assessment.UsesAllModes);
        Assert.Equal(50, fallback.Assessment.SampleSize);
        Assert.Equal(50, fallback.Games.Last().GameId);
    }
    [Fact]
    public void CustomClassicRoomUsesClassicPvpHistoryWithoutMixingOtherModes()
    {
        var solo = Game(1); solo.QueueId = 420; solo.GameMode = "CLASSIC";
        var flex = Game(2); flex.QueueId = 440; flex.GameMode = "CLASSIC";
        var normal = Game(3); normal.QueueId = 430; normal.GameMode = "CLASSIC";
        var aram = Game(4);
        var kiwi = Game(5); kiwi.QueueId = 2400; kiwi.GameMode = "KIWI";
        var bot = Game(6); bot.QueueId = 830; bot.GameMode = "CLASSIC";
        var urf = Game(7); urf.QueueId = 900; urf.GameMode = "CLASSIC";
        var games = new[] { solo, flex, normal, aram, kiwi, bot, urf };
        var result = MatchStatisticsCalculator.Calculate(games, "self", 3110, "CLASSIC", "自定义峡谷", 22, Now);
        Assert.Equal(3, result.Assessment.SampleSize);
        Assert.Equal(6, result.Assessment.Kda);
        Assert.False(result.Assessment.UsesAllModes);
        Assert.Equal(3, result.Assessment.WinSampleSize);
        Assert.Equal(1, MatchStatisticsCalculator.Calculate(games, "self", 420, "CLASSIC", "单双排", 22, Now).Assessment.SampleSize);
    }

    [Fact]
    public void OldGamesRemainEligibleAndMissingFieldsAreExplained()
    {
        var old = Game(1); old.GameCreation = Now.AddDays(-31).ToUnixTimeMilliseconds();
        Assert.Equal(1, Calculate([old]).Assessment.SampleSize);
        Assert.Null(Calculate([old]).Assessment.NoKdaSampleReason);
        var other = Game(2); other.QueueId = 420;
        Assert.True(Calculate([other]).Assessment.UsesAllModes);
        Assert.Equal(1, Calculate([other]).Assessment.SampleSize);
        var missing = Game(3); missing.Participants[0].stats!.HasKdaResult = false;
        Assert.Equal("KDA字段缺失", Calculate([missing]).Assessment.NoKdaSampleReason);
    }
    [Fact]
    public void IndependentFieldSamplesDoNotInventLossesOrZeroKda()
    {
        var noWin = Game(1); noWin.Participants[0].stats!.HasWinResult = false;
        var noKda = Game(2); noKda.Participants[0].stats!.HasKdaResult = false; noKda.Participants[0].stats!.Win = false;
        var valid = Game(3);
        var result = Calculate([noWin, noKda, valid]);
        Assert.Equal(2, result.Assessment.SampleSize);
        Assert.Equal(2, result.Assessment.WinSampleSize);
        Assert.Equal(50, result.Assessment.WinRate);
        Assert.Equal(6, result.Assessment.Kda);
        Assert.Equal(2, result.ChampionGames);
        Assert.Equal(1, result.ChampionWins);
        Assert.Equal(0, result.RecentStreak);
        Assert.Equal(10, result.Assessment.RequiredSampleSize);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, result.Assessment.Label);
    }

    [Fact]
    public void CompletedSameModeGamesCountWithoutDateCutoffButNotFutureGames()
    {
        var boundary = Game(1); boundary.GameCreation = Now.AddDays(-30).ToUnixTimeMilliseconds();
        var old = Game(2); old.GameCreation = boundary.GameCreation - 1;
        var future = Game(3); future.GameCreation = Now.AddSeconds(1).ToUnixTimeMilliseconds();
        var other = Game(4); other.QueueId = 420;
        var remake = Game(5); remake.GameDuration = 90;
        var result = Calculate([boundary, old, future, other, remake, boundary]);
        Assert.Equal(2, result.Assessment.SampleSize);
        Assert.Equal(2, result.RecentResults.Count);
        Assert.False(result.Assessment.UsesAllModes);
    }

    [Fact]
    public void RatingUsesAllEligibleSummariesWhileRecentResultsUseTenAndChampionRateUsesOwnSamples()
    {
        var games = Enumerable.Range(1, 20).Select(Game).ToArray();
        foreach (var game in games.Skip(10)) { game.Participants[0].stats!.Win = false; game.Participants[0].championId = 99; }
        games[0].Participants[0].stats!.kills = 40;
        var result = Calculate(games);
        Assert.Equal(20, result.Assessment.SampleSize);
        Assert.Equal(50, result.Assessment.WinRate);
        Assert.Equal(10, result.RecentResults.Count);
        Assert.All(result.RecentResults, Assert.True);
        Assert.Equal(10, result.ChampionGames);
        Assert.Equal(10, result.ChampionWins);
        Assert.Equal(6.75, result.Assessment.Kda);
        Assert.Equal(RecentPerformanceLabel.Upper, result.Assessment.Label);
    }

    [Fact]
    public void AllModesFallbackIsAutomaticAndMinimumIsConfigurable()
    {
        var other = Game(1); other.QueueId = 420;
        Assert.True(Calculate([other]).Assessment.UsesAllModes);
        var result = Calculate([other], new(MinimumRatingGames: 1, AllowAllModesFallback: true));
        Assert.True(result.Assessment.UsesAllModes);
        Assert.Equal(RecentPerformanceLabel.Upper, result.Assessment.Label);
    }

    [Fact]
    public void FallbackExcludesCustomQueueAndCustomTypeWithoutDateCutoff()
    {
        var other = Game(1); other.QueueId = 420;
        var customZero = Game(2); customZero.QueueId = 0;
        var customRoom = Game(3); customRoom.QueueId = 3110; customRoom.GameMode = "CLASSIC";
        var customType = Game(4); customType.GameType = "CUSTOM_GAME";
        var old = Game(5); old.QueueId = 420; old.GameCreation = Now.AddDays(-31).ToUnixTimeMilliseconds();
        var result = Calculate([other, customZero, customRoom, customType, old]);
        Assert.True(result.Assessment.UsesAllModes);
        Assert.Equal(2, result.Assessment.SampleSize);
        Assert.Equal(2, result.ChampionGames);
        Assert.Equal(2, result.RecentResults.Count);
        Assert.Equal(new long[] { 1, 5 }, result.Games.Select(game => game.GameId));
    }

    [Fact]
    public void MissingSameModeKdaFallsBackButSmallValidSampleDoesNot()
    {
        var same = Game(1); same.Participants[0].stats!.HasKdaResult = false;
        var other = Game(2); other.QueueId = 420;
        var fallback = Calculate([same, other]);
        Assert.True(fallback.Assessment.UsesAllModes);
        Assert.Equal(1, fallback.Assessment.SampleSize);
        Assert.Equal(2, fallback.Assessment.WinSampleSize);
        same.Participants[0].stats!.HasKdaResult = true;
        var preferred = Calculate([same, other]);
        Assert.False(preferred.Assessment.UsesAllModes);
        Assert.Equal(1, preferred.Assessment.SampleSize);
        Assert.Equal(1, preferred.Assessment.WinSampleSize);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, preferred.Assessment.Label);
    }

    private static PlayerMatchStatistics Calculate(IEnumerable<MatchHistoryGame> games, MatchAnalysisPolicy? policy = null) =>
        MatchStatisticsCalculator.Calculate(games, "self", 450, "ARAM", "大乱斗", 22, Now, policy);
    private static MatchHistoryGame Game(int id) => new()
    {
        GameId = id, GameCreation = Now.AddMinutes(-id).ToUnixTimeMilliseconds(), GameDuration = 1200,
        QueueId = 450, GameMode = "ARAM", Participants = [new() { participantId = 1, championId = 22,
            stats = new() { kills = 10, deaths = 2, assists = 2, Win = true } }]
    };
}
