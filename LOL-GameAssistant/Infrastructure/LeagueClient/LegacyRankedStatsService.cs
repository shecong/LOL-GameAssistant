using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>旧排位 LCU 调用的基础设施适配器。</summary>
public sealed class LegacyRankedStatsService : IRankedStatsService
{
    /// <summary>调用旧排位接口并将结果转换为领域排位概况。</summary>
    public async Task<RankedOverview?> GetAsync(
        string puuid,
        bool isCurrentUser = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LolRankedDataParser.RankedData? legacy = await Game_Api
            .GetRankedStatsAsync(puuid, isCurrentUser)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : Map(legacy);
    }

    /// <summary>集中吸收旧 LCU 解析器字段，避免其 DTO 穿透应用边界。</summary>
    internal static RankedOverview Map(LolRankedDataParser.RankedData source)
    {
        var result = new RankedOverview();
        // queueMap is the parser's preferred source. The duplicate queues array must
        // not overwrite it with a stale/empty record, and map keys identify queues
        // even when the nested queueType is omitted.
        var entries = (source.Queues ?? Enumerable.Empty<LolRankedDataParser.RankedEntry>())
            .Select(entry => (QueueType: entry.QueueType, Entry: entry))
            .Concat((source.QueueMap ?? new()).Select(pair => (QueueType: pair.Key, Entry: pair.Value)));

        foreach (var (queueType, entry) in entries)
        {
            if (string.IsNullOrWhiteSpace(queueType)) continue;
            result.Queues[queueType] = new RankedQueue
            {
                QueueType = queueType,
                Tier = entry.Tier,
                Division = entry.Division,
                LeaguePoints = entry.LeaguePoints,
                Wins = entry.Wins,
                Losses = entry.Losses,
                IsProvisional = entry.IsProvisional,
                ProvisionalGamesRemaining = entry.ProvisionalGamesRemaining,
                ProvisionalGameThreshold = entry.ProvisionalGameThreshold,
                MiniSeriesProgress = entry.MiniSeriesProgress,
                RatedRating = entry.RatedRating,
                RatedTier = entry.RatedTier,
                HighestTier = entry.HighestTier,
                HighestDivision = entry.HighestDivision
            };
        }

        foreach ((string queueType, LolRankedDataParser.SeasonInfo season) in source.Seasons ?? new Dictionary<string, LolRankedDataParser.SeasonInfo>())
        {
            if (season.CurrentSeasonEnd > 0)
                result.SeasonEndsAt[queueType] = season.SeasonEndDateTime;
        }

        return result;
    }
}
