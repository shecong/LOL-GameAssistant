namespace LOL_GameAssistant.Application.Skins;

public sealed record FileSkinResourceRelease(string Version, string GamePatch, string Sha256, string[] Urls,
    long Size = 0, string Notes = "");
public sealed record FileSkinResourceUpdate(FileSkinResourceRelease Release, string InstalledVersion,
    bool UpdateAvailable, bool CheckedOnline, string Message);

public interface IFileSkinResourceInstaller
{
    Task ImportResourcesAsync(string archive, CancellationToken token = default);
    Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false);
    Task<FileSkinResourceUpdate?> CheckForUpdatesAsync(CancellationToken token = default) => Task.FromResult<FileSkinResourceUpdate?>(null);
}
