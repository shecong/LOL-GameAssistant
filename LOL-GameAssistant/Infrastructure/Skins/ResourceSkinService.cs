using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Wind-compatible local WAD staging. Does not install or execute the loader.</summary>
public sealed class ResourceSkinService : IResourceSkinService, IDisposable
{
    private const long MaxBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxFiles = 128;
    private const string Prefix = "sc-resource-";
    private readonly string _journal;
    private readonly Func<bool> _gameRunning;
    private readonly Func<bool> _allowed;
    private readonly string? _journalError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ResourceSkinState _state = new("", []);
    public ResourceSkinState State => Volatile.Read(ref _state);
    public string? CleanupError { get; private set; }

    public ResourceSkinService(string journal, Func<bool> gameRunning, Func<bool>? allowed = null, bool monitor = true)
    {
        _journal = Path.GetFullPath(journal); _gameRunning = gameRunning; _allowed = allowed ?? (() => true);
        if (File.Exists(_journal))
        {
            try
            {
                // Fail closed on a damaged journal; do not silently abandon ownership.
                _state = JsonSerializer.Deserialize<ResourceSkinState>(File.ReadAllText(_journal))
                    ?? throw new InvalidDataException("资源包挂载清单为空");
                if (_state.Files == null) throw new InvalidDataException("资源包挂载清单无效");
                foreach (var file in _state.Files)
                {
                    if (file == null) throw new InvalidDataException("资源包挂载清单包含空条目");
                    ValidateOwnedName(file.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                _state = new("", []);
                _journalError = CleanupError = "资源包挂载清单无法读取，请检查 " + _journal + "：" + ex.Message;
            }
        }
        if (monitor) _ = WatchAsync(_lifetime.Token);
    }

    public async Task ApplyAsync(string gameDirectory, IReadOnlyList<string> packages, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        string? staging = null;
        var committed = new List<string>();
        bool saved = false;
        try
        {
            CheckJournal();
            CheckIdle();
            if (!_allowed()) throw new InvalidOperationException("请先切换到资源包换肤模式");
            var game = ResolveGame(gameDirectory);
            if (!File.Exists(Path.Combine(game, "HID.dll")))
                throw new InvalidOperationException("游戏目录缺少 HID.dll，无法加载资源包。此模式需要已有的 Wind 兼容加载组件。");
            if (packages.Count == 0 || packages.Count > MaxFiles) throw new InvalidDataException("请选择 1 至 128 个资源包");
            if (State.Files.Count > 0 && !string.Equals(State.GameDirectory, game, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请先清理上一个游戏目录的挂载");
            staging = Path.Combine(Path.GetTempPath(), "sc-resource-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var prepared = new List<ResourceSkinFile>();
            long total = 0;
            async Task AddAsync(Stream source, string display, string packageName, long length)
            {
                if (length < 1 || length > MaxBytes || total + length > MaxBytes || prepared.Count >= MaxFiles)
                    throw new InvalidDataException("资源包超过限制：最多 128 个 WAD，总计不超过 2 GB");
                total += length;
                // HID chooses the encrypted-package reader by suffix. Never rename it to a plain WAD.
                string name = Prefix + Guid.NewGuid().ToString("N") + PackageExtension(packageName);
                string path = Path.Combine(staging, name);
                await using (var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long copied = 0; int count;
                    while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        copied += count;
                        if (copied > length) throw new InvalidDataException("资源包解压长度与声明不符");
                        await dest.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    }
                    if (copied != length) throw new InvalidDataException("资源包已截断");
                }
                uint entries = ValidatePackage(path, packageName);
                prepared.Add(new(name, display, Hash(path), length, entries));
            }
            foreach (string path in packages)
            {
                token.ThrowIfCancellationRequested();
                if (IsWad(path))
                {
                    await using var source = File.OpenRead(path);
                    await AddAsync(source, Path.GetFileName(path), path, source.Length).ConfigureAwait(false);
                }
                else if (path.EndsWith(".fantome", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = ZipFile.OpenRead(path);
                    int before = prepared.Count;
                    foreach (var entry in zip.Entries.Where(e => IsWad(e.FullName)))
                    {
                        // Archive names are display text only, never extraction paths.
                        await using var source = entry.Open();
                        await AddAsync(source, Path.GetFileName(path) + " / " + entry.FullName, entry.FullName, entry.Length).ConfigureAwait(false);
                    }
                    if (before == prepared.Count) throw new InvalidDataException(Path.GetFileName(path) + " 中没有 WAD 资源");
                }
                else throw new InvalidDataException("仅支持 .wad.client、.riftmod.client、.fantome、.zip");
            }
            CheckIdle();
            if (!_allowed()) throw new InvalidOperationException("换肤模式已发生变化，请重试");
            var mount = Mount(game); EnsureNoLinks(game, mount); Directory.CreateDirectory(mount);
            foreach (var file in prepared)
            {
                token.ThrowIfCancellationRequested(); CheckIdle();
                var target = Path.Combine(mount, file.Name);
                // Copy to a sibling first so the final rename is on the same volume.
                var temp = target + ".tmp";
                committed.Add(temp);
                File.Copy(Path.Combine(staging, file.Name), temp, false);
                File.Move(temp, target); committed.Add(target);
            }
            var previous = State;
            var next = new ResourceSkinState(game, prepared.AsReadOnly());
            var pendingCleanup = next with { Files = next.Files.Concat(previous.Files).ToArray() };
            Save(pendingCleanup); Volatile.Write(ref _state, pendingCleanup); saved = true;
            // Keep old files tracked until deletion succeeds, including modified files.
            var retained = DeleteOwned(previous);
            if (retained.Count > 0)
            {
                next = next with { Files = next.Files.Concat(retained).ToArray() };
                Save(next); Volatile.Write(ref _state, next);
                throw new IOException("新资源已挂载，部分旧资源被修改或占用，请检查后清理");
            }
            Save(next); Volatile.Write(ref _state, next);
            CleanupError = null;
        }
        finally
        {
            if (!saved) foreach (var path in committed) { try { File.Delete(path); } catch (IOException) { } }
            if (staging != null) { try { Directory.Delete(staging, true); } catch (IOException) { } }
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckJournal();
            if (State.Files.Count == 0) return;
            CheckIdle();
            var retained = DeleteOwned(State);
            var next = State with { Files = retained };
            Save(next); Volatile.Write(ref _state, next);
            if (retained.Count > 0) throw new IOException("部分资源已被修改或占用，已保留，请检查后重试");
            CleanupError = null;
        }
        finally { _gate.Release(); }
    }

    private List<ResourceSkinFile> DeleteOwned(ResourceSkinState state)
    {
        var retained = new List<ResourceSkinFile>();
        if (state.Files.Count == 0) return retained;
        var game = ResolveGame(state.GameDirectory); var mount = Mount(game); EnsureNoLinks(game, mount);
        foreach (var file in state.Files)
        {
            ValidateOwnedName(file.Name); CheckIdle();
            var path = Path.Combine(mount, file.Name);
            try
            {
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Hash(path) != file.Sha256) { retained.Add(file); continue; }
                File.Delete(path);
            }
            catch (IOException) { retained.Add(file); }
            catch (UnauthorizedAccessException) { retained.Add(file); }
        }
        return retained;
    }
    private async Task WatchAsync(CancellationToken token)
    {
        bool seen = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    bool running = _gameRunning();
                    if (running && State.Files.Count > 0) seen = true;
                    if (!running && seen) { await ClearAsync(token).ConfigureAwait(false); seen = false; }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { CleanupError = ex.Message; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public static string ResolveGame(string path)
    {
        string full = Path.GetFullPath(path);
        if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
        if (!File.Exists(Path.Combine(full, "League of Legends.exe"))) full = Path.Combine(full, "Game");
        if (!File.Exists(Path.Combine(full, "League of Legends.exe"))) throw new DirectoryNotFoundException("请选择包含 League of Legends.exe 的 Game 目录");
        full = ResolveDirectoryAliases(full);
        if (!File.Exists(Path.Combine(full, "League of Legends.exe"))) throw new DirectoryNotFoundException("游戏路径别名的目标无效");
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static string ResolveDirectoryAliases(string path)
    {
        // Accept Wind's ASCII Game aliases, then perform all writes and ownership
        // checks on the real directory. Links inside DATA/FINAL remain forbidden.
        for (int pass = 0; pass < 32; pass++)
        {
            string root = Path.GetPathRoot(path)!;
            string[] parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            bool resolved = false;
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                var directory = new DirectoryInfo(current);
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0) continue;
                var target = directory.ResolveLinkTarget(true) ?? throw new IOException("无法解析游戏路径别名：" + current);
                path = Path.GetFullPath(Path.Combine(new[] { target.FullName }.Concat(parts.Skip(i + 1)).ToArray()));
                resolved = true;
                break;
            }
            if (!resolved) return path;
        }
        throw new IOException("游戏路径别名层级过多，无法安全解析");
    }
    private void CheckIdle() { if (_gameRunning()) throw new InvalidOperationException("请先退出对局，再挂载或清理资源包"); }
    private void CheckJournal() { if (_journalError != null) throw new InvalidDataException(_journalError); }
    private static string Mount(string game) => Path.Combine(game, "DATA", "FINAL", "jinju2");
    private static bool IsWad(string path) => path.EndsWith(".wad.client", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".riftmod.client", StringComparison.OrdinalIgnoreCase);
    private static string PackageExtension(string path) => path.EndsWith(".riftmod.client", StringComparison.OrdinalIgnoreCase) ? ".riftmod.client" : ".wad.client";
    private static void ValidateOwnedName(string name)
    {
        string extension = PackageExtension(name ?? "");
        if (string.IsNullOrEmpty(name) || name.Length != Prefix.Length + 32 + extension.Length || name != Path.GetFileName(name) || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(extension, StringComparison.Ordinal) || !Guid.TryParseExact(name[Prefix.Length..^extension.Length], "N", out _))
            throw new InvalidDataException("挂载清单包含无效的文件名");
    }
    private static void EnsureNoLinks(string game, string target)
    {
        for (var dir = new DirectoryInfo(target); dir != null; dir = dir.Parent)
        {
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("挂载目录不能经过符号链接或目录联接");
        }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private void Save(ResourceSkinState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
        string temp = _journal + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(state)); File.Move(temp, _journal, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static uint ValidatePackage(string path, string packageName)
    {
        if (!packageName.EndsWith(".riftmod.client", StringComparison.OrdinalIgnoreCase)) return ValidateWad(path);
        // Wind accepts nonempty encrypted packages without parsing their opaque payload.
        // Zero entries means unknown, not that an encrypted package has no assets.
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length == 0 || stream.Length > MaxBytes) throw new InvalidDataException("加密资源包为空或超过 2 GB");
            Span<byte> magic = stackalloc byte[3];
            int read = stream.ReadAtLeast(magic, 3, throwOnEndOfStream: false);
            if (read != 3 || magic[0] != 'R' || magic[1] != 'W' || magic[2] != 3) return 0;
        }
        // A plaintext WAD carrying this suffix still has to pass normal structural checks.
        return ValidateWad(path);
    }
    internal static uint ValidateWad(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 272) throw new InvalidDataException("WAD 头部已截断：" + Path.GetFileName(path));
        Span<byte> header = stackalloc byte[272]; stream.ReadExactly(header);
        if (header[0] != 'R' || header[1] != 'W' || header[2] != 3) throw new InvalidDataException("不是有效的 WAD v3：" + Path.GetFileName(path));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[268..]);
        long tableEnd = 272L + count * 32L;
        if (count == 0 || tableEnd > stream.Length) throw new InvalidDataException("WAD 条目表为空或已截断");
        Span<byte> entry = stackalloc byte[32];
        for (uint i = 0; i < count; i++)
        {
            stream.ReadExactly(entry);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset < tableEnd || (long)offset + length > stream.Length) throw new InvalidDataException("WAD 条目超出文件范围");
        }
        return count;
    }
    public void Dispose() { _lifetime.Cancel(); }
}
