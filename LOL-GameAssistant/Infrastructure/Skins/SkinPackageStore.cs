using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>
/// 读取 PLskin 的 skins.plskin 加密包，按需解密单个条目。
/// 包格式：
///   0x00  "PLSKINPK"
///   0x08  uint32 数据区长度
///   0x0C  数据区（条目 offset 相对 0x0C）
///   尾部  JSON { version, patch, files:[{fileName,offset,encryptedSize,size,nonce,tag}] }
/// 每条 = AES-256-GCM(packageKey, nonce, tag)，明文是 fantome(zip)。
/// </summary>
public sealed class SkinPackageStore : IDisposable
{
    private readonly string _packagePath;
    private FileStream? _stream;
    private PackageHeader? _header;
    private readonly Dictionary<string, PackageEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _key;
    private uint _dataLength;

    public SkinPackageStore(string packagePath, string packageKeyBase64)
    {
        _packagePath = packagePath;
        _key = Convert.FromBase64String(packageKeyBase64);
        if (_key.Length != 32) throw new ArgumentException("packageKey 必须是 32 字节", nameof(packageKeyBase64));
    }

    public bool Exists => File.Exists(_packagePath);

    public IReadOnlyDictionary<string, PackageEntry> Entries
    {
        get { EnsureOpen(); return _byName; }
    }

    public PackageEntry? Find(string fileName)
        => Entries.TryGetValue(fileName, out var e) ? e : null;

    private void EnsureOpen()
    {
        if (_header is not null) return;
        if (!File.Exists(_packagePath)) throw new FileNotFoundException("皮肤包不存在", _packagePath);

        _stream = File.OpenRead(_packagePath);
        Span<byte> head = stackalloc byte[12];
        _stream.ReadExactly(head);

        var magic = Encoding.ASCII.GetString(head[..8]);
        if (magic != "PLSKINPK") throw new InvalidDataException($"皮肤包标识不符: {magic}");

        var dataLength = BitConverter.ToUInt32(head[8..12]);
        _dataLength = dataLength;
        var jsonStart = 12L + dataLength;
        var jsonLength = _stream.Length - jsonStart;
        if (jsonLength <= 0 || jsonLength > 64L * 1024 * 1024)
            throw new InvalidDataException($"皮肤包头长度异常: {jsonLength}");

        _stream.Position = jsonStart;
        var json = new byte[jsonLength];
        _stream.ReadExactly(json);

        var header = JsonSerializer.Deserialize<PackageHeader>(json, JsonOptions)
                  ?? throw new InvalidDataException("皮肤包头解析失败");

        _byName.Clear();
        foreach (var f in header.Files)
        {
            if (f.Offset < 0 || f.Size <= 0 || f.Size > 256 * 1024 * 1024 || f.EncryptedSize != f.Size
                || f.Offset > _dataLength || f.EncryptedSize > _dataLength - f.Offset
                || Convert.FromBase64String(f.Nonce).Length != 12 || Convert.FromBase64String(f.Tag).Length != 16
                || string.IsNullOrWhiteSpace(f.FileName) || !_byName.TryAdd(f.FileName, f))
                throw new InvalidDataException("加密包条目范围、认证数据或名称无效");
        }
        _header = header;
    }

    /// <summary>把单个条目解密并写到目标路径（覆盖）。返回明文长度。</summary>
    public async Task<long> ExtractAsync(PackageEntry entry, string destinationPath, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (!_byName.TryGetValue(entry.FileName, out var known) || known != entry)
            throw new InvalidDataException("条目不属于当前皮肤包");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var cipher = new byte[entry.EncryptedSize];
        await using var input = File.OpenRead(_packagePath);
        input.Position = 12L + entry.Offset;
        await input.ReadExactlyAsync(cipher, cancellationToken).ConfigureAwait(false);

        var nonce = Convert.FromBase64String(entry.Nonce);
        var tag = Convert.FromBase64String(entry.Tag);
        var plain = new byte[entry.Size];

        using (var aes = new AesGcm(_key, 16))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        var temp = destinationPath + "." + Guid.NewGuid().ToString("N") + ".decrypting";
        try
        {
            await File.WriteAllBytesAsync(temp, plain, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, destinationPath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temp)) File.Delete(temp); }
        return plain.Length;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _header = null;
        CryptographicOperations.ZeroMemory(_key);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public sealed record PackageHeader
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("patch")] public string? Patch { get; init; }
        [JsonPropertyName("files")] public List<PackageEntry> Files { get; init; } = [];
    }

    public sealed record PackageEntry
    {
        [JsonPropertyName("fileName")] public string FileName { get; init; } = "";
        [JsonPropertyName("offset")] public long Offset { get; init; }
        [JsonPropertyName("encryptedSize")] public int EncryptedSize { get; init; }
        [JsonPropertyName("size")] public int Size { get; init; }
        [JsonPropertyName("nonce")] public string Nonce { get; init; } = "";
        [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    }
}
