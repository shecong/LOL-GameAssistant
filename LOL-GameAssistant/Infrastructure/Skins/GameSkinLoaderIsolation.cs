using System.Security.Cryptography;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Separates the resource loader from the CSLOL overlay without deleting either loader.</summary>
public static class GameSkinLoaderIsolation
{
    public const string ResourceLoaderSha256 = "E4FBBF9671303712A764D505D9E9A6C5FAE7004873899255AB798F759D23E69B";
    public const string DisabledLoaderName = "HID.dll.sc-disabled";

    public static async Task<bool> DisableResourceLoaderAsync(string game, Func<bool> gameRunning, CancellationToken token)
    {
        var loader = Path.Combine(game, "HID.dll");
        if (!File.Exists(loader)) return false;
        for (var directory = new DirectoryInfo(game); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("游戏目录不能经过符号链接或目录联接");
        if (gameRunning()) throw new InvalidOperationException("请先退出对局，再停用资源包加载组件");
        if (!await IsKnownLoaderAsync(loader, token).ConfigureAwait(false))
            throw new IOException("游戏目录存在其他版本的 HID.dll，与游戏外换肤可能冲突。请退出游戏，备份并移走该文件后重试。");
        token.ThrowIfCancellationRequested();
        if (gameRunning()) throw new InvalidOperationException("游戏已启动，未修改加载组件，请退出对局后重试");
        // Never overwrite an existing backup, even if it appears to be ours.
        File.Move(loader, Path.Combine(game, DisabledLoaderName), false);
        return true;
    }

    public static async Task<bool> IsKnownLoaderAsync(string path, CancellationToken token)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) == ResourceLoaderSha256;
    }
}
