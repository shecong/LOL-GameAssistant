using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>区分战绩摘要缺失的零值与详情确认过的 0/0/0。</summary>
public static class RecentKdaStatsResolver
{
    /// <summary>摘要缺失或 KDA 全为零时，要求读取详情以确认真实数据。</summary>
    public static bool NeedsDetail(MatchParticipantStats? summary) =>
        summary == null ||
        (summary.kills == 0 && summary.deaths == 0 && summary.assists == 0);

    /// <summary>摘要缺失或 KDA 全零时使用详情统计，否则保留摘要数据。</summary>
    public static MatchParticipantStats? Resolve(
        MatchParticipantStats? summary, MatchParticipantStats? detail) =>
        NeedsDetail(summary) ? detail : summary;
}