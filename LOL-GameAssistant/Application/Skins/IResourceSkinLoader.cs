namespace LOL_GameAssistant.Application.Skins;

public interface IResourceSkinLoader
{
    Task EnsureInstalledAsync(string gameDirectory, CancellationToken token = default);
    Task DisableAsync(string gameDirectory, CancellationToken token = default);
}
