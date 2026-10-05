using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>跨窗口、跨重启复用图标与名称；缓存按游戏版本隔离。</summary>
internal sealed class CachedGameAssetService : IGameAssetService
{
    private readonly IGameAssetService _inner;
    private readonly Func<string> _version;
    private readonly LocalDataCache<GameAsset> _icons;
    private readonly LocalDataCache<string> _names;

    public CachedGameAssetService(IGameAssetService inner, Func<string> version, string? directory = null)
    {
        _inner = inner;
        _version = version;
        string root = directory ?? Path.Combine(LocalDataCache<GameAsset>.Root, "assets");
        _icons = new(root, TimeSpan.FromDays(30), asset => asset.Content is { Length: > 0 });
        _names = new(Path.Combine(root, "names"), TimeSpan.FromDays(30), name => !string.IsNullOrWhiteSpace(name));
    }

    private string Key(string kind, int id) => $"{_version()}:{kind}:{id}";
    public Task<string?> GetItemDescriptionAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("item-description-zh-CN", id), () => _inner.GetItemDescriptionAsync(id), token);
    public Task<string?> GetRuneDetailsAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("rune-details-zh-CN", id), () => _inner.GetRuneDetailsAsync(id), token);
    public Task<GameAsset?> GetChampionIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("champion", id), () => _inner.GetChampionIconAsync(id), token);
    public Task<GameAsset?> GetItemIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("item", id), () => _inner.GetItemIconAsync(id), token);
    public Task<GameAsset?> GetRuneIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("rune", id), () => _inner.GetRuneIconAsync(id), token);
    public Task<GameAsset?> GetSummonerSpellIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("spell", id), () => _inner.GetSummonerSpellIconAsync(id), token);
    public Task<string?> GetItemNameAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("item-name-zh-CN", id), () => _inner.GetItemNameAsync(id), token);
    public Task<string?> GetSummonerSpellNameAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("spell-name-zh-CN", id), () => _inner.GetSummonerSpellNameAsync(id), token);
}
