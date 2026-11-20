namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>共享下载并限制内存占用，单个等待者取消不影响其他等待者。</summary>
internal sealed class AsyncByteCache(Func<string, Task<byte[]?>> download, long maxBytes = 64 * 1024 * 1024)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lazy<Task<byte[]?>>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte[] Bytes, long Access)> _entries = new(StringComparer.Ordinal);
    private long _bytes;
    private long _access;

    internal void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _entries.Clear();
            _bytes = 0;
        }
    }

    internal (int Entries, long Bytes) MeasureMemory()
    {
        lock (_gate) return (_entries.Count, _bytes);
    }

    internal Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Lazy<Task<byte[]?>>? pending;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _entries[key] = (entry.Bytes, ++_access);
                return Task.FromResult<byte[]?>(entry.Bytes);
            }
            if (!_pending.TryGetValue(key, out pending))
            {
                pending = new(() => DownloadAsync(key, pending!));
                _pending.Add(key, pending);
            }
        }
        return pending.Value.WaitAsync(cancellationToken);
    }

    private async Task<byte[]?> DownloadAsync(string key, Lazy<Task<byte[]?>> pending)
    {
        byte[]? bytes = null;
        try { return bytes = await download(key).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(key, out var active) && active == pending)
                {
                    _pending.Remove(key);
                    if (bytes is { Length: > 0 } && bytes.Length <= maxBytes)
                    {
                        while (_bytes + bytes.Length > maxBytes)
                        {
                            var oldest = _entries.MinBy(entry => entry.Value.Access);
                            _entries.Remove(oldest.Key);
                            _bytes -= oldest.Value.Bytes.Length;
                        }
                        _entries[key] = (bytes, ++_access);
                        _bytes += bytes.Length;
                    }
                }
            }
        }
    }
}
