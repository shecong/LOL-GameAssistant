using System.Globalization;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Infrastructure.Pengu;

public sealed record PenguPlayerStatistic(string Kda, string WinRate, int Count, string Scope, string Detail)
{
    public static PenguPlayerStatistic FromAssessment(RecentModePerformanceAssessment? assessment) => new(
        assessment is { SampleSize: > 0 } ? ChampSelectKdaAnnouncementBuilder.FormatKda(assessment.Kda) : "--",
        assessment?.HasWinRate == true ? assessment.WinRate.ToString("F0", CultureInfo.InvariantCulture) + "%" : "--",
        assessment?.SampleSize ?? 0, assessment?.UsesAllModes == true ? "全模式参考" : "同模式",
        assessment?.Detail ?? "战绩暂未获取");
}

public sealed record PenguPlayerStatistics(long Timestamp, int QueueId, string[] Allies,
    IReadOnlyDictionary<string, PenguPlayerStatistic> Players);
