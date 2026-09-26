using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecentKdaStatsResolverTests
{
    [Fact]
    public void EmptySummary_UsesDetailStats()
    {
        var summary = new MatchParticipantStats();
        var detail = new MatchParticipantStats { kills = 7, deaths = 3, assists = 11 };

        Assert.True(RecentKdaStatsResolver.NeedsDetail(summary));
        Assert.Same(detail, RecentKdaStatsResolver.Resolve(summary, detail));
        Assert.Null(RecentKdaStatsResolver.Resolve(summary, null));
    }

    [Fact]
    public void PopulatedSummary_DoesNotRequireAnotherRequest()
    {
        var summary = new MatchParticipantStats { kills = 0, deaths = 4, assists = 0 };

        Assert.False(RecentKdaStatsResolver.NeedsDetail(summary));
        Assert.Same(summary, RecentKdaStatsResolver.Resolve(summary, null));
    }
}