using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>旧排位 LCU 调用的基础设施适配器。</summary>
public sealed class LegacyRankedStatsService : IRankedStatsService
{
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
    private static RankedOverview Map(LolRankedDataParser.RankedData source)
    {
        var result = new RankedOverview();
        IEnumerable<LolRankedDataParser.RankedEntry> entries = (source.QueueMap?.Values ?? Enumerable.Empty<LolRankedDataParser.RankedEntry>())
            .Concat(source.Queues ?? Enumerable.Empty<LolRankedDataParser.RankedEntry>());

        foreach (LolRankedDataParser.RankedEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.QueueType)) continue;
            result.Queues[entry.QueueType] = new RankedQueue
            {
                QueueType = entry.QueueType,
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