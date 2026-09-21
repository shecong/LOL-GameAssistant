using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Application.GameData;

/// <summary>
/// 提供英雄、装备与召唤师技能的展示资源。
/// 应用层返回二进制内容，WinForms 图片解码仅发生在表现层。
/// </summary>
public interface IGameAssetService
{
    Task<GameAsset?> GetChampionIconAsync(int championId, CancellationToken cancellationToken = default);

    Task<GameAsset?> GetItemIconAsync(int itemId, CancellationToken cancellationToken = default);

    Task<GameAsset?> GetSummonerSpellIconAsync(int spellId, CancellationToken cancellationToken = default);

    Task<string?> GetItemNameAsync(int itemId, CancellationToken cancellationToken = default);

    Task<string?> GetSummonerSpellNameAsync(int spellId, CancellationToken cancellationToken = default);
}
