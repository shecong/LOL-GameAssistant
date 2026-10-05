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

    /// <summary>初始化 CachedGameAssetService 的实例状态，并保存传入的依赖或数据。</summary>
    public CachedGameAssetService(IGameAssetService inner, Func<string> version, string? directory = null)
    {
        _inner = inner;
        _version = version;
        string root = directory ?? Path.Combine(LocalDataCache<GameAsset>.Root, "assets");
        _icons = new(root, TimeSpan.FromDays(30), asset => asset.Content is { Length: > 0 });
        _names = new(Path.Combine(root, "names"), TimeSpan.FromDays(30), name => !string.IsNullOrWhiteSpace(name));
    }

    /// <summary>组合游戏版本、资源类别和标识，生成缓存键。</summary>
    private string Key(string kind, int id) => $"{_version()}:{kind}:{id}";
    /// <summary>根据装备标识读取作用说明。</summary>
    public Task<string?> GetItemDescriptionAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("item-description-zh-CN", id), () => _inner.GetItemDescriptionAsync(id), token);
    /// <summary>根据符文标识读取名称及详细作用文本。</summary>
    public Task<string?> GetRuneDetailsAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("rune-details-zh-CN", id), () => _inner.GetRuneDetailsAsync(id), token);
    /// <summary>读取指定英雄的展示图标。</summary>
    public Task<GameAsset?> GetChampionIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("champion", id), () => _inner.GetChampionIconAsync(id), token);
    /// <summary>读取指定装备的展示图标。</summary>
    public Task<GameAsset?> GetItemIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("item", id), () => _inner.GetItemIconAsync(id), token);
    /// <summary>读取指定符文的展示图标。</summary>
    public Task<GameAsset?> GetRuneIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("rune", id), () => _inner.GetRuneIconAsync(id), token);
    /// <summary>读取指定召唤师技能的展示图标。</summary>
    public Task<GameAsset?> GetSummonerSpellIconAsync(int id, CancellationToken token = default) =>
        _icons.GetAsync(Key("spell", id), () => _inner.GetSummonerSpellIconAsync(id), token);
    /// <summary>读取指定装备的展示名称。</summary>
    public Task<string?> GetItemNameAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("item-name-zh-CN", id), () => _inner.GetItemNameAsync(id), token);
    /// <summary>读取指定召唤师技能的展示名称。</summary>
    public Task<string?> GetSummonerSpellNameAsync(int id, CancellationToken token = default) =>
        _names.GetAsync(Key("spell-name-zh-CN", id), () => _inner.GetSummonerSpellNameAsync(id), token);
}
