using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguPlayerStatisticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengu-statistics", Guid.NewGuid().ToString("N"));
    [Fact]
    public void CardUsesChatFormattingIncludingTruncationAndUnknownResults()
    {
        var assessment = new RecentModePerformanceAssessment(MatchPerformanceTier.Medium, 60, 50, 40,
            "same assessment", 4.499, true) { UsesAllModes = true };
        var card = PenguPlayerStatistic.FromAssessment(assessment);
        string chat = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, assessment, "Player"));
        Assert.Equal("4.49", card.Kda);
        Assert.Equal("40%", card.WinRate);
        Assert.Contains("KDA" + card.Kda, chat);
        Assert.Contains("胜率" + card.WinRate, chat);
        Assert.Equal("全模式参考", card.Scope);
        Assert.Equal("--", PenguPlayerStatistic.FromAssessment(null).Kda);
        Assert.Equal("--", PenguPlayerStatistic.FromAssessment(assessment with { WinSampleSize = 0 }).WinRate);
    }

    [Fact]
    public void PublishingResultsPreservesOptionsAndSavingOlderOptionsPreservesLatestResults()
    {
        var service = new PenguVisualSettingsService(() => _root);
        var options = service.Save(new() { Enabled = false, ShowBadges = false, WallpaperEnabled = false });
        var assessment = new RecentModePerformanceAssessment(MatchPerformanceTier.Medium, 55, 50, 44, "recent50", 2.10);
        var snapshot = new PenguPlayerStatistics(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 420, ["alice"],
            new Dictionary<string, PenguPlayerStatistic> { ["alice"] = PenguPlayerStatistic.FromAssessment(assessment) });
        service.PublishStatistics(snapshot);
        var updated = service.Read();
        Assert.False(updated.Enabled || updated.ShowBadges || updated.WallpaperEnabled);
        Assert.Equal(options.Revision, updated.Revision);
        Assert.Equal("2.10", updated.PlayerStatistics!.Players["alice"].Kda);
        service.Save(options with { ShowCounters = false });
        Assert.Equal("44%", service.Read().PlayerStatistics!.Players["alice"].WinRate);
        service.PublishStatistics(null);
        Assert.Null(service.Read().PlayerStatistics);
        Assert.False(service.Read().Enabled);
    }

    [Fact]
    public void PublishingDoesNotCreateAnInstallation()
    {
        new PenguVisualSettingsService(() => _root).PublishStatistics(null);
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
