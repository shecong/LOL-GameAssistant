using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Application.Skins;

public sealed record ChampionSkinResource(int SkinNumber, string Name, IReadOnlyList<string> PosterPaths);

/// <summary>与当前客户端版本、语言一致的皮肤名称及选肤海报。</summary>
public interface IChampionSkinResourceService
{
    Task<IReadOnlyList<ChampionSkinResource>> GetCatalogAsync(string model, CancellationToken cancellationToken = default);
    Task<GameAsset?> GetPosterAsync(ChampionSkinResource skin, CancellationToken cancellationToken = default);
}
