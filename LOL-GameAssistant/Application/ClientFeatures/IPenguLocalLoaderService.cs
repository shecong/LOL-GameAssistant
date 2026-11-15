namespace LOL_GameAssistant.Application.ClientFeatures;

public sealed record PreparedPenguLoader(string Version, string CorePath, string PluginsDirectory);

public interface IPenguLocalLoaderService
{
    string SourceDirectory { get; }
    PreparedPenguLoader Prepare();
    void SelectDirectory(string directory);
    Task<PreparedPenguLoader> InstallFromGiteeAsync(CancellationToken cancellationToken = default);
    void SetActivated(bool enabled);
}
