using LOL_GameAssistant.Domain.GameData;
namespace LOL_GameAssistant.Application.Skins;
public interface IFileSkinArtworkService
{
    Task<GameAsset?> GetChampionAsync(CslolSkinEntry entry, CancellationToken token);
    Task<GameAsset?> GetSkinAsync(CslolSkinEntry entry, CancellationToken token);
}
