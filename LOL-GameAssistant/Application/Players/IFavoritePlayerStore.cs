using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Application.Players;

/// <summary>收藏召唤师列表的持久化端口。</summary>
public interface IFavoritePlayerStore
{
    /// <summary>读取当前模块使用的持久化或内置数据。</summary>
    List<FavoritePlayer> Load();

    /// <summary>将当前模块的数据保存到持久化存储。</summary>
    void Save(List<FavoritePlayer> favorites);
}