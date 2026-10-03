using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>版本隔离的本地只读数据缓存；合并重复请求，失败不落盘。</summary>
internal sealed class LocalDataCache<T> where T : class
{
    private sealed record Entry(DateTimeOffset SavedAt, T Value);
    private readonly string _directory;
    private readonly TimeSpan _ttl;
    private readonly Func<T, bool> _valid;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Entry> _memory = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<T?>>> _pending = new();

    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "cache", "v1");

    public LocalDataCache(string directory, TimeSpan ttl, Func<T, bool> valid, TimeProvider? clock = null)
    {
        _directory = directory;
        _ttl = ttl;
        _valid = valid;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<T?> GetAsync(string key, Func<Task<T?>> load, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_memory.TryGetValue(key, out var entry) && Fresh(entry)) return Task.FromResult<T?>(entry.Value);
        // 调用方取消只结束自己的等待，不会取消其他窗口共享的下载。
        var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<T?>>(() => ReadOrLoadAsync(key, load)));
        return pending.Value.WaitAsync(cancellationToken);
    }

    private bool Fresh(Entry entry) => entry.SavedAt <= _clock.GetUtcNow() &&
        _clock.GetUtcNow() - entry.SavedAt < _ttl && entry.Value != null && _valid(entry.Value);

    private async Task<T?> ReadOrLoadAsync(string key, Func<Task<T?>> load)
    {
        string file = Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
        try
        {
            try
            {
                await using var stream = File.OpenRead(file);
                if (stream.Length <= 8 * 1024 * 1024)
                {
                    var entry = await JsonSerializer.DeserializeAsync<Entry>(stream).ConfigureAwait(false);
                    if (entry != null && Fresh(entry))
                    {
                        Remember(key, entry);
                        return entry.Value;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

            T? value = await load().ConfigureAwait(false);
            if (value == null || !_valid(value)) return value;
            var saved = new Entry(_clock.GetUtcNow(), value);
            Remember(key, saved);
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(_directory);
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(saved)).ConfigureAwait(false);
                File.Move(temporary, file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return value;
        }
        finally { _pending.TryRemove(key, out _); }
    }

    private void Remember(string key, Entry entry)
    {
        if (_memory.Count >= 512) _memory.Clear();
        _memory[key] = entry;
    }
}
