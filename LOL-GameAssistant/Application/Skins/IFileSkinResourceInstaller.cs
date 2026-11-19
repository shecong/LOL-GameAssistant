namespace LOL_GameAssistant.Application.Skins;

public sealed record FileSkinResourceRelease(string Version, string GamePatch, string Sha256, string[] Urls,
    long Size = 0, string Notes = "");
public sealed record FileSkinResourceUpdate(FileSkinResourceRelease Release, string InstalledVersion,
    bool UpdateAvailable, bool CheckedOnline, string Message);

public sealed record FileSkinResourceDownloadProgress(long Downloaded, long? Total, string Stage = "正在下载")
{
    public double? Percent => Total is > 0 ? Math.Clamp(Downloaded * 100d / Total.Value, 0, 100) : null;
    public string Description => Total is > 0
        ? $"{Stage}：{Downloaded / 1048576d:F1} / {Total.Value / 1048576d:F1} MB · {Percent:F1}%"
        : $"{Stage}：已下载 {Downloaded / 1048576d:F1} MB · 总大小未知";
}

public interface IFileSkinResourceInstaller
{
    Task ImportResourcesAsync(string archive, CancellationToken token = default);
    Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false,
        IProgress<FileSkinResourceDownloadProgress>? progress = null);
    Task<FileSkinResourceUpdate?> CheckForUpdatesAsync(CancellationToken token = default) => Task.FromResult<FileSkinResourceUpdate?>(null);
}
