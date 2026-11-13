namespace LOL_GameAssistant.Application.ClientFeatures;

public sealed record PreparedPenguLoader(string Version, string ExecutablePath, string PluginsDirectory);
public sealed record PenguLoaderDownloadProgress(string Message, long Downloaded = 0, long Total = 0);

public interface IPenguLoaderDownloadService
{
    PreparedPenguLoader? GetInstalled();
    Task<PreparedPenguLoader> DownloadAsync(IProgress<PenguLoaderDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
