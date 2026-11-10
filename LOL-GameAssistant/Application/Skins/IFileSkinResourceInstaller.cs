namespace LOL_GameAssistant.Application.Skins;

public interface IFileSkinResourceInstaller
{
    Task ImportResourcesAsync(string archive, CancellationToken token = default);
    Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false);
}
