using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Owns its tools and overlay process; never changes the game's installed WAD files.</summary>
public sealed class CslolSkinService : ICslolSkinService, IDisposable
{
    public const string DefaultPackageKey = "mRPqttejVfINzIh0r5KRoteQqgVma/tdJFG+IwugAnM=";
    private readonly string _toolsDir, _packagePath, _manifestPath, _workRoot, _key;
    private readonly string? _library;
    private readonly Func<bool> _gameRunning, _memoryEnabled;
    private readonly ICslolToolRunner _runner;
    private readonly CslolGameIndexMirror _indexMirror;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _operation;
    private ICslolOverlaySession? _overlay;
    private CslolSkinEntry[] _catalog = [];
    private long _revision;
    private bool _disposed;

    public CslolSkinService(string toolsDir, string packagePath, string manifestPath, string workRoot,
        string packageKeyBase64 = DefaultPackageKey, string? localLibraryDir = null,
        Func<bool>? memoryEnabled = null, Func<bool>? gameRunning = null, ICslolToolRunner? runner = null,
        string? indexMirrorRoot = null)
    {
        _toolsDir = Path.GetFullPath(toolsDir); _packagePath = packagePath;
        _manifestPath = manifestPath; _workRoot = Path.GetFullPath(workRoot); _key = packageKeyBase64;
        _library = string.IsNullOrWhiteSpace(localLibraryDir) ? null : Path.GetFullPath(localLibraryDir);
        _memoryEnabled = memoryEnabled ?? (() => false); _gameRunning = gameRunning ?? IsGameRunning;
        _runner = runner ?? new CslolToolRunner(Path.Combine(_toolsDir, "mod-tools.exe"));
        _indexMirror = new(indexMirrorRoot ?? CslolGameIndexMirror.DefaultRoot);
    }

    public bool IsAvailable => File.Exists(Path.Combine(_toolsDir, "mod-tools.exe"))
        && File.Exists(Path.Combine(_toolsDir, "cslol-dll.dll")) && File.Exists(Path.Combine(_toolsDir, "hashes.game.txt"))
        && File.Exists(_manifestPath) && (_library is not null ? Directory.Exists(_library) : File.Exists(_packagePath));
    public bool IsRunning { get { lock (_sync) return _overlay?.IsRunning == true; } }
    public IReadOnlyList<CslolSkinEntry> Catalog => _catalog;
    public string CatalogPatch { get; private set; } = "";
    public event Action<string>? StatusChanged;

