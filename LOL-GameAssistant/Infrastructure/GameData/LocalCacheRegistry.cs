namespace LOL_GameAssistant.Infrastructure.GameData;

internal interface ILocalCacheStore
{
    string DirectoryPath { get; }
    (int Entries, long Bytes) MeasureMemory();
    void Clear();
}

internal static class LocalCacheRegistry
{
    private static readonly object Sync = new();
    private static readonly List<WeakReference<ILocalCacheStore>> Stores = [];
    internal static void Register(ILocalCacheStore store)
    {
        lock (Sync)
        {
            Stores.RemoveAll(reference => !reference.TryGetTarget(out _));
            Stores.Add(new(store));
        }
    }
    internal static ILocalCacheStore[] Find(string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        lock (Sync)
            return Stores.Select(reference => reference.TryGetTarget(out var store) ? store : null)
                .Where(store => store != null && (store.DirectoryPath.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                    store.DirectoryPath.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Cast<ILocalCacheStore>().ToArray();
    }
}
