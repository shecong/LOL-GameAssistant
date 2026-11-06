namespace LOL_GameAssistant.Application.Skins;

public sealed record FileSkinPreferences
{
    public HashSet<string> Favorites { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> Presets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
public interface IFileSkinPreferencesStore
{
    FileSkinPreferences Load();
    void Save(FileSkinPreferences preferences);
}

/// <summary>Selections contain real catalog entries; translated titles never become package keys.</summary>
public sealed class FileSkinBrowserModel
{
    private readonly Dictionary<string, CslolSkinEntry> _selected = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CslolSkinEntry> Catalog { get; private set; } = [];
    public IReadOnlyList<CslolSkinEntry> Selected => _selected.Values.ToArray();
    public void SetCatalog(IReadOnlyList<CslolSkinEntry> entries)
    {
        Catalog = entries;
        var available = entries.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _selected.ToArray())
            if (available.TryGetValue(pair.Value.FileName, out var current)) _selected[pair.Key] = current;
            else _selected.Remove(pair.Key);
    }
    public bool IsSelected(CslolSkinEntry entry) => _selected.TryGetValue(entry.ChampionKey, out var pick) && pick.FileName == entry.FileName;
    public void Toggle(CslolSkinEntry entry)
    {
        if (!Catalog.Contains(entry)) throw new InvalidOperationException("皮肤不属于当前目录");
        if (IsSelected(entry)) _selected.Remove(entry.ChampionKey); else _selected[entry.ChampionKey] = entry;
    }
    public void Clear() => _selected.Clear();
    public void Restore(IEnumerable<string> files)
    {
        var byFile = Catalog.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        _selected.Clear();
        foreach (var file in files) if (byFile.TryGetValue(file, out var entry)) _selected[entry.ChampionKey] = entry;
    }
    public IReadOnlyList<CslolSkinEntry> Filter(string? champion, Func<CslolSkinEntry, bool> matches,
        ISet<string> favorites, bool favoritesOnly, bool selectedOnly, bool showChromas) => Catalog
        .Where(e => (champion is null || e.ChampionKey.Equals(champion, StringComparison.OrdinalIgnoreCase))
            && matches(e) && (!favoritesOnly || favorites.Contains(e.FileName)) && (!selectedOnly || IsSelected(e))
            && (showChromas || e.ChromaId is null)).ToArray();
}
