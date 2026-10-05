using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>版本隔离的本地只读数据缓存；合并重复请求，失败不落盘。</summary>
internal sealed class LocalDataCache<T> where T : class
{
    /// <summary>缓存数据及其保存时间，供有效期检查使用。</summary>
    private sealed record Entry(DateTimeOffset SavedAt, T Value);
    private readonly string _directory;
    private readonly TimeSpan _ttl;
    private readonly Func<T, bool> _valid;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Entry> _memory = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<T?>>> _pending = new();

    /// <summary>返回当前用户用于存储游戏数据缓存的根目录。</summary>
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "cache", "v1");

    /// <summary>初始化 LocalDataCache 的实例状态，并保存传入的依赖或数据。</summary>
    public LocalDataCache(string directory, TimeSpan ttl, Func<T, bool> valid, TimeProvider? clock = null)
    {
        _directory = directory;
        _ttl = ttl;
        _valid = valid;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>优先返回有效内存缓存；合并同键加载请求，调用方取消只结束自己的等待。</summary>
    public Task<T?> GetAsync(string key, Func<Task<T?>> load, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_memory.TryGetValue(key, out var entry) && Fresh(entry)) return Task.FromResult<T?>(entry.Value);
        // 调用方取消只结束自己的等待，不会取消其他窗口共享的下载。
        var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<T?>>(() => ReadOrLoadAsync(key, load)));
        return pending.Value.WaitAsync(cancellationToken);
    }

    /// <summary>检查缓存时间和数据有效性，排除过期或时间异常的条目。</summary>
    private bool Fresh(Entry entry) => entry.SavedAt <= _clock.GetUtcNow() &&
        _clock.GetUtcNow() - entry.SavedAt < _ttl && entry.Value != null && _valid(entry.Value);

    /// <summary>优先读取有效磁盘缓存，缺失时加载数据并以临时文件方式保存。</summary>
    private async Task<T?> ReadOrLoadAsync(string key, Func<Task<T?>> load)
    {
        // 缓存键可能包含 URL 或版本分隔符；使用摘要作为文件名，避免非法字符及路径嵌套。
        string file = Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
        try
        {
            try
            {
                await using var stream = File.OpenRead(file);
                // 缓存是可重建数据；异常大的文件不参与反序列化，直接转入重新加载流程。
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
            // 不缓存空响应或不完整结果，防止一次网络失败影响后续窗口查询。
            if (value == null || !_valid(value)) return value;
            var saved = new Entry(_clock.GetUtcNow(), value);
            Remember(key, saved);
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(_directory);
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(saved)).ConfigureAwait(false);
                // 内容完整写入临时文件后再替换正式缓存，减少进程中断留下半份 JSON 的风险。
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
        // 无论读取成功、失败还是抛出异常，都允许同一键后续重新发起加载。
        finally { _pending.TryRemove(key, out _); }
    }

    /// <summary>记录内存缓存条目，并在达到容量限制时清理旧集合。</summary>
    private void Remember(string key, Entry entry)
    {
        if (_memory.Count >= 512) _memory.Clear();
        _memory[key] = entry;
    }
}
