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
}
