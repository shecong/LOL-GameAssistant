using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class ChampionSelectionSnapshotMapperTests
{
    [Fact]
    public void SelectionEvent_ExposesBenchWithoutPlayerOrModeData()
    {
        var snapshot = ChampionSelectionSnapshotMapper.ParseEvent(
            """{"benchChampions":[{"championId":777},{"championId":22},{"championId":777},{"championId":0},null]}""");
        Assert.NotNull(snapshot);
        Assert.Equal([777, 22], snapshot.BenchChampionIds);
        Assert.Empty(snapshot.MyTeam);
    }

    [Fact]
    public void FollowingEvent_ClearsBenchAndPreservesPlayerContext()
    {
        var snapshot = ChampionSelectionSnapshotMapper.ParseEvent(
            """{"localPlayerCellId":3,"benchChampions":[],"myTeam":[{"cellId":3,"championId":777,"puuid":"local"}]}""");
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.BenchChampionIds);
        Assert.Equal(3, snapshot.LocalPlayerCellId);
        Assert.Equal("local", Assert.Single(snapshot.MyTeam).Puuid);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"errorCode\":\"NOT_FOUND\"}")]
    [InlineData("[8,")]
    public void InvalidEvent_DoesNotReplaceCurrentBench(string data) =>
        Assert.Null(ChampionSelectionSnapshotMapper.ParseEvent(data));

    [Fact]
    public void NullableFields_DoNotBreakBenchUpdates()
    {
        var snapshot = ChampionSelectionSnapshotMapper.ParseEvent(
            """{"benchChampions":[{"championId":22}],"actions":[null,[null]],"myTeam":null,"theirTeam":[null]}""");
        Assert.NotNull(snapshot);
        Assert.Equal([22], snapshot.BenchChampionIds);
        Assert.Empty(snapshot.MyTeam);
        Assert.Empty(snapshot.TheirTeam);
    }
}
