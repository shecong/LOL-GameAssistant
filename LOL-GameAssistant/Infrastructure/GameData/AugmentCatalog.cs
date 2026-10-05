using System.Collections.Concurrent;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>竞技场和海克斯大乱斗强化 ID 的离线中文名称表。</summary>
internal static class AugmentCatalog
{
    private const string ResourceName = "LOL_GameAssistant.Resources.AugmentNames.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Lazy<IReadOnlyDictionary<int, AugmentDisplay>> Catalog = new(Load);
    private static readonly Lazy<Dictionary<string, string>> Descriptions = new(() =>
    {
        using Stream? stream = typeof(AugmentCatalog).Assembly.GetManifestResourceStream("LOL_GameAssistant.Resources.AugmentDescriptions.json");
        return stream == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    });

    /// <summary>按强化标识读取内置作用说明。</summary>
    internal static string? GetDescription(int id) => Descriptions.Value.GetValueOrDefault(id.ToString());
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> IconCache = new(StringComparer.Ordinal);

    /// <summary>内置强化目录中的名称、图标和稀有度条目。</summary>
    internal sealed record AugmentDisplay(int Id, string Name, string? IconUrl, string? EnglishName = null, int? Rarity = null);

    /// <summary>解析给定标识对应的展示信息。</summary>
    public static Task<IReadOnlyList<AugmentDisplay>> ResolveAsync(IEnumerable<int> ids)
    {
        IReadOnlyDictionary<int, AugmentDisplay> catalog = Catalog.Value;
        IReadOnlyList<AugmentDisplay> result = ids.Where(id => id > 0)
            .Select(id => catalog.TryGetValue(id, out AugmentDisplay? display)
                ? display
                : new AugmentDisplay(id, $"未知强化 #{id}", null))
            .ToArray();
        return Task.FromResult(result);
    }

    /// <summary>返回当前目录中的全部条目。</summary>
    public static IReadOnlyList<AugmentDisplay> GetAll() => Catalog.Value.Values.ToArray();

    /// <summary>读取指定地址的图标并复用已缓存的下载任务。</summary>
    public static Task<byte[]?> GetIconAsync(string? url) =>
        string.IsNullOrWhiteSpace(url)
            ? Task.FromResult<byte[]?>(null)
            : IconCache.GetOrAdd(url, static value => LoadIconAsync(value));

    /// <summary>读取内嵌强化目录并过滤无效标识或缺失名称的条目。</summary>
    private static IReadOnlyDictionary<int, AugmentDisplay> Load()
    {
        using Stream? stream = typeof(AugmentCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null) return new Dictionary<int, AugmentDisplay>();
        using JsonDocument document = JsonDocument.Parse(stream);
        var result = new Dictionary<int, AugmentDisplay>();
        foreach (JsonProperty entry in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out int id) || id <= 0 ||
                !entry.Value.TryGetProperty("name", out JsonElement nameValue)) continue;
            string? name = nameValue.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            string? iconUrl = entry.Value.TryGetProperty("iconUrl", out JsonElement iconValue)
                ? iconValue.GetString() : null;
            string? englishName = entry.Value.TryGetProperty("englishName", out JsonElement englishValue)
                ? englishValue.GetString() : null;
            int? rarity = entry.Value.TryGetProperty("rarity", out JsonElement rarityValue)
                && rarityValue.TryGetInt32(out int value) && value is >= 0 and <= 2 ? value : null;
            result[id] = new AugmentDisplay(id, name, iconUrl, englishName, rarity);
        }
        return result;
    }

    /// <summary>下载图标二进制内容，失败时返回空结果。</summary>
    private static async Task<byte[]?> LoadIconAsync(string url)
    {
        try { return await Http.GetByteArrayAsync(url).ConfigureAwait(false); }
        catch { return null; }
    }
}
