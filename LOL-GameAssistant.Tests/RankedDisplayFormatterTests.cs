using LOL_GameAssistant.Domain.Ranked;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RankedDisplayFormatterTests
{
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
