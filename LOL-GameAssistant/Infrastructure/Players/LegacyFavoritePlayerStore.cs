using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Infrastructure.Players;

/// <summary>兼容既有 favorites.json 格式的收藏列表存储适配器。</summary>
public sealed class LegacyFavoritePlayerStore : IFavoritePlayerStore
{
    /// <summary>读取当前模块使用的持久化或内置数据。</summary>
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

    /// <summary>将当前模块的数据保存到持久化存储。</summary>
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