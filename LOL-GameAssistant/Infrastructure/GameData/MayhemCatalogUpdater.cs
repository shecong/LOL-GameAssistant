using LOL_GameAssistant.Helper;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>以 ETag 和内容摘要检查增幅表版本，校验后原子替换本地缓存与内存快照。</summary>
internal sealed class MayhemCatalogUpdater
{
    internal const string Source = "https://hextech.dtodo.cn/data/aram-mayhem-augments.zh_cn.json";
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly HttpClient SharedHttp = new(NetworkTrafficService.Handler(NetworkModule.Augments)) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _http;
    private readonly string _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private Task? _refresh;
    private DateTimeOffset _nextCheck;
    private Snapshot? _current;
    internal Action<string>? Diagnostic { get; set; }
    internal sealed record Entry(int Id, string Name, int Rarity, string Description);
    private sealed record Cache(string Version, string? ETag, string Payload);
    private sealed record Snapshot(Cache Cache, IReadOnlyDictionary<int, Entry> Entries);
    internal IReadOnlyDictionary<int, Entry> Entries => Volatile.Read(ref _current)?.Entries ?? Empty;
    private static readonly IReadOnlyDictionary<int, Entry> Empty = new Dictionary<int, Entry>();
    internal string? Version => Volatile.Read(ref _current)?.Cache.Version;

    internal MayhemCatalogUpdater(string file, HttpClient? http = null, Func<DateTimeOffset>? now = null)
    {
        _file = file;
        _http = http ?? SharedHttp;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > MaxBytes * 2) return;
            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(file));
            if (cache == null || cache.Version != Hash(cache.Payload)) return;
            _current = new(cache, Parse(cache.Payload));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { }
    }

    /// <summary>并发调用共享一次检查；调用方取消等待不会取消其他调用方的更新。</summary>
    internal Task RefreshAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_refresh is { IsCompleted: false }) return _refresh.WaitAsync(token);
            if (_now() < _nextCheck) return Task.CompletedTask;
            _refresh = Task.Run(CheckAsync);
            return _refresh.WaitAsync(token);
        }
    }

    private async Task CheckAsync()
    {
        bool success = false;
        try
        {
            var previous = Volatile.Read(ref _current);
            using var request = new HttpRequestMessage(HttpMethod.Get, Source);
            if (EntityTagHeaderValue.TryParse(previous?.Cache.ETag, out var etag))
                request.Headers.IfNoneMatch.Add(etag);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (previous == null) throw new InvalidDataException("服务器返回未修改，但本机没有有效缓存");
                Diagnostic?.Invoke($"对照表已是最新版本 {previous.Cache.Version[..12]}");
                success = true;
                return;
            }
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(MaxBytes, timeout.Token).ConfigureAwait(false);
            string payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var entries = Parse(payload);
            var cache = new Cache(Hash(payload), response.Headers.ETag?.ToString(), payload);
            var snapshot = new Snapshot(cache, entries);
            // Publish only a completely validated snapshot. Preserve the update in memory if disk is read-only.
            Volatile.Write(ref _current, snapshot);
            if (previous?.Cache != cache)
            {
                try { await SaveAsync(cache, timeout.Token).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
                { Diagnostic?.Invoke("对照表已更新到内存，本地缓存写入失败；下次启动将重新检查"); }
            }
            success = true;
            Diagnostic?.Invoke(previous?.Cache.Version == cache.Version
                ? $"对照表已是最新版本 {cache.Version[..12]}"
                : $"已自动更新对照表 {cache.Version[..12]}，共 {entries.Count} 条增幅");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidDataException or OperationCanceledException)
        { Diagnostic?.Invoke($"更新检查失败，继续使用本地或内置对照表：{e.GetType().Name}"); }
        finally
        {
            lock (_gate) _nextCheck = _now() + (success ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(5));
        }
    }

    private async Task SaveAsync(Cache cache, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        string temp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(cache), token).ConfigureAwait(false);
            File.Move(temp, _file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static IReadOnlyDictionary<int, Entry> Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("增幅表必须按 ID 索引");
        var entries = new Dictionary<int, Entry>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var item = property.Value;
            if (!int.TryParse(property.Name, out int id) || id < 1000 || item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.Number || !idValue.TryGetInt32(out int sourceId) || sourceId != id ||
                !item.TryGetProperty("displayName", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
                !item.TryGetProperty("rarity", out var rarity) || rarity.ValueKind != JsonValueKind.Number || !rarity.TryGetInt32(out int tier) || tier is < 0 or > 2 ||
                !item.TryGetProperty("description", out var description) || description.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(description.GetString()) ||
                !entries.TryAdd(id, new(id, name.GetString()!, tier, description.GetString()!)))
                throw new InvalidDataException($"增幅条目无效：{property.Name}");
        }
        if (entries.Count < 200) throw new InvalidDataException("增幅表不完整");
        return entries;
    }
}
