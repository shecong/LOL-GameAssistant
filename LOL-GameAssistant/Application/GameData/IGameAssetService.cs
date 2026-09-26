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

    /// <summary>获取符文（基石、普通符文或属性碎片）的展示图标；未找到时返回 null。</summary>
    Task<GameAsset?> GetRuneIconAsync(int perkId, CancellationToken cancellationToken = default);

    Task<GameAsset?> GetSummonerSpellIconAsync(int spellId, CancellationToken cancellationToken = default);

    Task<string?> GetItemNameAsync(int itemId, CancellationToken cancellationToken = default);

    Task<string?> GetSummonerSpellNameAsync(int spellId, CancellationToken cancellationToken = default);
}