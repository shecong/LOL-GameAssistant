using System.Collections.Concurrent;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>竞技场和海克斯大乱斗强化 ID 的离线中文名称表。</summary>
internal static class AugmentCatalog
{
    private const string ResourceName = "LOL_GameAssistant.Resources.AugmentNames.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Lazy<IReadOnlyDictionary<int, AugmentDisplay>> Catalog = new(Load);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> IconCache = new(StringComparer.Ordinal);

    internal sealed record AugmentDisplay(int Id, string Name, string? IconUrl);

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

    public static Task<byte[]?> GetIconAsync(string? url) =>
        string.IsNullOrWhiteSpace(url)
            ? Task.FromResult<byte[]?>(null)
            : IconCache.GetOrAdd(url, static value => LoadIconAsync(value));

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
            result[id] = new AugmentDisplay(id, name, iconUrl);
        }
        return result;
    }

    private static async Task<byte[]?> LoadIconAsync(string url)
    {
        try { return await Http.GetByteArrayAsync(url).ConfigureAwait(false); }
        catch { return null; }
    }
}