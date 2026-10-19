namespace LOL_GameAssistant.Domain.ApplicationInfo;

/// <summary>单一版本和架构的完整更新配置，与根目录最新版本索引分开。</summary>
public sealed record UpdateManifest(int SchemaVersion, string Version, string Runtime,
    string Package, long Size, string Sha256, string EntryExecutable);

public sealed record UpdateDownloadProgress(long Downloaded, long Total, string Stage)
{
    public int Percent => Total <= 0 ? 0 : (int)Math.Min(100, Downloaded * 100 / Total);
}

public sealed record PreparedUpdate(string Directory, string JobPath, string Token, string Version);
