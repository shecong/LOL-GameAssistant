using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchHistoryPagingTests
{
    [Theory]
    [InlineData(0, 9, 10)]
    [InlineData(10, 19, 10)]
    [InlineData(100, 109, 1)]
    [InlineData(110, 119, 0)]
    public void IgnoredServerPagingIsSlicedWithoutRepeatingTheFirstPage(int begin, int end, int expectedCount)
    {
        var source = Response(0, 100, 101);
        var result = LegacyMatchReadModelMapper.ToDomainPage(source, begin, end)!.Games!;
        Assert.Equal(expectedCount, result.Games.Count);
        Assert.Equal(1234, result.GameCount);
        Assert.Equal(begin, result.GameIndexBegin);
        Assert.Equal(begin + expectedCount - 1, result.GameIndexEnd);
        Assert.Equal(Enumerable.Range(begin, expectedCount).Select(id => (long)id), result.Games.Select(game => game.GameId));
        Assert.Equal(101, source.Games!.Games!.Count);
    }

    [Fact]
    public void CorrectServerPagingDoesNotApplyTheOffsetTwice()
    {
        var result = LegacyMatchReadModelMapper.ToDomainPage(Response(20, 29, 10), 20, 29)!.Games!;
        Assert.Equal(Enumerable.Range(20, 10).Select(id => (long)id), result.Games.Select(game => game.GameId));
    }

    [Fact]
    public void MissingRangeMetadataKeepsAnAlreadyLimitedResponse()
    {
        var source = Response(20, 29, 10);
        source.Games!.GameIndexBegin = 0;
        source.Games.GameIndexEnd = 0;
        var result = LegacyMatchReadModelMapper.ToDomainPage(source, 20, 29)!.Games!;
        Assert.Equal(Enumerable.Range(20, 10).Select(id => (long)id), result.Games.Select(game => game.GameId));
    }

    private static GameHeadModel.MatchHistoryResponse Response(int begin, int end, int count) => new()
    {
        Games = new()
        {
            GameIndexBegin = begin, GameIndexEnd = end, GameCount = 1234,
            Games = Enumerable.Range(begin, count).Select(id => new GameHeadModel.GameInfo { GameId = id }).ToList()
        }
    };
}
