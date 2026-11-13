using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>
/// Stream-copies game WADs to local storage when the tool cannot map the original volume.
/// Full WADs are required: mkoverlay reads unchanged entry payloads, not just their TOC.
/// This does not supply a game executable or an alternative overlay loading channel.
/// </summary>
public sealed class CslolGameIndexMirror(string cacheRoot)
{
    private sealed record SourceFile(string Path, long Length, long Modified);
    private sealed record CachedFile(SourceFile Source, string Sha256);
    private sealed record Manifest(string Game, SourceFile Executable, CachedFile[] Files);
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "Cslol", "game-index-mirrors");

    public async Task<string> PrepareAsync(string gamePath, Action<string> status, CancellationToken token)
    {
        gamePath = Path.GetFullPath(gamePath);
        var root = Path.GetFullPath(cacheRoot);
        if (root.Equals(gamePath, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(Path.TrimEndingDirectorySeparator(gamePath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("本地索引镜像必须存放在游戏目录之外。");
        token.ThrowIfCancellationRequested();
        var sources = Snapshot(gamePath);
        var executable = Describe(gamePath, Path.Combine(gamePath, "League of Legends.exe"));
        var identity = Hash(JsonSerializer.Serialize(new { Game = gamePath.ToUpperInvariant(), executable, sources }));
        var destination = Path.Combine(root, identity);
        var candidates = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, identity + "*").Where(path => !path.EndsWith(".partial", StringComparison.Ordinal)).ToArray()
            : [];
        foreach (var candidate in candidates)
        {
            if (await IsValidAsync(candidate, gamePath, executable, sources, status, token).ConfigureAwait(false))
            {
                status("已校验并复用本地游戏索引镜像");
                EnsureUnchanged(gamePath, executable, sources);
                return candidate;
            }
        }

        Directory.CreateDirectory(root);
        long required = sources.Sum(file => file.Length);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.DriveType == DriveType.Network)
            throw new IOException("本地索引镜像缓存位于网络盘，请将用户本地数据目录配置到可写本地磁盘。");
        if (drive.AvailableFreeSpace < required + 64L * 1024 * 1024)
            throw new IOException($"本地索引镜像空间不足，需要约 {required / 1073741824d:F2} GB，缓存位置：{root}");

        // Each attempt owns only its GUID staging directory. Failed/cancelled copies never become a usable cache.
        var staging = Path.Combine(root, identity + "." + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(staging);
        try
        {
            var copied = new List<CachedFile>();
            long completed = 0;
            var buffer = new byte[1024 * 1024];
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                status($"正在建立本地索引镜像 {copied.Count + 1}/{sources.Length}（{completed / 1073741824d:F2}/{required / 1073741824d:F2} GB）：{source.Path}");
                string target = Path.Combine(staging, source.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var input = OpenRead(Path.Combine(gamePath, source.Path)))
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long count = 0;
                    int read;
                    long nextProgress = 64L * 1024 * 1024;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        count += read;
                        if (count > source.Length) throw new IOException("游戏资源正在更新，请更新完成后重试。");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        if (count >= nextProgress)
                        {
                            status($"正在建立本地索引镜像（{(completed + count) / 1073741824d:F2}/{required / 1073741824d:F2} GB）");
                            nextProgress = count + 64L * 1024 * 1024;
                        }
                    }
                    if (count != source.Length) throw new IOException("游戏资源读取不完整，请检查磁盘或等待游戏更新完成。");
                }
                copied.Add(new(source, Convert.ToHexString(hash.GetHashAndReset())));
                completed += source.Length;
            }
            EnsureUnchanged(gamePath, executable, sources);
            await File.WriteAllTextAsync(Path.Combine(staging, "mirror.json"),
                JsonSerializer.Serialize(new Manifest(gamePath, executable, copied.ToArray())), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Keep corrupt/old snapshots out of the publication path; never recursively remove a shared cache here.
            if (Directory.Exists(destination)) destination += "." + Guid.NewGuid().ToString("N");
            Directory.Move(staging, destination);
            status("本地索引镜像已准备，将重新读取游戏资源");
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static SourceFile[] Snapshot(string gamePath)
    {
        var final = Path.Combine(gamePath, "DATA", "FINAL");
        if (!Directory.Exists(final)) throw new IOException("游戏目录缺少 DATA/FINAL，无法建立索引镜像。");
        var files = Directory.EnumerateFiles(final, "*", Enumeration)
            .Where(path => path.EndsWith(".wad.client", StringComparison.OrdinalIgnoreCase))
            .Select(path => Describe(gamePath, path)).OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new IOException("游戏目录没有可读取的 WAD 资源，无法建立索引镜像。");
        return files;
    }

    private static SourceFile Describe(string root, string path)
    {
        var info = new FileInfo(path);
        return new(Path.GetRelativePath(root, path), info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static void EnsureUnchanged(string gamePath, SourceFile executable, SourceFile[] sources)
    {
        if (Describe(gamePath, Path.Combine(gamePath, executable.Path)) != executable || !Snapshot(gamePath).SequenceEqual(sources))
            throw new IOException("建立索引镜像期间游戏资源发生变化，请等待游戏更新完成后重试。");
    }

    private static async Task<bool> IsValidAsync(string destination, string gamePath, SourceFile executable,
        SourceFile[] sources, Action<string> status, CancellationToken token)
    {
        try
        {
            if (!File.Exists(Path.Combine(destination, "mirror.json"))) return false;
            var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(Path.Combine(destination, "mirror.json"), token).ConfigureAwait(false));
            if (manifest is null || manifest.Game != gamePath || manifest.Executable != executable
                || manifest.Files is null || manifest.Files.Any(file => file is null)
                || !manifest.Files.Select(file => file.Source).SequenceEqual(sources)) return false;
            // Use trusted source paths, never paths read from the cache manifest.
            for (int i = 0; i < sources.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                status($"正在校验本地索引镜像 {i + 1}/{sources.Length}：{sources[i].Path}");
                string target = Path.Combine(destination, sources[i].Path);
                if (!File.Exists(target) || new FileInfo(target).Length != sources[i].Length) return false;
                await using var stream = OpenRead(target);
                if (Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != manifest.Files[i].Sha256) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