    public async Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_manifestPath, cancellationToken).ConfigureAwait(false));
        CatalogPatch = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("patch", out var patch) ? patch.GetString() ?? "" : "";
        var files = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("files");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
        var entries = files.Deserialize<CslolSkinEntry[]>(options) ?? [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ChampionKey) || string.IsNullOrWhiteSpace(entry.SkinName)
                || string.IsNullOrWhiteSpace(entry.FileName) || !names.Add(entry.FileName)
                || entry.Size <= 0 || entry.Size > 256L * 1024 * 1024 || entry.SkinNum < 0
                || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("皮肤索引条目无效或重复");
        }
        _catalog = entries;
        return entries;
    }

    public async Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries,
        bool ignoreConflict = false, CancellationToken cancellationToken = default)
    {
        long revision; lock (_sync) revision = _revision;
        bool acquired = false;
        CancellationTokenSource? operation = null;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); acquired = true;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (revision != _revision) return Fail("cancelled", "操作已停止");
                _operation = operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            var token = operation.Token;
            if (!IsAvailable) return Fail("precheck", "文件换肤组件或皮肤资源缺失");
            gamePath = ResolveGameDirectory(gamePath) ?? "";
            if (gamePath.Length == 0) return Fail("precheck", "请选择包含 League of Legends.exe 和 DATA 的 Game 目录");
            if (_gameRunning()) return Fail("game-running", "请先退出对局，应用文件皮肤后再进入游戏");
            if (_memoryEnabled()) return Fail("engine-conflict", "请先关闭内存换肤引擎");
            if (entries.Count == 0) return Fail("precheck", "请选择皮肤");
            if (_catalog.Length == 0) await LoadCatalogAsync(token).ConfigureAwait(false);
            var selected = entries.ToArray();
            if (selected.Any(e => !_catalog.Contains(e))) return Fail("catalog", "所选皮肤不属于当前目录，请重新加载");
            var conflict = selected.GroupBy(e => e.ConflictKey, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (!ignoreConflict && conflict is not null) return Fail("conflict", $"同一英雄只能选择一个皮肤：{conflict.Key}");
            try
            {
                if (await GameSkinLoaderIsolation.DisableResourceLoaderAsync(gamePath, _gameRunning, token).ConfigureAwait(false))
                    Status("已备份并停用资源包 HID.dll，避免与游戏外换肤同时加载；再次挂载资源包时恢复。");
            }
            catch (IOException error) { return Fail("loader-conflict", error.Message); }
            StopOverlay();
            string indexPath = gamePath;
            bool mirrorAttempted = false;
            async Task<CslolToolResult> RunIndexedAsync(IReadOnlyList<string> arguments)
            {
                IReadOnlyList<string> WithIndexPath() => arguments.Select(arg => arg.StartsWith("--game:", StringComparison.Ordinal)
                    ? "--game:" + indexPath : arg).ToArray();
                var result = await _runner.RunAsync(WithIndexPath(), Status, token).ConfigureAwait(false);
                if (!HasGameReadFailure(result)) return result;
                if (mirrorAttempted)
                    throw new PreparationException("index-mirror", "本地镜像仍无法读取游戏资源，请检查资源版本、文件权限和磁盘状态。\n" + result.Message);
                mirrorAttempted = true;
                EnsurePreparationAllowed();
                Status("游戏资源读取失败，正在尝试本地索引镜像兼容（无盘/虚拟盘 IO）");
                try { indexPath = await _indexMirror.PrepareAsync(gamePath, Status, token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new PreparationException("index-mirror", "无法建立本地索引镜像：" + error.Message
                        + "\n请检查本地磁盘空间和文件读取权限；访问被管控策略拒绝时，请联系网吧管理员。\n原始工具信息：" + result.Message);
                }
                EnsurePreparationAllowed();
                result = await _runner.RunAsync(WithIndexPath(), Status, token).ConfigureAwait(false);
                if (HasGameReadFailure(result))
                    throw new PreparationException("index-mirror", "本地镜像仍无法读取游戏资源，请检查资源版本、文件权限和磁盘状态。\n" + result.Message);
                return result;
            }
            void EnsurePreparationAllowed()
            {
                token.ThrowIfCancellationRequested();
                if (_gameRunning()) throw new PreparationException("game-running", "准备期间游戏已启动，请退出对局后重试");
                if (_memoryEnabled()) throw new PreparationException("engine-conflict", "内存换肤已启用，文件覆盖层未启动");
            }
            var preset = Path.Combine(_workRoot, "profiles", Guid.NewGuid().ToString("N"));
            var installed = Path.Combine(preset, "installed");
            var overlay = Path.Combine(preset, "overlay");
            Directory.CreateDirectory(installed);
            using var store = _library is null ? new SkinPackageStore(_packagePath, _key) : null;
            var mods = new List<string>();
            for (int i = 0; i < selected.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Status($"正在导入皮肤资源 {i + 1}/{selected.Length}：{selected[i].DisplayName}");
                var source = await GetPackageAsync(store, selected[i], token).ConfigureAwait(false);
                var mod = $"mod_{i}";
                var destination = Path.Combine(installed, mod);
                var imported = await RunIndexedAsync(["import", source, destination, $"--game:{gamePath}", "--noTFT"]).ConfigureAwait(false);
                if (!imported.Ok || !Directory.Exists(Path.Combine(destination, "WAD"))
                    || !Directory.EnumerateFiles(Path.Combine(destination, "WAD"), "*.wad.client", SearchOption.AllDirectories).Any())
                    return Fail("import", imported.Message.Length == 0 ? "导入未生成 WAD 文件" : imported.Message);
                mods.Add(mod);
            }
            token.ThrowIfCancellationRequested();
            if (_gameRunning()) return Fail("game-running", "准备期间游戏已启动，请退出对局后重试");
            Status($"正在生成覆盖层（{mods.Count} 个皮肤）");
            var args = new List<string> { "mkoverlay", installed, overlay, $"--game:{gamePath}", $"--mods:{string.Join('/', mods)}" };
            if (ignoreConflict) args.Add("--ignoreConflict");
            var built = await RunIndexedAsync(args).ConfigureAwait(false);
            if (!built.Ok) return Fail("mkoverlay", built.Message);
            token.ThrowIfCancellationRequested();
            if (_gameRunning()) return Fail("game-running", "准备期间游戏已启动，请退出对局后重试");
            if (_memoryEnabled()) return Fail("engine-conflict", "内存换肤已启用，文件覆盖层未启动");
            Status("正在启动文件换肤，等待进入对局");
            var session = await _runner.StartOverlayAsync(["runoverlay", overlay, overlay + ".config", $"--game:{gamePath}", "--opts:none"], Status, token).ConfigureAwait(false);
            lock (_sync)
            {
                if (token.IsCancellationRequested || revision != _revision || _disposed) { session.Dispose(); token.ThrowIfCancellationRequested(); return Fail("cancelled", "操作已停止"); }
                _overlay = session;
            }
            Status("覆盖层已准备，等待游戏启动；进程启动不代表画面验收通过");
            return new() { Ok = true, Stage = "waiting", Message = "覆盖层已准备，等待游戏启动", ImportedMods = mods.Count, OverlayPath = overlay, RunoverlayPid = session.Pid };
        }
        catch (OperationCanceledException) { return Fail("cancelled", "已取消文件换肤操作"); }
        catch (PreparationException error) { return Fail(error.Stage, error.Message); }
        catch (Exception error) { return Fail("exception", error.Message); }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_operation, operation)) _operation = null; }
            operation?.Dispose();
            if (acquired) _gate.Release();
        }
    }

    public async Task<CslolApplyResult> StopAsync()
    {
        lock (_sync) { _revision++; _operation?.Cancel(); }
        await _gate.WaitAsync().ConfigureAwait(false);
        try { StopOverlay(); Status("文件换肤已停止；已进入游戏时需退出对局才能切换引擎"); return new() { Ok = true, Stage = "stopped", Message = "已停止" }; }
        catch (Exception error) { return Fail("stop", error.Message); }
        finally { _gate.Release(); }
    }

    private async Task<string> GetPackageAsync(SkinPackageStore? store, CslolSkinEntry entry, CancellationToken token)
    {
        if (_library is not null)
        {
            var relative = entry.FileName.Replace('/', Path.DirectorySeparatorChar);
            var options = new[] { Path.Combine(_library, relative), Path.Combine(_library, Safe(entry.ChampionKey), Safe(entry.SkinName), Safe(entry.DisplayName) + ".fantome") };
            foreach (var path in options)
                if (IsChild(_library, path) && File.Exists(path) && await VerifyAsync(path, entry, token).ConfigureAwait(false)) return path;
            throw new InvalidDataException($"本地皮肤文件缺失或校验失败：{entry.DisplayName}");
        }
        var cache = Path.Combine(_workRoot, "package-cache", entry.Sha256.ToLowerInvariant() + ".fantome");
        if (File.Exists(cache) && await VerifyAsync(cache, entry, token).ConfigureAwait(false)) return cache;
        var item = store!.Find(entry.FileName) ?? throw new InvalidDataException("加密包中不存在所选条目");
        await store.ExtractAsync(item, cache, token).ConfigureAwait(false);
        if (!await VerifyAsync(cache, entry, token).ConfigureAwait(false)) throw new InvalidDataException("皮肤资源 SHA256 校验失败");
        return cache;
    }

    private static async Task<bool> VerifyAsync(string path, CslolSkinEntry entry, CancellationToken token)
    {
        if (new FileInfo(path).Length != entry.Size) return false;
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private void StopOverlay() { lock (_sync) { _overlay?.Dispose(); _overlay = null; } }
    private void Status(string text) { try { StatusChanged?.Invoke(text); } catch { /* UI disposal must not break process cleanup. */ } }
    private static CslolApplyResult Fail(string stage, string message) => new() { Stage = stage, Message = message };
    private sealed class PreparationException(string stage, string message) : IOException(message)
    { public string Stage { get; } = stage; }

    private static bool HasGameReadFailure(CslolToolResult result)
    {
        // mod-tools may skip unreadable WADs, print a warning and still return exit code 0.
        // Never start an overlay built from an incomplete game index.
        if (result.Message.Contains("Failed to open game wad file", StringComparison.OrdinalIgnoreCase)) return true;
        if (result.Ok) return false;
        if (result.Message.Contains("Not a valid Game folder", StringComparison.OrdinalIgnoreCase)) return true;
        return result.Message.Contains("from_game_folder", StringComparison.OrdinalIgnoreCase)
            && new[] { "map", "open", "read", "access", "I/O" }.Any(word => result.Message.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
    private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
    private static bool IsChild(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string? ResolveGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        for (int depth = 0; depth < 3 && !string.IsNullOrEmpty(path); depth++, path = Path.GetDirectoryName(path)!)
            foreach (var candidate in new[] { path, Path.Combine(path, "Game") })
                if (File.Exists(Path.Combine(candidate, "League of Legends.exe")) && Directory.Exists(Path.Combine(candidate, "DATA"))) return Path.GetFullPath(candidate);
        return null;
    }
    public static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("League of Legends");
        try { return processes.Length != 0; } finally { foreach (var p in processes) p.Dispose(); }
    }
    public static bool HasGameWindow()
    {
        var processes = Process.GetProcessesByName("League of Legends");
        try { return processes.Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } }); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public void Dispose() { lock (_sync) { _disposed = true; _revision++; _operation?.Cancel(); } StopOverlay(); }
}

public sealed record CslolToolResult(bool Ok, string Message);
public interface ICslolOverlaySession : IDisposable { bool IsRunning { get; } int Pid { get; } }
public interface ICslolToolRunner
{
    Task<CslolToolResult> RunAsync(IReadOnlyList<string> arguments, Action<string> status, CancellationToken token);
    Task<ICslolOverlaySession> StartOverlayAsync(IReadOnlyList<string> arguments, Action<string> status, CancellationToken token);
}
