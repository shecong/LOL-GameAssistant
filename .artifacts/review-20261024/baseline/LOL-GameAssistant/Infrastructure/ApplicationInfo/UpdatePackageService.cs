using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Infrastructure.ApplicationInfo;

/// <summary>下载、校验并准备更新。安装文件仅由本机自带的原生更新器修改。</summary>
internal sealed class UpdatePackageService
{
    internal const string ManifestFile = "update-files.txt";
    internal const long MaximumPackageSize = 4L * 1024 * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;
    private readonly string _installDirectory;

    public UpdatePackageService() : this(SharedClient, AppContext.BaseDirectory) { }
    internal UpdatePackageService(HttpClient client, string installDirectory)
    {
        _client = client;
        _installDirectory = Path.GetFullPath(installDirectory);
    }

    internal static Uri AssetUrl(UpdateRelease release, string asset, bool useServer = false)
    {
        if (asset.Length is < 1 or > 128 || asset.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new InvalidDataException("更新附件名称无效。");
        if (useServer)
        {
            var manifest = release.ServerManifestUrl ?? throw new InvalidDataException("尚未配置服务器下载地址。");
            ServerUpdateReleaseService.ValidateServerUrl(manifest, release.AllowHttpServer);
            return new Uri(manifest, asset);
        }
        return new Uri("https://github.com/shecong/LOL-GameAssistant/releases/download/"
            + Uri.EscapeDataString(release.TagName) + "/" + asset);
    }

    public async Task<PreparedUpdate> PrepareAsync(UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken, bool useServer = false)
    {
        string updater = Path.Combine(_installDirectory, "Updater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("当前运行包未包含 Updater.exe，请先从服务器、GitHub 或 QQ 群下载一次完整包。", updater);
        RejectReparsePoints(_installDirectory);
        string updateRoot = Path.Combine(_installDirectory, ".update");
        RejectReparsePoints(updateRoot);
        if (File.Exists(Path.Combine(updateRoot, "active-job.txt")))
            throw new IOException("存在未完成的安装，请先运行程序目录中的 Updater.exe 恢复，再重试。 ");

        var cached = await TryRestoreAsync(release, progress, cancellationToken).ConfigureAwait(false);
        if (cached != null) return cached;

        progress?.Report(new(0, 0, "正在读取更新配置"));
        using var configResponse = await GetManifestAsync(release, useServer, cancellationToken).ConfigureAwait(false);
        if (configResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new HttpRequestException("该下载源尚未提供此架构和版本的自动更新配置，请切换下载源或到 QQ 群获取完整包。", null, configResponse.StatusCode);
        configResponse.EnsureSuccessStatusCode();
        byte[] configBytes = await ReadLimitedAsync(configResponse.Content, 64 * 1024, cancellationToken).ConfigureAwait(false);
        var config = JsonSerializer.Deserialize<UpdateManifest>(configBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("更新配置为空。");
        ValidateManifest(config, release);

        string token = Guid.NewGuid().ToString("N");
        string transaction = Path.Combine(updateRoot, token);
        Directory.CreateDirectory(transaction);
        try
        {
            string zip = Path.Combine(transaction, "package.zip");
            await DownloadAsync(AssetUrl(release, config.Package, useServer), zip, config, progress, cancellationToken).ConfigureAwait(false);
            string stage = Path.Combine(transaction, "stage");
            progress?.Report(new(config.Size, config.Size, "正在解压并验证文件"));
            await Task.Run(() => ExtractAndVerify(zip, stage, cancellationToken), cancellationToken).ConfigureAwait(false);
            ValidateStagedExecutable(stage, config);
            var prepared = await WriteJobAsync(transaction, config, cancellationToken).ConfigureAwait(false);
            // Write the cache marker last, only after the entire package is verified and ready.
            await File.WriteAllBytesAsync(Path.Combine(transaction, "manifest.json"), configBytes, cancellationToken).ConfigureAwait(false);
            return prepared;
        }
        catch
        {
            // 此目录尚未交给更新器，且不包含任何已安装文件。
            try { Directory.Delete(transaction, recursive: true); } catch { }
            throw;
        }
    }

    public async Task<PreparedUpdate?> TryRestoreAsync(UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.Combine(_installDirectory, ".update");
        RejectReparsePoints(root);
        if (!Directory.Exists(root) || File.Exists(Path.Combine(root, "active-job.txt"))) return null;
        foreach (string transaction in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(transaction), "N", out _)) continue;
            try
            {
                RejectReparsePoints(transaction);
                // Started transactions belong to native updater recovery, never to the download cache.
                if (File.Exists(Path.Combine(transaction, "state.txt")) || Directory.Exists(Path.Combine(transaction, "backup"))) continue;
                string metadata = Path.Combine(transaction, "manifest.json");
                RejectReparsePoints(metadata);
                if (!File.Exists(metadata) || new FileInfo(metadata).Length > 64 * 1024) continue;
                var config = JsonSerializer.Deserialize<UpdateManifest>(await File.ReadAllBytesAsync(metadata, cancellationToken).ConfigureAwait(false),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config == null) continue;
                ValidateManifest(config, release);
                progress?.Report(new(0, config.Size, "正在校验已下载的更新包"));
                string zip = Path.Combine(transaction, "package.zip");
                RejectReparsePoints(zip);
                if (new FileInfo(zip).Length != config.Size) continue;
                await using (var stream = File.OpenRead(zip))
                    if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false), Convert.FromHexString(config.Sha256))) continue;
                string stage = Path.Combine(transaction, "stage");
                await Task.Run(() => VerifyStage(stage, cancellationToken), cancellationToken).ConfigureAwait(false);
                ValidateStagedExecutable(stage, config);
                var prepared = await WriteJobAsync(transaction, config, cancellationToken).ConfigureAwait(false);
                progress?.Report(new(config.Size, config.Size, "已恢复下载完成的更新包"));
                return prepared;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
            {
                // Missing, stale or damaged caches fall back to a fresh download; installed files stay untouched.
            }
        }
        return null;
    }

