using System.Security.Cryptography;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>Never load executable images from the build/publish directory: Windows retains them in the game.</summary>
internal static class GameOverlayRuntimeFiles
{
    internal static readonly string[] Names = ["sc-game-overlay-host.exe", "sc-game-overlay.dll"];

    internal static string Prepare(string sourceDirectory, string? cacheRoot = null)
    {
        // Read a complete pair before creating a cache entry. Content addresses also
        // reuse the same DLL path when an assistant reconnects to an existing game.
        var files = Names.Select(name => (Name: name, Bytes: File.ReadAllBytes(Path.Combine(sourceDirectory, name)))).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files) hash.AppendData(SHA256.HashData(file.Bytes));
        string version = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        cacheRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LOL-GameAssistant", "GameOverlay", "runtime");
        string directory = Path.Combine(cacheRoot, version);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            string destination = Path.Combine(directory, file.Name);
            if (!File.Exists(destination))
            {
                string temporary = Path.Combine(directory, $"{file.Name}.{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllBytes(temporary, file.Bytes);
                    try { File.Move(temporary, destination); }
                    catch (IOException) when (File.Exists(destination)) { } // Another assistant prepared the same version.
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            // Never overwrite or silently execute a damaged cache entry.
            if (!SHA256.HashData(File.ReadAllBytes(destination)).AsSpan().SequenceEqual(SHA256.HashData(file.Bytes)))
                throw new InvalidDataException($"游戏覆盖层运行缓存校验失败：{file.Name}");
        }
        return directory;
    }
}
