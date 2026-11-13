using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.Pengu;

/// <summary>从官方稳定发布接口下载便携版；不修改 Loader 激活状态。</summary>
public sealed class PenguLoaderDownloadService : IPenguLoaderDownloadService
{
    internal const string ReleaseApi = "https://api.github.com/repos/PenguLoader/PenguLoader/releases/latest";
    private const long MaximumDownload = 100 * 1024 * 1024;
    private const long MaximumExtracted = 256 * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _client;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PenguLoaderDownloadService(string directory) : this(SharedClient, directory) { }
    internal PenguLoaderDownloadService(HttpClient client, string directory)
    {
        _client = client;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    public PreparedPenguLoader? GetInstalled()
    {
        try
        {
            RejectLinks(_root);
            string marker = Path.Combine(_root, "current.json");
            RejectLinks(marker);
            if (!File.Exists(marker) || new FileInfo(marker).Length > 4096) return null;
            string? version = JsonSerializer.Deserialize<string>(File.ReadAllText(marker));
            return ValidVersion(version) ? FindPrepared(Path.Combine(_root, version!), version!) : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    public async Task<PreparedPenguLoader> DownloadAsync(IProgress<PenguLoaderDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Pengu Loader 正在下载中。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        string? working = null;
        try
        {
            var token = deadline.Token;
            progress?.Report(new("正在查询 Pengu Loader 官方稳定版"));
            using var apiDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            apiDeadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _client.GetAsync(ReleaseApi, HttpCompletionOption.ResponseHeadersRead, apiDeadline.Token).ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
                throw new HttpRequestException("GitHub 发布接口暂时限流，请稍后重试。");
            response.EnsureSuccessStatusCode();
            byte[] metadata = await ReadLimitedAsync(response.Content, 2 * 1024 * 1024, apiDeadline.Token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(metadata);
            var release = json.RootElement;
            string? version = release.GetProperty("tag_name").GetString();
            if (!ValidVersion(version) || release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("官方接口没有返回有效的稳定版本。");
            string expectedName = $"pengu-loader-{version}.zip";
            var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == expectedName).ToArray();
            if (assets.Length != 1) throw new InvalidDataException("该官方版本没有可用的 Windows 便携 ZIP，请稍后重试或手动安装。");
            var asset = assets[0];
            string expectedUrl = $"https://github.com/PenguLoader/PenguLoader/releases/download/{version}/{expectedName}";
            if (asset.GetProperty("browser_download_url").GetString() != expectedUrl)
                throw new InvalidDataException("发布附件地址不是 Pengu Loader 官方仓库。");
            long size = asset.GetProperty("size").GetInt64();
            if (size is <= 0 or > MaximumDownload) throw new InvalidDataException("下载包大小超出允许范围。");
            string? digest = asset.TryGetProperty("digest", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
            if (digest != null && !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("官方附件摘要格式无效。");

            RejectLinks(_root);
            Directory.CreateDirectory(_root);
            string final = Path.Combine(_root, version!);
            RejectLinks(final);
            if (Directory.Exists(final))
            {
                var cached = FindPrepared(final, version!);
                if (cached == null) throw new IOException("该版本目录不完整，请检查 Loader 下载目录，或手动选择已有 Loader。");
                Directory.CreateDirectory(cached.PluginsDirectory);
                WriteCurrent(version!);
                progress?.Report(new($"已准备 Pengu Loader {version}", size, size));
                return cached;
            }
            working = Path.Combine(_root, ".download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            string zip = Path.Combine(working, "package.zip");
            using (var download = await _client.GetAsync(expectedUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                download.EnsureSuccessStatusCode();
                if (download.Content.Headers.ContentLength is long length && length != size) throw new InvalidDataException("下载文件大小与官方发布信息不一致。");
                await using var input = await download.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                long count = 0;
                int reportedPercent = -1;
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    count += read;
                    if (count > size) throw new InvalidDataException("下载内容超过官方声明的大小。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    int percent = (int)(count * 100 / size);
                    if (percent != reportedPercent)
                    {
                        reportedPercent = percent;
                        progress?.Report(new($"正在下载 Pengu Loader {version}", count, size));
                    }
                }
                if (count != size) throw new InvalidDataException("下载未完成，请重试。");
            }
            string sha;
            await using (var stream = File.OpenRead(zip)) sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
            if (digest != null && !string.Equals(digest, "sha256:" + sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载包 SHA-256 与官方摘要不一致。");
            progress?.Report(new("正在解压 Pengu Loader", size, size));
            string stage = Path.Combine(working, "loader");
            await Task.Run(() => Extract(zip, stage, token), token).ConfigureAwait(false);
            var prepared = FindPrepared(stage, version!) ?? throw new InvalidDataException("下载包没有有效的 Pengu Loader 程序。");
            Directory.CreateDirectory(prepared.PluginsDirectory);
            File.WriteAllText(Path.Combine(stage, "sc-download.json"), JsonSerializer.Serialize(new { version, source = expectedUrl, sha256 = sha, officialDigest = digest }));
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, final);
            WriteCurrent(version!);
            progress?.Report(new($"已下载 Pengu Loader {version}", size, size));
            return FindPrepared(final, version!)!;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Pengu Loader 下载超时，请检查 GitHub 网络连接后重试。", error); }
        finally
        {
            // The unique workspace is a direct child of our own fixed download root.
            if (working != null && Path.GetDirectoryName(working) == _root && Path.GetFileName(working).StartsWith(".download-", StringComparison.Ordinal))
            {
                try { RejectLinks(working); if (Directory.Exists(working)) Directory.Delete(working, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _gate.Release();
        }
    }

    private void WriteCurrent(string version)
    {
        string marker = Path.Combine(_root, "current.json");
        RejectLinks(marker);
        string temporary = Path.Combine(_root, "current-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(version)); File.Move(temporary, marker, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool ValidVersion(string? version) => version != null && Regex.IsMatch(version, "^v[0-9]+\\.[0-9]+\\.[0-9]+$");

    private static PreparedPenguLoader? FindPrepared(string directory, string version)
    {
        RejectLinks(directory);
        if (!Directory.Exists(directory)) return null;
        foreach (string name in new[] { "Pengu Loader.exe", "PenguLoader.exe", "pengu-loader.exe" })
        {
            string executable = Path.Combine(directory, name);
            RejectLinks(executable);
            if (!File.Exists(executable)) continue;
            using var input = File.OpenRead(executable);
            if (input.ReadByte() != 'M' || input.ReadByte() != 'Z') continue;
            string plugins = Path.Combine(directory, "plugins");
            RejectLinks(plugins);
            return new(version, executable, plugins);
        }
        return null;
    }

    private static void Extract(string zip, string directory, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 4096) throw new InvalidDataException("压缩包文件数量过多。");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            total += entry.Length;
            if (total > MaximumExtracted || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                entry.FullName.Split('/', '\\').Any(part => part is "." or ".." || part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')))
                throw new InvalidDataException("压缩包包含不受支持的文件路径或大小。");
            string path = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("压缩包文件超出目标目录。");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: false);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("发布接口响应过大。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("下载目录包含符号链接 / 联接，请使用实际目录。");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(NetworkTrafficService.Handler(NetworkModule.Updates)) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LOL-GameAssistant");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
