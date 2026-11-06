using System.Security.Cryptography;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Deploys only the user-supplied, pinned x64 loader, without replacing other DLLs.</summary>
public sealed class ResourceSkinLoader(string source, Func<bool> gameRunning) : IResourceSkinLoader
{
    public const string Sha256 = GameSkinLoaderIsolation.ResourceLoaderSha256;
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
            if (File.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || await HashAsync(target, token).ConfigureAwait(false) != Sha256)
                    throw new IOException("游戏目录已有不同版本的 HID.dll，已保留。请先备份并移走该文件，再安装本助手组件。");
                return;
            }
            var disabled = Path.Combine(game, GameSkinLoaderIsolation.DisabledLoaderName);
            if (File.Exists(disabled))
            {
                if (!await GameSkinLoaderIsolation.IsKnownLoaderAsync(disabled, token).ConfigureAwait(false))
                    throw new IOException("HID.dll 停用备份校验失败，已保留，请检查 " + disabled);
                CheckIdle(); token.ThrowIfCancellationRequested();
                File.Move(disabled, target, false);
                return;
            }
            if (!File.Exists(source)) throw new FileNotFoundException("缺少随助手分发的 Tools/ResourceSkins/HID.dll，请重新准备资源组件");
            temporary = Path.Combine(game, ".sc-hid-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            if (await HashAsync(temporary, token).ConfigureAwait(false) != Sha256) throw new InvalidDataException("HID.dll 组件校验失败，未安装");
            CheckIdle(); token.ThrowIfCancellationRequested();
            File.Move(temporary, target, false);
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
    private void CheckIdle() { if (gameRunning()) throw new InvalidOperationException("请先退出对局，再安装加载组件"); }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
}
