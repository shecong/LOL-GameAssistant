using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Infrastructure.Players;

/// <summary>兼容既有 favorites.json 格式的收藏列表存储适配器。</summary>
public sealed class LegacyFavoritePlayerStore : IFavoritePlayerStore
{
    public List<FavoritePlayer> Load() => Entity.FavoriteStore.Load()
        .Select(item => new FavoritePlayer
        {
            Puuid = item.Puuid,
            GameName = item.GameName,
            TagLine = item.TagLine,
            SummonerLevel = item.SummonerLevel,
            AddedAt = item.AddedAt
        })
        .ToList();

    public void Save(List<FavoritePlayer> favorites) => Entity.FavoriteStore.Save(favorites
        .Select(item => new Entity.FavoritePlayer
        {
            Puuid = item.Puuid,
            GameName = item.GameName,
            TagLine = item.TagLine,
            SummonerLevel = item.SummonerLevel,
            AddedAt = item.AddedAt
        })
        .ToList());
}
