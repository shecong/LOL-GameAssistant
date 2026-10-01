using LOL_GameAssistant.Domain.GameData;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class AugmentNameMatcherTests
{
    [Fact]
    public void ExactCardTitleWinsOverNameContainedInTitle()
    {
        (int Id, string Name)[] names = [(1001, "巨人"), (1002, "巨人杀手")];

        Assert.Equal([1002], AugmentNameMatcher.Match("巨人杀手", names, limit: 1));
    }

    [Fact]
    public void CardTitle_PreservesSingleCharacterFragments()
    {
        (int Id, string Name)[] names = [(1001, "珠光护手")];
        Assert.Equal(1001, AugmentNameMatcher.MatchCard("珠\n光护\n手", names)?.Id);
    }

    [Fact]
    public void CardTitle_JoinsLongEnglishTitles()
    {
        (int Id, string Name)[] names = [(1001, "From Beginning to End")];
        Assert.Equal(1001, AugmentNameMatcher.MatchCard("From Beginning\nto End", names)?.Id);
    }

    [Fact]
    public void CardTitle_ToleratesOneWrongCharacterAcrossLines()
    {
        (int Id, string Name)[] names = [(1001, "珠光护手")];
        var match = AugmentNameMatcher.MatchCard("珠先\n护手", names);
        Assert.Equal(1001, match?.Id);
        Assert.Equal(0.75, match?.Score);
    }

    [Fact]
    public void CardTitle_RejectsEquallyPlausibleNames()
    {
        (int Id, string Name)[] names = [(1001, "珠光护手"), (1002, "珠光妙手")];
        Assert.Null(AugmentNameMatcher.MatchCard("珠光好手", names));
    }

    [Fact]
    public void CardTitle_PrefersExactNameOverSimilarName()
    {
        (int Id, string Name)[] names = [(1001, "珠光护手"), (1002, "珠光妙手")];
        Assert.Equal(1002, AugmentNameMatcher.MatchCard("珠光妙手", names)?.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n : \n")]
    [InlineData("123456789")]
    public void CardTitle_RejectsEmptyOrUnrelatedReadings(string text)
    {
        (int Id, string Name)[] names = [(1001, "珠光护手")];
        Assert.Null(AugmentNameMatcher.MatchCard(text, names));
    }
}
