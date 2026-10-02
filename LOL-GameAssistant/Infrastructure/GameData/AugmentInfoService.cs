using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.Infrastructure.GameData;

public sealed class AugmentInfoService : IAugmentInfoService
{
    public async Task<IReadOnlyList<AugmentInfo>> ResolveAsync(IEnumerable<int> ids) =>
        (await AugmentCatalog.ResolveAsync(ids).ConfigureAwait(false))
        .Select(item => new AugmentInfo(item.Id, item.Name, item.IconUrl, item.EnglishName, item.Rarity)).ToArray();
}
