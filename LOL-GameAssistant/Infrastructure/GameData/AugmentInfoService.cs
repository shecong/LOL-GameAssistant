using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>将内置强化目录转换为应用层展示信息。</summary>
public sealed class AugmentInfoService : IAugmentInfoService
{
    /// <summary>解析给定标识对应的展示信息。</summary>
    public async Task<IReadOnlyList<AugmentInfo>> ResolveAsync(IEnumerable<int> ids) =>
        (await AugmentCatalog.ResolveAsync(ids).ConfigureAwait(false))
        .Select(item => new AugmentInfo(item.Id, item.Name, item.IconUrl, item.EnglishName, item.Rarity)).ToArray();
}
