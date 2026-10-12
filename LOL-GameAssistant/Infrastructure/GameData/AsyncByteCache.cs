using System.Collections.Concurrent;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>共享成功下载；失败不永久缓存，单个等待者取消不影响其它等待者。</summary>
internal sealed class AsyncByteCache(Func<string, Task<byte[]?>> download)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _entries = new(StringComparer.Ordinal);
    internal void Clear() => _entries.Clear();
    internal (int Entries, long Bytes) MeasureMemory()
    {
        var bytes = _entries.Values.Where(entry => entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
            .Select(entry => entry.Value.Result).Where(value => value is { Length: > 0 }).ToArray();
        return (bytes.Length, bytes.Sum(value => (long)value!.Length));
    }
    internal async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Lazy<Task<byte[]?>>? candidate = null;
        candidate = new Lazy<Task<byte[]?>>(() => DownloadAsync(key, candidate!));
        var entry = _entries.GetOrAdd(key, candidate);
        Task<byte[]?> task = entry.Value;
        try { return await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (task.IsFaulted || task.IsCanceled || task.IsCompletedSuccessfully && task.Result is not { Length: > 0 })
                ((ICollection<KeyValuePair<string, Lazy<Task<byte[]?>>>>)_entries).Remove(new(key, entry));
        }
    }

    private async Task<byte[]?> DownloadAsync(string key, Lazy<Task<byte[]?>> entry)
    {
        byte[]? bytes = null;
        try { return bytes = await download(key).ConfigureAwait(false); }
        finally
        {
            if (bytes is not { Length: > 0 })
                ((ICollection<KeyValuePair<string, Lazy<Task<byte[]?>>>>)_entries).Remove(new(key, entry));
        }
    }
}
