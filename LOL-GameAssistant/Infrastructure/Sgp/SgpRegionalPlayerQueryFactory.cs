using System.Collections.Concurrent;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.Sgp;

internal sealed class SgpRegionalPlayerQueryFactory(ILcuRequestSender lcu, PlayerQueryServices current) : IRegionalPlayerQueryFactory
{
    public IReadOnlyList<PlayerQueryRegion> Regions { get; } =
        new[] { "HN1", "HN10", "CQ100" }.Select(code => new PlayerQueryRegion(code,
            $"{(LeagueRegionNames.TryGetName(code, out var name) ? name : code)} · {code}"))
            .Prepend(new("", "当前大区（自动）")).ToArray();
    private readonly ConcurrentDictionary<string, PlayerQueryServices> _services = new(StringComparer.OrdinalIgnoreCase);

    public PlayerQueryServices Create(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return current;
        platform = platform.ToUpperInvariant();
        if (!Regions.Any(r => r.Platform == platform)) throw new NotSupportedException("该大区尚未配置 SGP 地址。");
        return _services.GetOrAdd(platform, realm =>
        {
            var client = new SgpClient(lcu, targetPlatform: realm);
            async Task<string> Scope(CancellationToken token)
            {
                var context = await client.GetContextAsync(token).ConfigureAwait(false);
                return $"sgp-region-v2:login:{context.LoginPlatform}:target:{realm}";
            }
            var history = new CachedMatchHistoryService(new SgpMatchHistoryService(client), context: Scope);
            var profiles = new CachedPlayerProfileService(new SgpPlayerProfileService(client, new LcuPlayerProfileService(lcu), history), context: Scope);
            var ranked = new CachedRankedStatsService(new SgpRankedStatsService(client), context: Scope);
            return new(profiles, history, ranked, new LegacyPremadeDetectionService(history));
        });
    }
}
