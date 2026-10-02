using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.Application.Builds;

public static class AugmentRecommendationGroups
{
    public static IReadOnlyList<OpggAugmentRecommendation> Select(
        IEnumerable<OpggAugmentRecommendation> recommendations,
        IReadOnlyDictionary<int, AugmentInfo> names, int rarity, int limit = 12) =>
        recommendations.Where(item => (names.GetValueOrDefault(item.Id)?.Rarity ?? item.Rarity) == rarity)
            .OrderByDescending(item => item.WinRate).ThenByDescending(item => item.Matches)
            .DistinctBy(item => item.Id).Take(limit).ToArray();
}
