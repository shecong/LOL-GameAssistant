using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>
/// 游戏本地资源的基础设施适配器。
/// 它负责关闭底层流并复制内容，调用方永远不会持有 LCU 响应流。
/// </summary>
public sealed class LegacyGameAssetService : IGameAssetService
{
    /// <summary>读取指定英雄的展示图标。</summary>
    public Task<GameAsset?> GetChampionIconAsync(int championId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameYXImg(championId), cancellationToken);

    /// <summary>读取指定装备的展示图标。</summary>
    public Task<GameAsset?> GetItemIconAsync(int itemId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameZBImg(itemId.ToString()), cancellationToken);

    /// <summary>读取指定符文的展示图标。</summary>
    public Task<GameAsset?> GetRuneIconAsync(int perkId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameRuneImg(perkId), cancellationToken);

    /// <summary>读取指定召唤师技能的展示图标。</summary>
    public Task<GameAsset?> GetSummonerSpellIconAsync(int spellId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameZHSJNImg(spellId.ToString()), cancellationToken);

    /// <summary>读取指定装备的展示名称。</summary>
    public Task<string?> GetItemNameAsync(int itemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetItemNameAsync(itemId);
    }

    /// <summary>读取指定召唤师技能的展示名称。</summary>
    public Task<string?> GetSummonerSpellNameAsync(int spellId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetSpellNameAsync(spellId);
    }

    /// <summary>根据装备标识读取作用说明。</summary>
    public Task<string?> GetItemDescriptionAsync(int itemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetItemDescriptionAsync(itemId);
    }

    /// <summary>根据符文标识读取名称及详细作用文本。</summary>
    public Task<string?> GetRuneDetailsAsync(int perkId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetRuneDetailsAsync(perkId);
    }

    /// <summary>复制资源流为独立二进制内容，并在完成后关闭原始流。</summary>
    private static async Task<GameAsset?> CopyAssetAsync(
        Func<Task<Stream>> loadStream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Stream stream = await loadStream().ConfigureAwait(false);
        if (stream == Stream.Null) return null;

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.Length == 0 ? null : new GameAsset(buffer.ToArray());
    }
}
