using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.Application.Builds;

/// <summary>按强化稀有度分组并排序推荐条目。</summary>
public static class AugmentRecommendationGroups
{
    /// <summary>按稀有度筛选强化，依次按胜率和样本量排序，去重并限制展示数量。</summary>
    public static IReadOnlyList<OpggAugmentRecommendation> Select(
        IEnumerable<OpggAugmentRecommendation> recommendations,
        IReadOnlyDictionary<int, AugmentInfo> names, int rarity, int limit = 12) =>
        recommendations.Where(item => (names.GetValueOrDefault(item.Id)?.Rarity ?? item.Rarity) == rarity)
            .OrderByDescending(item => item.WinRate).ThenByDescending(item => item.Matches)
            .DistinctBy(item => item.Id).Take(limit).ToArray();
}
