using System.Security.Cryptography;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Deploys the compatible loader and a local copy of this OS's HID API provider.</summary>
public sealed class ResourceSkinLoader(string source, Func<bool> gameRunning) : IResourceSkinLoader
{
    public const string Sha256 = GameSkinLoaderIsolation.ResourceLoaderSha256;
    public const string SystemCompanionName = "sc_resource_hid_system.dll";
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task DisableAsync(string gameDirectory, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path.Combine(gameDirectory, "HID.dll"))) return;
            var game = ResourceSkinService.ResolveGame(gameDirectory);
            await GameSkinLoaderIsolation.DisableResourceLoaderAsync(game, gameRunning, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public async Task EnsureInstalledAsync(string gameDirectory, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            CheckIdle();
            var game = ResourceSkinService.ResolveGame(gameDirectory);
            for (var dir = new DirectoryInfo(game); dir != null; dir = dir.Parent)
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("游戏目录不能经过符号链接或目录联接");
            var target = Path.Combine(game, "HID.dll");
            var disabled = Path.Combine(game, GameSkinLoaderIsolation.DisabledLoaderName);
            bool legacyActive = false, legacyBackup = false, compatibleBackup = false;
            if (File.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("HID.dll 不能是符号链接");
                var hash = await HashAsync(target, token).ConfigureAwait(false);
                legacyActive = hash == GameSkinLoaderIsolation.LegacyResourceLoaderSha256;
                if (hash != Sha256 && !legacyActive)
                    throw new IOException("游戏目录已有不同版本的 HID.dll，已保留。请先备份并移走该文件，再安装本助手组件。");
            }
            if (File.Exists(disabled))
            {
                if (!await GameSkinLoaderIsolation.IsKnownLoaderAsync(disabled, token).ConfigureAwait(false))
                    throw new IOException("HID.dll 停用备份校验失败，已保留，请检查 " + disabled);
                legacyBackup = await HashAsync(disabled, token).ConfigureAwait(false) == GameSkinLoaderIsolation.LegacyResourceLoaderSha256;
                compatibleBackup = !legacyBackup;
            }
            if (File.Exists(target) && !legacyActive)
            {
                await EnsureSystemCompanionAsync(game, token).ConfigureAwait(false);
                if (legacyBackup) ArchiveLegacy(disabled, token);
                return;
            }
            if (compatibleBackup)
            {
                await EnsureSystemCompanionAsync(game, token).ConfigureAwait(false);
                CheckIdle(); token.ThrowIfCancellationRequested();
                if (legacyActive) ArchiveLegacy(target, token);
                File.Move(disabled, target, false);
                return;
            }
            if (!File.Exists(source)) throw new FileNotFoundException("缺少随助手分发的 Tools/ResourceSkins/HID.dll，请重新准备资源组件");
            temporary = Path.Combine(game, ".sc-hid-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            if (await HashAsync(temporary, token).ConfigureAwait(false) != Sha256) throw new InvalidDataException("HID.dll 组件校验失败，未安装");
            await EnsureSystemCompanionAsync(game, token).ConfigureAwait(false);
            CheckIdle(); token.ThrowIfCancellationRequested();
            if (legacyBackup) ArchiveLegacy(disabled, token);
            if (legacyActive) ArchiveLegacy(target, token);
            File.Move(temporary, target, false);
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
    private void ArchiveLegacy(string path, CancellationToken token)
    {
        CheckIdle(); token.ThrowIfCancellationRequested();
        File.Move(path, path + ".legacy-" + Guid.NewGuid().ToString("N") + ".bak", false);
    }
    private async Task EnsureSystemCompanionAsync(string game, CancellationToken token)
    {
        var systemHid = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "hid.dll");
        var expected = await HashAsync(systemHid, token).ConfigureAwait(false);
        var target = Path.Combine(game, SystemCompanionName);
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || await HashAsync(target, token).ConfigureAwait(false) != expected)
                throw new IOException("系统 HID 转发组件与本机版本不一致，已保留，请备份并移走 " + target);
            return;
        }
        var temporary = Path.Combine(game, ".sc-hid-system-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var input = File.OpenRead(systemHid))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            if (await HashAsync(temporary, token).ConfigureAwait(false) != expected) throw new InvalidDataException("本机系统 HID 组件复制校验失败");
            CheckIdle(); token.ThrowIfCancellationRequested();
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void CheckIdle() { if (gameRunning()) throw new InvalidOperationException("请先退出对局，再安装加载组件"); }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
}