    private static void ValidateStagedExecutable(string stage, UpdateManifest config)
    {
        string entry = Path.Combine(stage, config.EntryExecutable);
        UpdatePlatform.ValidateExecutable(entry);
        var binaryVersion = FileVersionInfo.GetVersionInfo(entry);
        var actualVersion = new Version(binaryVersion.FileMajorPart, binaryVersion.FileMinorPart,
            Math.Max(0, binaryVersion.FileBuildPart), Math.Max(0, binaryVersion.FilePrivatePart));
        if (AppVersion.Normalize(actualVersion) != AppVersion.Normalize(Version.Parse(config.Version)))
            throw new InvalidDataException("更新包中的主程序版本与发布配置不一致。");
    }

    private async Task<PreparedUpdate> WriteJobAsync(string transaction, UpdateManifest config, CancellationToken ct)
    {
        string token = Path.GetFileName(transaction);
        string runner = Path.Combine(transaction, "runner", "Updater.exe");
        RejectReparsePoints(runner);
        string updater = Path.Combine(_installDirectory, "Updater.exe");
        RejectReparsePoints(updater);
        Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
        File.Copy(updater, runner, overwrite: true);
        string manifestHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(transaction, "stage", ManifestFile), ct).ConfigureAwait(false)));
        using var parent = Process.GetCurrentProcess();
        string job = Path.Combine(transaction, "job.ini");
        RejectReparsePoints(job);
        // Refresh process identity when reusing a download after restarting the application.
        string text = $"[update]\r\nSchema=1\r\nToken={token}\r\nEntry={config.EntryExecutable}\r\nVersion={config.Version}\r\nParentPid={parent.Id}\r\nParentStart={parent.StartTime.ToUniversalTime().ToFileTimeUtc()}\r\nManifestSha256={manifestHash}\r\nState=ready\r\n";
        await File.WriteAllTextAsync(job, text, Encoding.Unicode, ct).ConfigureAwait(false);
        return new(transaction, job, token, config.Version);
    }

    internal static void ValidateManifest(UpdateManifest config, UpdateRelease release, bool requireNewer = true)
    {
        if (config.SchemaVersion != 1 || config.Runtime != UpdatePlatform.Current ||
            !Version.TryParse(config.Version, out var version) ||
            AppVersion.Normalize(version) != AppVersion.Normalize(release.Version) ||
            (requireNewer && !AppVersion.IsNewer(version, AppVersion.Current)) ||
            config.Size <= 0 || config.Size > MaximumPackageSize ||
            config.EntryExecutable != "sc助手.exe" ||
            !string.Equals(config.Package, UpdatePlatform.Package(UpdatePlatform.Current), StringComparison.Ordinal))
            throw new InvalidDataException("更新配置与目标版本或平台不匹配。");
        if (config.Sha256 is not { Length: 64 } || !config.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("更新包缺少有效的 SHA256。");
    }

    private async Task<HttpResponseMessage> GetManifestAsync(UpdateRelease release, bool useServer, CancellationToken ct)
    {
        var response = await _client.GetAsync(AssetUrl(release, useServer ? "update.json" : UpdatePlatform.GithubManifest, useServer),
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        // 兼容此前只有 update.json 的 x64 GitHub 附件；不对其他架构猜测包类型。
        if (!useServer && UpdatePlatform.Current == "win-x64" && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return await _client.GetAsync(AssetUrl(release, "update.json"), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        return response;
    }

    private async Task DownloadAsync(Uri url, string path, UpdateManifest config,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long size && size != config.Size)
            throw new InvalidDataException("更新包大小与配置不符。");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        byte[] buffer = new byte[81920];
        long total = 0;
        long lastReport = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > config.Size) throw new InvalidDataException("更新包超出声明大小。");
            await target.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            hash.AppendData(buffer, 0, count);
            if (Environment.TickCount64 - lastReport >= 100 || total == config.Size)
            {
                progress?.Report(new(total, config.Size, "正在下载"));
                lastReport = Environment.TickCount64;
            }
        }
        if (total != config.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(config.Sha256)))
            throw new InvalidDataException("更新包不完整或 SHA256 校验失败，请重新下载。");
    }

    internal static void ExtractAndVerify(string archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 20000) throw new InvalidDataException("更新包文件数过多。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
            ValidateRelativePath(relative);
            if (!seen.Add(relative)) throw new InvalidDataException("更新包存在重复路径。");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("更新包不允许符号链接。");
            string path = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            expanded += entry.Length;
            if (expanded > 8L * 1024 * 1024 * 1024) throw new InvalidDataException("解压体积超出限制。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
        }
        VerifyStage(destination, ct);
    }

    private static void VerifyStage(string destination, CancellationToken ct)
    {
        RejectReparsePoints(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            RejectReparsePoints(path);
        }
        string manifest = Path.Combine(destination, ManifestFile);
        if (new FileInfo(manifest).Length > 8 * 1024 * 1024)
            throw new InvalidDataException("更新文件清单过大。");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestFile };
        foreach (string line in File.ReadLines(manifest, Encoding.UTF8))
        {
            ct.ThrowIfCancellationRequested();
            int separator = line.IndexOf('\t');
            if (separator != 64 || !line[..64].All(Uri.IsHexDigit)) throw new InvalidDataException("文件清单格式无效。");
            string relative = line[(separator + 1)..];
            ValidateRelativePath(relative);
            if (!listed.Add(relative)) throw new InvalidDataException("文件清单路径重复。");
            using var file = File.OpenRead(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), Convert.FromHexString(line[..64])))
                throw new InvalidDataException($"文件校验失败：{relative}");
        }
        foreach (string path in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
            if (!listed.Contains(Path.GetRelativePath(destination, path).Replace('\\', '/')))
                throw new InvalidDataException("更新包包含清单以外的文件。");
        if (!listed.Contains("sc助手.exe") || !listed.Contains("Updater.exe"))
            throw new InvalidDataException("更新包缺少主程序或更新器。");
    }

    internal static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 220 || path.Contains('\\'))
            throw new InvalidDataException("更新路径无效。");
        string[] parts = path.Split('/');
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException("更新包包含不安全路径。");
        }
        if (parts[0].Equals(".update", StringComparison.OrdinalIgnoreCase) ||
            new[] { "logs", "cache", "userdata" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase) ||
            parts[^1].Equals("settings.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包试图覆盖用户数据。");
    }

    internal static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新目录不支持符号链接或目录联接。");
    }

    internal static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        await using var input = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("更新配置过大。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LOL-GameAssistant-Updater/1.0");
        return client;
    }
}
