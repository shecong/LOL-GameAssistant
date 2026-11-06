namespace LOL_GameAssistant.Application.Skins;

public sealed record ResourceSkinFile(string Name, string Source, string Sha256, long Bytes, uint Entries);
public sealed record ResourceSkinState(string GameDirectory, IReadOnlyList<ResourceSkinFile> Files);

public interface IResourceSkinService
{
    ResourceSkinState State { get; }
    Task ApplyAsync(string gameDirectory, IReadOnlyList<string> packages, CancellationToken token = default);
    Task ClearAsync(CancellationToken token = default);
}
