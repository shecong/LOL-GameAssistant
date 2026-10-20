using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Sgp;

public sealed class SgpRankedStatsService(SgpClient client) : IRankedStatsService
{
    public async Task<RankedOverview?> GetAsync(string puuid, bool isCurrentUser = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return null;
        var context = await client.GetContextAsync(cancellationToken).ConfigureAwait(false);
        var result = await client.ReadAsync(context, $"/leagues-ledge/v2/rankedStats/puuid/{Uri.EscapeDataString(puuid)}", true, cancellationToken).ConfigureAwait(false);
        if (result["queues"] is not JArray queues) throw new IOException("SGP 排位响应缺少队列数据。");
        foreach (JObject queue in queues)
        {
            queue["division"] = queue["rank"]?.DeepClone();
            queue["highestDivision"] = queue["highestRank"]?.DeepClone();
            queue["isProvisional"] = (queue.Value<int?>("provisionalGamesRemaining") ?? 0) > 0;
        }
        return LegacyRankedStatsService.Map(result.ToObject<LolRankedDataParser.RankedData>()!, true);
    }
}
