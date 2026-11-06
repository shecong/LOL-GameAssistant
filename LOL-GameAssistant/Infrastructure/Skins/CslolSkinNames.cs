using System.Globalization;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Display-only Chinese names. Package identities and filenames remain untouched.</summary>
public sealed class CslolSkinNames
{
    private static readonly Lazy<Snapshot> Names = new(() =>
    {
        using var source = typeof(CslolSkinNames).Assembly.GetManifestResourceStream("LOL_GameAssistant.Resources.CslolNamesZhCN.json")
            ?? throw new InvalidOperationException("缺少文件皮肤中文名称资源");
        var snapshot = JsonSerializer.Deserialize<Snapshot>(source, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("文件皮肤中文名称资源无效");
        return snapshot with { Champions = new(snapshot.Champions, StringComparer.OrdinalIgnoreCase) };
    });

    public string Champion(string key) => Names.Value.Champions.GetValueOrDefault(key, key);
    public string Display(CslolSkinEntry entry)
    {
        string id = string.IsNullOrWhiteSpace(entry.ChromaId) ? entry.SkinId.ToString(CultureInfo.InvariantCulture) : entry.ChromaId;
        if (Names.Value.Skins.TryGetValue(id, out var name)) return name;
        // Unknown future chromas retain their distinguishing original label.
        if (entry.ChromaId is not null && Names.Value.Skins.TryGetValue(entry.SkinId.ToString(CultureInfo.InvariantCulture), out var parent))
            return parent + " · " + (entry.ChromaName ?? entry.ChromaId);
        return entry.DisplayName;
    }
    public bool Matches(CslolSkinEntry entry, string query) =>
        $"{Champion(entry.ChampionKey)} {entry.ChampionKey} {Display(entry)} {entry.DisplayName} {entry.SkinId} {entry.ChromaId}"
            .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    public string TranslateStatus(string status, IEnumerable<CslolSkinEntry> selected)
    {
        foreach (var entry in selected.OrderByDescending(e => e.DisplayName.Length))
            status = status.Replace(entry.DisplayName, Display(entry), StringComparison.Ordinal);
        return status;
    }
    private sealed record Snapshot
    {
        public Dictionary<string, string> Champions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Skins { get; init; } = new();
    }
}
