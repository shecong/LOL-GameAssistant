using System.IO.Compression;
using System.Security.Cryptography;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.Pengu;

/// <summary>Downloads the pinned Gitee release without changing an existing Loader installation.</summary>
public sealed class PenguReleaseInstaller
{
    public const string ReleasePageUrl = "https://gitee.com/sc_c/PenguLoader/releases/tag/visuals-v0.4.0";
    public const string DownloadUrl = "https://gitee.com/sc_c/PenguLoader/releases/download/visuals-v0.4.0/PenguLoader-sc-assistant-headless-v0.4.0-win-x64.zip";
    internal const string ArchiveSha256 = "565b955e13c02de53e71975a485eef9de405a378640db46cfb7d2bdb799b1179";
    private static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "PenguLoader", "gitee");
    internal static string DefaultDirectory => Path.Combine(DefaultRoot, "visuals-v0.4.0", "PenguLoader");
    private static readonly HttpClient Http = new(NetworkTrafficService.Handler(NetworkModule.Updates)) { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly string _expectedHash;
    private readonly string _expectedCoreHash;

    public PenguReleaseInstaller() : this(DefaultRoot, Http, ArchiveSha256) { }

    internal PenguReleaseInstaller(string root, HttpClient http, string expectedHash,
        string expectedCoreHash = "c59d37ac8a3fbad2fa75b5541bd8f1d1f7036a67f0c0cdb716d2bd4e17efd66a")
    { _root = Path.GetFullPath(root); _http = http; _expectedHash = expectedHash; _expectedCoreHash = expectedCoreHash; }

    public async Task<string> InstallAsync(CancellationToken token = default)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        string? staging = null;
        try
        {
            Directory.CreateDirectory(_root);
            string destination = Path.Combine(_root, "visuals-v0.4.0");
            if (Directory.Exists(destination))
            {
                // Plugin settings may have changed since extraction; verify the immutable core.
                string core = Path.Combine(destination, "PenguLoader", "bin", "core.dll");
                if (File.Exists(core) && File.Exists(Path.Combine(destination, ".archive-sha256")) &&
                    File.ReadAllText(Path.Combine(destination, ".archive-sha256")) == _expectedHash &&
                    Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(core, token).ConfigureAwait(false)))
                        .Equals(_expectedCoreHash, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(destination, "PenguLoader");
                throw new IOException("Gitee 组件目录不完整，请选择其他本地目录，或移走损坏目录后重新安装。");
            }
            using var response = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            const int maxBytes = 32 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("视觉组件下载包过大。");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + count > maxBytes) throw new InvalidDataException("视觉组件下载包过大。");
                bytes.Write(buffer, 0, count);
            }
            if (!Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)))
                .Equals(_expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Gitee 下载包 SHA-256 校验失败，未更改现有安装。");
            bytes.Position = 0;
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
            if (archive.Entries.Count > 100 || archive.Entries.Sum(entry => entry.Length) > maxBytes)
                throw new InvalidDataException("视觉组件压缩包内容异常。");
            staging = Path.Combine(_root, ".download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            // ExtractToDirectory rejects entries that escape the destination.
            archive.ExtractToDirectory(staging);
            foreach (string file in new[] { "bin/core.dll", "loader/src-tauri/tauri.conf.json", "extensions/lol-game-assistant-visuals/index.js" })
                if (!File.Exists(Path.Combine(staging, "PenguLoader", file))) throw new InvalidDataException("视觉组件压缩包缺少必要文件。");
            token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, ".archive-sha256"), _expectedHash);
            Directory.Move(staging, destination);
            staging = null;
            return Path.Combine(destination, "PenguLoader");
        }
        finally
        {
            try
            {
                if (staging != null && Path.GetFullPath(staging).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(staging, true);
            }
            finally { Gate.Release(); }
        }
    }
}
