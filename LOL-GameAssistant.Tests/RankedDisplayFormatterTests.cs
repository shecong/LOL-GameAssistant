using LOL_GameAssistant.Domain.Ranked;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RankedDisplayFormatterTests
{
    [Fact]
    public void MissingSeasonLossesUseOnlyConfirmedMatchingRecentResultsAndKeepScope()
    {
        var queue = new RankedQueue { QueueType = RankedQueues.Solo5x5, Tier = "GOLD", Wins = 429,
            HasWinLossRecord = false };
        LOL_GameAssistant.Domain.Matches.MatchHistoryGame Game(long id, int mode, bool win, bool known = true) => new()
        {
            GameId = id, QueueId = mode, GameDuration = 1200,
            Participants = [new() { stats = new() { Win = win, HasWinResult = known } }]
        };
        var win = Game(1, 420, true);
        var remake = Game(5, 420, true);
        remake.GameDuration = 120;
        var history = new LOL_GameAssistant.Domain.Matches.MatchHistoryResponse { Games = new() {
            Games = [win, win, Game(2, 420, false), Game(3, 440, true), Game(4, 420, false, false), remake] } };
        var result = RankedRecordResolver.Resolve(queue, history, "other")!;
        Assert.True(result.HasWinLossRecord);
        Assert.Equal(1, result.Wins);
        Assert.Equal(1, result.Losses);
        Assert.Equal(50, result.WinRate);
        Assert.Equal("已加载5场", result.RecordScope);
        Assert.Equal("GOLD", result.Tier);
        Assert.False(queue.HasWinLossRecord);
        Assert.Same(queue, RankedRecordResolver.Resolve(queue, null, "other"));
        var complete = queue with { Wins = 31, Losses = 20, HasWinLossRecord = true };
        Assert.Same(complete, RankedRecordResolver.Resolve(complete, history, "self"));
    }

    [Theory]
    [InlineData("GOLD", "IV", "单双排 黄金四 · 23 胜点")]
    [InlineData("EMERALD", "II", "单双排 翡翠二 · 23 胜点")]
    [InlineData("MASTER", "I", "单双排 超凡大师 · 23 胜点")]
    [InlineData("GRANDMASTER", "", "单双排 宗师 · 23 胜点")]
    public void ChineseLocaleUsesChineseTierDivisionAndPoints(string tier, string division, string expected)
    {
        var queue = new RankedQueue { Tier = tier, Division = division, LeaguePoints = 23 };

        Assert.Equal(expected, RankedDisplayFormatter.FormatSoloQueue(queue, english: false));
    }

    [Fact]
    public void EnglishLocaleKeepsEnglishRank()
    {
        var queue = new RankedQueue { Tier = "GOLD", Division = "IV", LeaguePoints = 23 };

        Assert.Equal("Solo/Duo GOLD IV · 23 LP", RankedDisplayFormatter.FormatSoloQueue(queue, english: true));
    }

    [Fact]
    public void MissingRankHasLocalizedFallback()
    {
        Assert.Equal("段位未获取", RankedDisplayFormatter.FormatSoloQueue(null, english: false));
        Assert.Equal("Rank unavailable", RankedDisplayFormatter.FormatSoloQueue(null, english: true));
    }
}
