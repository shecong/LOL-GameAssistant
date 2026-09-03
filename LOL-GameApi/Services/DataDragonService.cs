using System.Net.Http.Json;

namespace LOL_GameApi.Services
{
    /// <summary>
    /// DataDragon 数据服务：负责游戏版本查询与本地缓存（并发安全）。
    /// </summary>
    public class DataDragonService
    {
        private static readonly HttpClient Http = new();
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static string? _latestVersion;
        private static DateTime _lastFetch = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

        /// <summary>
        /// 获取最新游戏版本；6 小时内命中缓存，多个请求并发时只拉取一次。
        /// </summary>
        public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
        {
            if (_latestVersion != null && DateTime.Now - _lastFetch < CacheDuration)
                return _latestVersion;

            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (_latestVersion != null && DateTime.Now - _lastFetch < CacheDuration)
                    return _latestVersion;

                var versions = await Http.GetFromJsonAsync<List<string>>(
                    "https://ddragon.leagueoflegends.com/api/versions.json",
                    cancellationToken);
                if (versions is { Count: > 0 })
                {
                    _latestVersion = versions[0];
                    _lastFetch = DateTime.Now;
                }
            }
            catch
            {
                // 外部接口不可用时返回当前缓存
            }
            finally
            {
                Gate.Release();
            }
            return _latestVersion;
        }
    }
}
