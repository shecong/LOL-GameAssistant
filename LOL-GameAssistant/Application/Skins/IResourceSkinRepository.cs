namespace LOL_GameAssistant.Application.Skins;

// Deliberately not a record: its default ToString must never include the token.
public sealed class ResourceRepositoryOptions
{
    public string Owner { get; init; } = "jinjutwo";
    public string Repository { get; init; } = "skin-care-manager--hid.dll";
    public string Branch { get; init; } = "";
    public string AccessToken { get; init; } = "";
}
public sealed record ResourceRepositoryEntry(string Name, string Path, bool IsDirectory, long Size, string Sha);
public sealed record ResourceDownloadProgress(string Name, long Bytes, long? Total);

public interface IResourceSkinRepository
{
    Task<IReadOnlyList<ResourceRepositoryEntry>> ListAsync(ResourceRepositoryOptions options, string path, CancellationToken token = default);
    Task<string> DownloadAsync(ResourceRepositoryOptions options, ResourceRepositoryEntry entry,
        IProgress<ResourceDownloadProgress>? progress = null, CancellationToken token = default);
}
