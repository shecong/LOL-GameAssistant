using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CslolChampionSearchTests
{
    private readonly CslolChampionSearch _search = new();
    private readonly CslolSkinNames _names = new();

    [Theory]
    [InlineData("Vayne", "weien")]
    [InlineData("Vayne", "WE")]
    [InlineData("Vayne", "wěi ēn")]
    [InlineData("Vayne", "暗夜猎手")]
    [InlineData("Vayne", "ayls")]
    [InlineData("Annie", "anni")]
    [InlineData("Annie", "an")]
    [InlineData("Annie", "火女")]
    [InlineData("Annie", "huonv")]
    [InlineData("Annie", "hn")]
    [InlineData("LeeSin", "liqing")]
    [InlineData("LeeSin", "lq")]
    [InlineData("MonkeyKing", "sunwukong")]
    [InlineData("Jade_Wukong", "swk")]
    [InlineData("KSante", "kst")]
    [InlineData("Mel", "me")]
    public void MatchesChineseEnglishPinyinAndInitials(string key, string query) =>
        Assert.True(_search.Matches(key, _names.Champion(key), query));

    [Theory]
    [InlineData("Aatrox", "TOP")]
    [InlineData("Annie", "MIDDLE")]
    [InlineData("LeeSin", "JUNGLE")]
    [InlineData("Vayne", "BOTTOM")]
    [InlineData("Vayne", "TOP")]
    [InlineData("Lux", "SUPPORT")]
    [InlineData("Lux", "MIDDLE")]
    [InlineData("Jade_Vayne", "BOTTOM")]
    public void SupportsAllFivePositionsAndMultiplePositions(string key, string position) =>
        Assert.True(_search.Matches(key, _names.Champion(key), "", position));

    [Fact]
    public void PositionAndSearchAreCombinedAndUnknownHeroesRemainInAll()
    {
        Assert.True(_search.Matches("Vayne", "薇恩", "weien", "BOTTOM"));
        Assert.False(_search.Matches("Vayne", "薇恩", "weien", "JUNGLE"));
        Assert.False(_search.Matches("Annie", "安妮", "weien", "MIDDLE"));
        Assert.False(_search.Matches("Annie", "安妮", "no-such-hero"));
        Assert.True(_search.Matches("FutureHero", "未来英雄", "future"));
        Assert.True(_search.Matches("FutureHero", "未来英雄", ""));
        Assert.False(_search.Matches("FutureHero", "未来英雄", "", "TOP"));
    }
}
