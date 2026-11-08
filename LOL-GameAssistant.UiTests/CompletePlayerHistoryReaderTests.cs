using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class CompletePlayerHistoryReaderTests
{
    [Fact]
    public async Task ReadsPastFiveThousandUsingRawOffsetsAndIncludesUnreadablePages()
    {
        const int total = 5377;
        var offsets = new List<int>();
        MatchHistoryResponse Page(int begin, int count)
        {
            int consumed = Math.Min(count, total - begin);
            var rows = Enumerable.Range(begin, consumed).Where(i => i < 250 || i >= 350)
                .Select(i => new MatchHistoryGame { GameId = i + 1, QueueId = i % 2 == 0 ? 420 : 450 }).ToList();
            return new() { Games = new() { Games = rows, GameIndexBegin = begin, GameIndexEnd = begin + consumed - 1,
                MissingSummaryCount = consumed - rows.Count, HasMore = consumed == count,
                IsTotalKnown = consumed < count, GameCount = begin + consumed + (consumed == count ? 1 : 0) } };
        }
        var source = Service<IMatchHistoryService>((method, args) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            int begin = (int)args[1]!, count = (int)args[2]! - begin + 1;
            Assert.Equal(100, count); offsets.Add(begin);
            return Task.FromResult<MatchHistoryResponse?>(Page(begin, count));
        });
        var progress = new List<int>();
        var result = (await CompletePlayerHistoryReader.ReadAsync(source, "player", Page(0, 250),
            page => progress.Add(page.Games!.GameCount), default)).Games!;
        Assert.Equal(250, offsets[0]); Assert.True(offsets.Last() > 5000);
        Assert.Equal(total, result.GameCount); Assert.Equal(total - 100, result.Games.Count);
        Assert.Equal(100, result.MissingSummaryCount); Assert.False(result.HasMore); Assert.True(result.IsTotalKnown);
        Assert.Equal(total, result.Games.Last().GameId);
        Assert.Equal(progress.Order(), progress); Assert.Equal(total, progress.Last());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CachedOrRepeatedPagesNeverBecomeCompleteTotals(bool cached)
    {
        MatchHistoryResponse Page(bool more) => new() { Games = new() { GameIndexBegin = more ? 0 : 1,
            GameIndexEnd = more ? 0 : 1, GameCount = 2, HasMore = more, IsTotalKnown = !more,
            Games = [new() { GameId = 42 }] } };
        var next = Page(false); next.IsCached = cached;
        var source = Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(next));
        await Assert.ThrowsAsync<IOException>(() => CompletePlayerHistoryReader.ReadAsync(source, "player", Page(true), _ => { }, default));
    }

    [Fact]
    public async Task CancelingPlayerReadStopsBeforeFetchingAnotherPage()
    {
        using var cancellation = new CancellationTokenSource();
        int requests = 0;
        var source = Service<IMatchHistoryService>((_, _) => { requests++; throw new InvalidOperationException(); });
        var seed = new MatchHistoryResponse { Games = new() { Games = [new() { GameId = 1 }],
            GameCount = 2, GameIndexEnd = 0, HasMore = true, IsTotalKnown = false } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CompletePlayerHistoryReader.ReadAsync(source, "player", seed,
            _ => cancellation.Cancel(), cancellation.Token));
        Assert.Equal(0, requests);
    }
}
