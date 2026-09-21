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
    public Task<GameAsset?> GetChampionIconAsync(int championId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameYXImg(championId), cancellationToken);

    public Task<GameAsset?> GetItemIconAsync(int itemId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameZBImg(itemId.ToString()), cancellationToken);

    public Task<GameAsset?> GetSummonerSpellIconAsync(int spellId, CancellationToken cancellationToken = default) =>
        CopyAssetAsync(() => Game_Api.GetGameZHSJNImg(spellId.ToString()), cancellationToken);

    public Task<string?> GetItemNameAsync(int itemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetItemNameAsync(itemId);
    }

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
