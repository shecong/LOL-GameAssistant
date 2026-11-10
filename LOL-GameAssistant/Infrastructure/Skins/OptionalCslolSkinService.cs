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
    private readonly SemaphoreSlim _install = new(1, 1);
    private CslolSkinService? _service;
    private static readonly string[] Required = ["Data/Skins/skins.plskin", "Data/Skins/manifest.json",
        "Tools/Cslol/hashes.game.txt", "Tools/Cslol/mod-tools.exe", "Tools/Cslol/cslol-dll.dll"];
    private static readonly HashSet<string> Allowed = new(Required.Concat([
        "Tools/Cslol/ritobin_cli.exe", "Tools/Cslol/wad-extract.exe", "Tools/Cslol/wad-make.exe",
        "Tools/Cslol/README.md", "Tools/Cslol/SHA256SUMS.json"]), StringComparer.Ordinal);
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
    public Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries,
        bool ignoreConflict = false, CancellationToken cancellationToken = default) =>
        Service.ApplyAsync(gamePath, entries, ignoreConflict, cancellationToken);
    public Task<CslolApplyResult> StopAsync() => _service?.StopAsync() ?? Task.FromResult(new CslolApplyResult { Ok = true });

    public async Task ImportResourcesAsync(string archive, CancellationToken token = default)
    {
        await _install.WaitAsync(token).ConfigureAwait(false);
        string staging = Path.Combine(_cache, Guid.NewGuid().ToString("N"));
        bool committed = false;
        try
        {
            if (_gameRunning() || IsRunning) throw new InvalidOperationException("请先退出对局并停止文件换肤，再安装资源。");
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
                if (entry.Length <= 0 || total > 2L * 1024 * 1024 * 1024 || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                    throw new InvalidDataException("资源大小或校验值无效。");
                StatusChanged?.Invoke("正在安装资源：" + Path.GetFileName(path));
                string destination = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var input = entry.Open())
                await using (var output = File.Create(destination))
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                await using var file = File.OpenRead(destination);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源校验失败：" + path);
            }
            using (var validation = CreateService(staging)) await validation.LoadCatalogAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_gameRunning() || IsRunning) throw new InvalidOperationException("对局已启动，资源安装已取消，请退出对局后重试。");
            string pointer = Path.Combine(_cache, "current.txt");
            string temporary = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Path.GetFileName(staging));
            File.Move(temporary, pointer, true);
            committed = true;
            _service?.Dispose(); _service = null;
            StatusChanged?.Invoke("换肤资源安装完成，后续更新主程序无需重复下载。");
        }
        finally
        {
            if (!committed && Directory.Exists(staging)) Directory.Delete(staging, true);
            _install.Release();
        }
    }

    public async Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false)
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(_sourceConfig, token).ConfigureAwait(false));
        var root = config.RootElement;
        if (!root.TryGetProperty("fileSkinResources", out var source))
            throw new InvalidOperationException("尚未配置独立换肤资源下载地址，可先导入本地资源包。");
        string expected = source.GetProperty("sha256").GetString() ?? "";
        bool allowHttp = root.TryGetProperty("allowHttp", out var allow) && allow.ValueKind == JsonValueKind.True;
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new InvalidDataException("下载配置缺少有效校验值。");
        var urls = source.GetProperty("urls").EnumerateArray().Select(item => new Uri(item.GetString() ?? "", UriKind.Absolute))
            .OrderBy(uri => uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == preferGitHub ? 0 : 1).ToArray();
        if (urls.Length == 0 || urls.Any(uri => !IsDownloadUri(uri, allowHttp))) throw new InvalidDataException("资源下载地址无效。");
        Directory.CreateDirectory(_cache);
        string download = Path.Combine(_cache, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using var handler = _httpHandlerFactory();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("sc-assistant/1.0");
            Exception? last = null;
            foreach (var url in urls)
            {
                try
                {
                    StatusChanged?.Invoke("正在下载换肤资源（首次使用需要下载）…");
                    Uri current = url;
                    for (int redirects = 0; ; redirects++)
                    {
                        using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                        {
                            current = new Uri(current, location);
                            if (redirects >= 5 || !IsDownloadUri(current, allowHttp)) throw new IOException("资源下载重定向无效。");
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                        await using (var output = File.Create(download))
                        {
                            byte[] buffer = new byte[81920]; long received = 0; int count;
                            var progress = System.Diagnostics.Stopwatch.StartNew();
                            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                            {
                                received += count;
                                if (received > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("资源下载超过大小限制。");
                                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                                if (progress.ElapsedMilliseconds >= 500)
                                {
                                    StatusChanged?.Invoke($"正在下载换肤资源：{received / 1048576d:F1} MB");
                                    progress.Restart();
                                }
                            }
                        }
                        break;
                    }
                    await using (var file = File.OpenRead(download))
                        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("下载的资源包校验失败。");
                    await ImportResourcesAsync(download, token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or TaskCanceledException) { last = error; }
            }
            throw new IOException("服务器和备用下载源均不可用：" + last?.Message, last);
        }
        finally { if (File.Exists(download)) File.Delete(download); }
    }
    private static bool IsDownloadUri(Uri uri, bool allowHttp) => uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment) && (uri.Scheme == "https" || allowHttp && uri.Scheme == "http");
    public void Dispose() => _service?.Dispose();
}
