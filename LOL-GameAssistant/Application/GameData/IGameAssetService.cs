using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Application.GameData;

/// <summary>
/// 提供英雄、装备与召唤师技能的展示资源。
/// 应用层返回二进制内容，WinForms 图片解码仅发生在表现层。
/// </summary>
public interface IGameAssetService
{
    /// <summary>读取指定英雄的展示图标。</summary>
    Task<GameAsset?> GetChampionIconAsync(int championId, CancellationToken cancellationToken = default);

    /// <summary>读取指定装备的展示图标。</summary>
    Task<GameAsset?> GetItemIconAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>获取符文（基石、普通符文或属性碎片）的展示图标；未找到时返回 null。</summary>
    Task<GameAsset?> GetRuneIconAsync(int perkId, CancellationToken cancellationToken = default);

    /// <summary>读取指定召唤师技能的展示图标。</summary>
    Task<GameAsset?> GetSummonerSpellIconAsync(int spellId, CancellationToken cancellationToken = default);

    /// <summary>读取指定装备的展示名称。</summary>
    Task<string?> GetItemNameAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>根据装备标识读取作用说明。</summary>
    Task<string?> GetItemDescriptionAsync(int itemId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>根据符文标识读取名称及详细作用文本。</summary>
    Task<string?> GetRuneDetailsAsync(int perkId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>读取指定召唤师技能的展示名称。</summary>
    Task<string?> GetSummonerSpellNameAsync(int spellId, CancellationToken cancellationToken = default);
}
