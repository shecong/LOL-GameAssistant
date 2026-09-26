using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Insights;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>公开 OP.GG 数据的只读适配器。数据不可用时抛出可由界面展示的异常，不影响 LCU 主流程。</summary>
public sealed class OpggChampionInsightsService : IChampionInsightsService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly IChampionCatalog _champions;

    public OpggChampionInsightsService(IChampionCatalog champions) => _champions = champions;

    public async Task<IReadOnlyList<ChampionTierInsight>> GetChampionTiersAsync(
        string mode,
        CancellationToken cancellationToken = default)
    {
        string normalizedMode = NormalizeMode(mode);
        if (normalizedMode == "aram_mayhem")
            throw new InvalidOperationException("KIWI 是海克斯大乱斗；OP.GG 当前公开接口不提供该模式的英雄 T 级数据。");
        string tier = normalizedMode == "arena" ? "all" : "gold_plus";
        JObject root = await GetJsonAsync(
            $"https://lol-api-champion.op.gg/api/global/champions/{normalizedMode}?tier={tier}",
            cancellationToken).ConfigureAwait(false);
        var output = new List<ChampionTierInsight>();
        foreach (JObject champion in root["data"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
        {
            int championId = champion.Value<int?>("id") ?? 0;
            if (championId <= 0) continue;

            JObject? stats = normalizedMode == "ranked"
                ? champion["positions"]?.OfType<JObject>()
                    .OrderByDescending(position => position["stats"]?.Value<double?>("pick_rate") ?? 0)
                    .FirstOrDefault()?["stats"] as JObject
                : champion["average_stats"] as JObject;
            if (stats == null) continue;

            int tierNumber = stats["tier_data"]?.Value<int?>("tier") ?? stats.Value<int?>("tier") ?? 0;
            int rank = stats["tier_data"]?.Value<int?>("rank") ?? stats.Value<int?>("rank") ?? 0;
            output.Add(new ChampionTierInsight(
                championId,
                ResolveName(championId),
                normalizedMode,
                tierNumber > 0 ? $"T{tierNumber}" : "-",
                rank,
                ToPercent(stats.Value<double?>("win_rate")),
                ToPercent(stats.Value<double?>("pick_rate"))));
        }
        return output
            .OrderBy(insight => ParseTier(insight.Tier))
            .ThenBy(insight => insight.Rank <= 0 ? int.MaxValue : insight.Rank)
            .ThenBy(insight => insight.ChampionName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ChampionBalanceAdjustment>> GetAramBalanceAdjustmentsAsync(
        CancellationToken cancellationToken = default)
    {
        JObject root = await GetJsonAsync(
            "https://lol-api-champion.op.gg/api/contents/aram-balance",
            cancellationToken).ConfigureAwait(false);
        return (root["data"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            .Select(entry => new ChampionBalanceAdjustment(
                entry.Value<int?>("champion_id") ?? 0,
                ResolveName(entry.Value<int?>("champion_id") ?? 0),
                entry.Value<double?>("damage_dealt") ?? 100,
                entry.Value<double?>("damage_taken") ?? 100,
                entry.Value<double?>("healing") ?? 100,
                entry.Value<double?>("shield_amount") ?? 100,
                entry.Value<double?>("tenacity") ?? 100,
                entry.Value<double?>("cooldown_reduction") ?? 0))
            .Where(entry => entry.ChampionId > 0 &&
                (entry.DamageDealt != 100 || entry.DamageTaken != 100 || entry.Healing != 100 ||
                 entry.ShieldAmount != 100 || entry.Tenacity != 100 || entry.CooldownReduction != 0))
            .OrderBy(entry => entry.ChampionName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("OP.GG 暂时未返回公开英雄数据，请稍后重试。");
        return JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private string ResolveName(int championId)
    {
        string name = _champions.GetDisplayName(championId);
        return string.IsNullOrWhiteSpace(name) ? $"英雄 {championId}" : name;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LOLGameAssistant", "1.0"));
        return http;
    }

    private static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        string kiwi when kiwi.StartsWith("kiwi", StringComparison.Ordinal) => "aram_mayhem",
        "aram" => "aram",
        "arena" or "cherry" => "arena",
        "urf" or "arurf" => "urf",
        "nexusblitz" or "nexus_blitz" => "nexus_blitz",
        _ => "ranked"
    };

    private static int ParseTier(string tier) => int.TryParse(tier.TrimStart('T'), out int value) ? value : int.MaxValue;

    /// <summary>OP.GG 有些模式返回 0~1，有些返回 0~100，统一为百分数供 UI 使用。</summary>
    private static double ToPercent(double? value) => value is null ? 0 : value <= 1 ? value.Value * 100 : value.Value;
}