using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.Files;

internal sealed class LocalCacheManagementService(string? root = null,
    IReadOnlyList<(Func<(int Entries, long Bytes)> Measure, Action Clear)>? matchCaches = null) : ILocalCacheManagementService
{
    private readonly string _root = Path.GetFullPath(root ?? LocalDataCache<string>.Root);
    public Task<IReadOnlyList<LocalCacheUsage>> GetUsageAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<LocalCacheUsage>>(() =>
        {
            var result = new List<LocalCacheUsage>();
            foreach (var (category, name, folder) in new[] {
                (LocalCacheCategory.GameAssets, "游戏图标与名称", "assets"),
                (LocalCacheCategory.OpggBuilds, "OP.GG 符文装备方案", "builds") })
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Path.Combine(_root, folder);
                var disk = CacheFiles.Measure(directory, cancellationToken);
                var memory = LocalCacheRegistry.Find(directory).Select(store => store.MeasureMemory()).ToArray();
                result.Add(new(category, name, directory, disk.Bytes, disk.Files,
                    memory.Sum(value => value.Bytes), memory.Sum(value => value.Entries), disk.Errors));
            }
            var matches = Game_Api.MeasureGameDetailCache();
            var additional = (matchCaches ?? []).Select(cache => cache.Measure()).ToArray();
            result.Add(new(LocalCacheCategory.MatchDetails, "对局战绩与玩家资料", "仅内存，退出助手后释放", 0, 0,
                matches.Bytes + additional.Sum(cache => cache.Bytes), matches.Entries + additional.Sum(cache => cache.Entries)));
            var augments = AugmentCatalog.MeasureIconCache();
            result.Add(new(LocalCacheCategory.AugmentIcons, "强化图标", "仅内存，退出助手后释放", 0, 0, augments.Bytes, augments.Entries));
            return result;
        }, cancellationToken);

    public Task<LocalCacheClearResult> ClearAsync(LocalCacheCategory category, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (category == LocalCacheCategory.MatchDetails)
            {
                Game_Api.ClearGameDetailCache();
                foreach (var cache in matchCaches ?? []) cache.Clear();
                return new LocalCacheClearResult(0, 0, "对局战绩与玩家资料缓存已清除，后续查询将重新获取。");
            }
            if (category == LocalCacheCategory.AugmentIcons)
            {
                AugmentCatalog.ClearIconCache();
                return new LocalCacheClearResult(0, 0, "强化图标缓存已清除。");
            }
            string folder = category switch
            {
                LocalCacheCategory.GameAssets => "assets",
                LocalCacheCategory.OpggBuilds => "builds",
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            string directory = Path.Combine(_root, folder);
            CacheFiles.ValidateRoot(directory);
            // 先使已启动的共享请求过期，避免清除后迟到的下载写回旧缓存。
            int errors = 0;
            var before = CacheFiles.Measure(directory, cancellationToken);
            foreach (var store in LocalCacheRegistry.Find(directory))
            {
                try { store.Clear(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors++; }
            }
            var remaining = CacheFiles.Clear(directory, cancellationToken);
            errors = Math.Max(errors, remaining.Errors);
            var after = CacheFiles.Measure(directory, cancellationToken);
            return new LocalCacheClearResult(Math.Max(0, before.Files - after.Files), errors,
                errors == 0 ? "本地缓存已清除，后续使用时会重新获取。" : $"部分缓存已清除，{errors} 项因占用、权限或链接未能清理。");
        }, cancellationToken);
}
