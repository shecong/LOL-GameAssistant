using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>独立资源安装到用户目录，按版本切换；兼容已有程序目录中的完整资源。</summary>
public sealed class OptionalCslolSkinService : ICslolSkinService, IFileSkinResourceInstaller, IDisposable
{
    private readonly string _bundled, _cache, _work, _sourceConfig;
    private readonly Func<bool> _memoryEnabled, _gameRunning;
    private readonly Func<HttpMessageHandler> _httpHandlerFactory;
    private readonly SemaphoreSlim _install = new(1, 1), _download = new(1, 1);
    private CslolSkinService? _service;
    private static readonly string[] Required = ["Data/Skins/skins.plskin", "Data/Skins/manifest.json",
        "Tools/Cslol/hashes.game.txt", "Tools/Cslol/mod-tools.exe", "Tools/Cslol/cslol-dll.dll"];
    private static readonly HashSet<string> Allowed = new(Required.Concat([
        "Tools/Cslol/ritobin_cli.exe", "Tools/Cslol/wad-extract.exe", "Tools/Cslol/wad-make.exe",
        "Tools/Cslol/README.md", "Tools/Cslol/SHA256SUMS.json", "resource-version.json"]), StringComparer.Ordinal);
    public OptionalCslolSkinService(string bundled, string cache, string work, string sourceConfig,
        Func<bool> memoryEnabled, Func<bool>? gameRunning = null, Func<HttpMessageHandler>? httpHandlerFactory = null)
    {
        _bundled = bundled; _cache = cache; _work = work; _sourceConfig = sourceConfig;
        _memoryEnabled = memoryEnabled; _gameRunning = gameRunning ?? CslolSkinService.IsGameRunning;
        _httpHandlerFactory = httpHandlerFactory ?? (() => new HttpClientHandler { AllowAutoRedirect = false });
    }
    public event Action<string>? StatusChanged;
    public bool IsAvailable => Service.IsAvailable;
    public bool IsRunning => _service?.IsRunning == true;
    public IReadOnlyList<CslolSkinEntry> Catalog => _service?.Catalog ?? [];
    public string CatalogPatch => _service?.CatalogPatch ?? "";
    private CslolSkinService Service => _service ??= CreateService(ResolveRoot());
    private string ResolveRoot()
    {
        string pointer = Path.Combine(_cache, "current.txt");
        if (File.Exists(pointer))
        {
            string version = File.ReadAllText(pointer).Trim();
            if (Guid.TryParseExact(version, "N", out _))
            {
                string root = Path.Combine(_cache, version);
                if (Required.All(path => File.Exists(Path.Combine(root, path)))) return root;
            }
        }
        return _bundled;
    }
    private CslolSkinService CreateService(string root)
    {
        string library = Path.Combine(root, "Data", "Skins", "skinlib");
        var service = new CslolSkinService(Path.Combine(root, "Tools", "Cslol"),
            Path.Combine(root, "Data", "Skins", "skins.plskin"), Path.Combine(root, "Data", "Skins", "manifest.json"),
            _work, localLibraryDir: Directory.Exists(library) ? library : null,
            memoryEnabled: _memoryEnabled, gameRunning: _gameRunning);
        service.StatusChanged += text => StatusChanged?.Invoke(text);
        return service;
    }
    public Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new FileNotFoundException("尚未安装文件换肤资源，请点击“下载资源”或“导入资源包”。");
        return Service.LoadCatalogAsync(cancellationToken);
    }
    public async Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries,
        bool ignoreConflict = false, CancellationToken cancellationToken = default)
    {
        await _install.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Service.ApplyAsync(gamePath, entries, ignoreConflict, cancellationToken).ConfigureAwait(false); }
        finally { _install.Release(); }
    }
    public Task<CslolApplyResult> StopAsync() => _service?.StopAsync() ?? Task.FromResult(new CslolApplyResult { Ok = true });

    public Task ImportResourcesAsync(string archive, CancellationToken token = default) => ImportCoreAsync(archive, null, token);
    private async Task ImportCoreAsync(string archive, FileSkinResourceRelease? release, CancellationToken token)
    {
        await _install.WaitAsync(token).ConfigureAwait(false);
        string staging = Path.Combine(_cache, Guid.NewGuid().ToString("N"));
        bool committed = false;
        try
        {
            if (_gameRunning() || IsRunning) throw new InvalidOperationException("请先退出对局并停止文件换肤，再安装资源。");
            Directory.CreateDirectory(_cache);
            await using var lease = new FileStream(Path.Combine(_cache, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > Allowed.Count + 1) throw new InvalidDataException("资源包包含额外文件。");
            var indexEntry = zip.GetEntry("resource-files.json") ?? throw new InvalidDataException("资源包缺少校验清单。");
            if (indexEntry.Length > 65536) throw new InvalidDataException("资源清单过大。");
            using var indexStream = indexEntry.Open();
            var index = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(indexStream, cancellationToken: token).ConfigureAwait(false)
                ?? throw new InvalidDataException("资源清单无效。");
            if (!Required.All(index.ContainsKey) || index.Keys.Any(path => !Allowed.Contains(path)) || zip.Entries.Any(entry => entry.FullName != "resource-files.json" && !index.ContainsKey(entry.FullName))
                || zip.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
                throw new InvalidDataException("资源清单不完整或包含不支持的路径。");
            long total = 0;
            Directory.CreateDirectory(staging);
            foreach (var (path, expected) in index)
            {
                token.ThrowIfCancellationRequested();
                var entry = zip.GetEntry(path) ?? throw new InvalidDataException("资源包缺少文件：" + path);
                total += entry.Length;
                if (entry.Length <= 0 || total > 2L * 1024 * 1024 * 1024 || expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                    throw new InvalidDataException("资源大小或校验值无效。");
                StatusChanged?.Invoke("正在安装资源：" + Path.GetFileName(path));
                string destination = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var input = entry.Open())
                await using (var output = File.Create(destination))
                {
                    byte[] buffer = new byte[81920]; long written = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        written += count;
                        if (written > entry.Length) throw new InvalidDataException("资源解压大小超过声明长度。");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    }
                    if (written != entry.Length) throw new InvalidDataException("资源包已截断。");
                }
                await using var file = File.OpenRead(destination);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源校验失败：" + path);
            }
            if (release == null)
            {
                string version = "legacy", patch = "";
                string versionFile = Path.Combine(staging, "resource-version.json");
                if (File.Exists(versionFile))
                {
                    if (new FileInfo(versionFile).Length > 4096) throw new InvalidDataException("资源版本信息过大。");
                    using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(versionFile, token).ConfigureAwait(false));
                    version = metadata.RootElement.GetProperty("version").GetString() ?? "legacy";
                    patch = metadata.RootElement.GetProperty("gamePatch").GetString() ?? "";
                    if (version.Length > 64 || patch.Length > 32) throw new InvalidDataException("资源版本信息无效。");
                }
                await using var zipStream = File.OpenRead(archive);
                release = new(version, patch, Convert.ToHexString(await SHA256.HashDataAsync(zipStream, token).ConfigureAwait(false)), []);
            }
            if (release != null) await File.WriteAllTextAsync(Path.Combine(staging, "installed-resource.json"), JsonSerializer.Serialize(release), token).ConfigureAwait(false);
            using (var validation = CreateService(staging)) await validation.LoadCatalogAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_gameRunning() || IsRunning) throw new InvalidOperationException("对局已启动，资源安装已取消，请退出对局后重试。");
            string pointer = Path.Combine(_cache, "current.txt");
            string previous = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : "";
            string temporary = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Path.GetFileName(staging));
            File.Move(temporary, pointer, true);
            committed = true;
            _service?.Dispose(); _service = null;
            CleanupVersions(Path.GetFileName(staging), previous);
            StatusChanged?.Invoke("换肤资源安装完成，后续更新主程序无需重复下载。");
        }
        finally
        {
            try { if (!committed && Directory.Exists(staging)) Directory.Delete(staging, true); }
            finally { _install.Release(); }
        }
    }

    private void CleanupVersions(string current, string previous)
    {
        try { CleanupVersionsCore(current, previous); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { StatusChanged?.Invoke("资源已安装，旧文件暂时无法清理：" + error.Message); }
    }
    private void CleanupVersionsCore(string current, string previous)
    {
        foreach (string directory in Directory.EnumerateDirectories(_cache))
        {
            string name = Path.GetFileName(directory);
            // 仅清理本安装器管理的直接子目录，保留当前与前一版；不跟随目录链接。
            if (name == current || name == previous || !Guid.TryParseExact(name, "N", out _)
                || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
                || !Required.All(path => File.Exists(Path.Combine(directory, path)))) continue;
            try { Directory.Delete(directory, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { StatusChanged?.Invoke("旧资源暂时无法清理：" + error.Message); }
        }
        foreach (string file in Directory.EnumerateFiles(_cache, "*.download"))
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task<FileSkinResourceUpdate?> CheckForUpdatesAsync(CancellationToken token = default)
    {
        using var handler = _httpHandlerFactory();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("sc-assistant/1.0");
        var (release, online, message, _) = await ResolveReleaseAsync(http, token).ConfigureAwait(false);
        FileSkinResourceRelease? installed = null;
        string root = ResolveRoot();
        string marker = Path.Combine(root, "installed-resource.json");
        try
        {
            if (File.Exists(marker) && new FileInfo(marker).Length <= 65536)
                installed = JsonSerializer.Deserialize<FileSkinResourceRelease>(await File.ReadAllTextAsync(marker, token).ConfigureAwait(false), FileSkinResourceTransfer.Json);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        bool available = Required.All(path => File.Exists(Path.Combine(root, path)));
        return new(release, installed?.Version ?? (available ? "已有资源（版本未知）" : "未安装"),
            !available || !string.Equals(installed?.Sha256, release.Sha256, StringComparison.OrdinalIgnoreCase), online, message);
    }

    private async Task<(FileSkinResourceRelease Release, bool Online, string Message, bool AllowHttp)> ResolveReleaseAsync(HttpClient http, CancellationToken token)
    {
        if (new FileInfo(_sourceConfig).Length > 65536) throw new InvalidDataException("资源下载配置过大。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(_sourceConfig, token).ConfigureAwait(false));
        var root = config.RootElement;
        if (!root.TryGetProperty("fileSkinResources", out var source)) throw new InvalidOperationException("尚未配置换肤资源下载地址。");
        bool allowHttp = root.TryGetProperty("allowHttp", out var allow) && allow.ValueKind == JsonValueKind.True;
        var options = FileSkinResourceTransfer.Json;
        // 兼容旧配置；有版本元数据后，主程序更新不再决定资源版本。
        var baseline = source.Deserialize<FileSkinResourceRelease>(options) ?? throw new InvalidDataException("资源配置为空。");
        baseline = baseline with { Version = string.IsNullOrWhiteSpace(baseline.Version) ? "legacy" : baseline.Version, GamePatch = baseline.GamePatch ?? "" };
        FileSkinResourceTransfer.Validate(baseline, allowHttp);
        string cached = Path.Combine(_cache, "latest-resource.json");
        try
        {
            if (File.Exists(cached) && new FileInfo(cached).Length <= 65536)
                baseline = FileSkinResourceTransfer.Validate(JsonSerializer.Deserialize<FileSkinResourceRelease>(await File.ReadAllTextAsync(cached, token).ConfigureAwait(false), options)!, allowHttp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        // 在线源失效或缓存被清理时，也不能把已安装的新资源降级到程序内置旧版本。
        try
        {
            string installedFile = Path.Combine(ResolveRoot(), "installed-resource.json");
            if (File.Exists(installedFile) && new FileInfo(installedFile).Length <= 65536)
            {
                var installed = FileSkinResourceTransfer.Validate(JsonSerializer.Deserialize<FileSkinResourceRelease>(await File.ReadAllTextAsync(installedFile, token).ConfigureAwait(false), options)!, allowHttp);
                if (Version.TryParse(installed.Version, out var installedVersion) && (!Version.TryParse(baseline.Version, out var baseVersion) || installedVersion > baseVersion)) baseline = installed;
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException) { }
        var transfer = new FileSkinResourceTransfer(http, allowHttp, text => StatusChanged?.Invoke(text));
        bool hasRemote = false;
        if (source.TryGetProperty("manifestUrls", out var manifests))
        {
            if (manifests.ValueKind != JsonValueKind.Array || manifests.GetArrayLength() > 8) throw new InvalidDataException("资源版本清单地址无效。");
            foreach (var address in manifests.EnumerateArray())
            {
                hasRemote = true;
                if (!Uri.TryCreate(address.GetString(), UriKind.Absolute, out var uri) || !FileSkinResourceTransfer.ValidUri(uri, allowHttp)) throw new InvalidDataException("资源版本清单地址无效。");
                try
                {
                    var release = await transfer.ReadManifestAsync(uri.AbsoluteUri, token).ConfigureAwait(false);
                    // 不让旧镜像回退已知资源版本；版本使用数字点分格式。
                    if (Version.TryParse(baseline.Version, out var known) && Version.TryParse(release.Version, out var incoming) && incoming < known)
                        throw new InvalidDataException("备用源资源版本落后于本地记录。");
                    Directory.CreateDirectory(_cache);
                    string temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(release), token).ConfigureAwait(false); File.Move(temporary, cached, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    return (release, true, "已检查在线资源版本", allowHttp);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
                { StatusChanged?.Invoke("版本源暂不可用，尝试备用清单。"); }
            }
        }
        return (baseline, false, hasRemote ? "在线检查失败，使用上次记录或内置资源版本" : "使用内置资源版本，尚未配置在线清单", allowHttp);
    }

    public async Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false)
    {
        await _download.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_gameRunning() || IsRunning) throw new InvalidOperationException("请退出对局并停止文件换肤后再更新资源。");
            Directory.CreateDirectory(_cache);
            // 跨进程互斥，防止两个助手同时修改续传文件或切换资源。
            await using var lease = new FileStream(Path.Combine(_cache, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            using var handler = _httpHandlerFactory();
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("sc-assistant/1.0");
            var (release, _, message, allowHttp) = await ResolveReleaseAsync(http, token).ConfigureAwait(false);
            StatusChanged?.Invoke(message);
            string installedMarker = Path.Combine(ResolveRoot(), "installed-resource.json");
            if (Required.All(path => File.Exists(Path.Combine(ResolveRoot(), path))) && File.Exists(installedMarker) && new FileInfo(installedMarker).Length <= 65536)
            {
                try
                {
                    var installed = JsonSerializer.Deserialize<FileSkinResourceRelease>(await File.ReadAllTextAsync(installedMarker, token).ConfigureAwait(false), FileSkinResourceTransfer.Json);
                    if (string.Equals(installed?.Sha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
                    { StatusChanged?.Invoke("换肤资源已是当前可用版本，无需重复下载。"); return; }
                }
                catch (Exception error) when (error is IOException or JsonException) { }
            }
            string download = Path.Combine(_cache, release.Sha256.ToLowerInvariant() + ".download");
            var transfer = new FileSkinResourceTransfer(http, allowHttp, text => StatusChanged?.Invoke(text));
            await transfer.DownloadAsync(release, download, preferGitHub, token).ConfigureAwait(false);
            await ImportCoreAsync(download, release, token).ConfigureAwait(false);
            try { File.Delete(download); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { StatusChanged?.Invoke("资源已安装，下载缓存稍后清理：" + error.Message); }
        }
        finally { _download.Release(); }
    }
    public void Dispose() => _service?.Dispose();
}
