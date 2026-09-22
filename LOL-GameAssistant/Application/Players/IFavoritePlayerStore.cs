using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Application.Players;

/// <summary>收藏召唤师列表的持久化端口。</summary>
public interface IFavoritePlayerStore
{
    List<FavoritePlayer> Load();

    void Save(List<FavoritePlayer> favorites);
}