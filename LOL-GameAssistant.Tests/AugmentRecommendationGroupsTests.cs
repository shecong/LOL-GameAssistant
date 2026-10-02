using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class AugmentRecommendationGroupsTests
{
    [Theory]
    [InlineData(1001, 2)]
    [InlineData(1005, 1)]
    [InlineData(1204, 0)]
    public async Task OfflineCatalog_HasVerifiedMayhemRarity(int id, int rarity)
    {
        var item = Assert.Single(await AugmentCatalog.ResolveAsync([id]));
        Assert.Equal(rarity, item.Rarity);
    }

    [Fact]
    public void CategoriesUseRealRarityAndSortEachTypeSeparately()
    {
        OpggAugmentRecommendation[] stats = [new(1, -1, 100, 70), new(2, -1, 100, 60), new(3, -1, 100, 80), new(4, -1, 100, 50)];
        var names = new Dictionary<int, AugmentInfo>
        {
            [1] = new(1, "白银一", null, Rarity: 0), [2] = new(2, "黄金一", null, Rarity: 1),
            [3] = new(3, "白银二", null, Rarity: 0), [4] = new(4, "棱彩一", null, Rarity: 2)
        };
        Assert.Equal([3, 1], AugmentRecommendationGroups.Select(stats, names, 0).Select(item => item.Id));
        Assert.Equal([2], AugmentRecommendationGroups.Select(stats, names, 1).Select(item => item.Id));
        Assert.Equal([4], AugmentRecommendationGroups.Select(stats, names, 2).Select(item => item.Id));
    }

    [Fact]
    public void RecommendationLimitIsAppliedAfterFilteringType()
    {
        var stats = Enumerable.Range(1, 30).Select(id => new OpggAugmentRecommendation(id, id <= 20 ? 2 : 0, 100, 100 - id));
        var silver = AugmentRecommendationGroups.Select(stats, new Dictionary<int, AugmentInfo>(), 0, 3);
        Assert.Equal([21, 22, 23], silver.Select(item => item.Id));
    }

    [Fact]
    public void UnknownRarityIsNotAssignedToSilver()
    {
        var names = new Dictionary<int, AugmentInfo> { [99999] = new(99999, "新增幅", null) };
        Assert.Empty(AugmentRecommendationGroups.Select([new(99999, -1, 100, 80)], names, 0));
    }
}
