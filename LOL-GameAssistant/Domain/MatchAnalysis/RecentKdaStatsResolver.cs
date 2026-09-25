using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>区分战绩摘要缺失的零值与详情确认过的 0/0/0。</summary>
public static class RecentKdaStatsResolver
{
    public static bool NeedsDetail(MatchParticipantStats? summary) =>
        summary == null ||
        (summary.kills == 0 && summary.deaths == 0 && summary.assists == 0);

    public static MatchParticipantStats? Resolve(
        MatchParticipantStats? summary, MatchParticipantStats? detail) =>
        NeedsDetail(summary) ? detail : summary;
}
