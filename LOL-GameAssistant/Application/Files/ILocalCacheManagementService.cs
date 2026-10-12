namespace LOL_GameAssistant.Application.Files;

public enum LocalCacheCategory { GameAssets, OpggBuilds, MatchDetails, AugmentIcons }

public sealed record LocalCacheUsage(LocalCacheCategory Category, string Name, string Location,
    long DiskBytes, int FileCount, long MemoryDataBytes, int MemoryEntries, int UnavailableFiles = 0);

public sealed record LocalCacheClearResult(int DeletedFiles, int FailedFiles, string Message);

public interface ILocalCacheManagementService
{
    Task<IReadOnlyList<LocalCacheUsage>> GetUsageAsync(CancellationToken cancellationToken = default);
    Task<LocalCacheClearResult> ClearAsync(LocalCacheCategory category, CancellationToken cancellationToken = default);
}
