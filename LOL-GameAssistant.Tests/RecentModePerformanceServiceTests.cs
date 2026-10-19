using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecentModePerformanceServiceTests
{
    [Fact]
    public async Task RequestsOneBatchOfFiveHundredWithoutDetailsOrPagination()
    {
        var source = new FakeHistory(Enumerable.Range(1, 600).Select(id => Game(id)).ToList());
        var result = await new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗");
        Assert.Equal(50, result.SampleSize);
        Assert.Equal([(0, 499)], source.Requests);
        Assert.Equal(0, source.DetailRequests);
    }

    [Fact]
    public async Task ProvidedBatchIsUsedWithoutFetchingAnyOtherPages()
    {
        var source = new FakeHistory([]);
        var first = new MatchHistoryResponse { Games = new() { GameCount = 500,
            Games = Enumerable.Range(1, 20).Select(id => Game(id)).ToList() } };
        var result = await new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗", first);
        Assert.Equal(20, result.SampleSize);
        Assert.Empty(source.Requests);
        Assert.Contains("已获取的 20 条摘要", result.Detail);
    }

    [Fact]
    public async Task FiltersDatesRemakesOtherModesDuplicatesAndMissingFieldsWithoutDetailRequests()
    {
        var valid = Game(1);
        var old = Game(2); old.GameCreation = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds();
        var remake = Game(3); remake.GameDuration = 90;
        var different = Game(4); different.QueueId = 420;
        var missingWin = Game(5); missingWin.Participants[0].stats!.HasWinResult = false;
        var missingKda = Game(6); missingKda.Participants[0].stats!.HasKdaResult = false;
        var negative = Game(7); negative.Participants[0].stats!.deaths = -1;
        var source = new FakeHistory([valid, valid, old, remake, different, missingWin, missingKda, negative]);
        var result = await new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗");
        Assert.Equal(3, result.SampleSize);
        Assert.Equal(4, result.WinSampleSize);
        Assert.Equal(100, result.WinRate);
        Assert.Equal(4.5, result.Kda);
        Assert.Equal(0, source.DetailRequests);
    }

    [Fact]
    public async Task ExplicitZeroKdaSummaryIsAValidSample()
    {
        var zero = Game(1); zero.Participants[0].stats = new() { Win = false };
        var source = new FakeHistory([zero]);
        var result = await new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗");
        Assert.Equal(1, result.SampleSize);
        Assert.Equal(0, result.Kda);
        Assert.Equal(0, result.WinRate);
        Assert.Equal(0, source.DetailRequests);
    }

    [Fact]
    public async Task MissingBatchThrowsInsteadOfInventingAnEmptyRecord()
    {
        var source = new FakeHistory([]) { Unavailable = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesOtherModesOnlyWhenSameModeHasNoValidSamples(bool sameMode)
    {
        var other = Game(1); other.QueueId = 420;
        var rows = new List<MatchHistoryGame> { other };
        if (sameMode) rows.Add(Game(2));
        var result = await new RecentModePerformanceService(new FakeHistory(rows))
            .EvaluateAsync("self", 450, "ARAM", "大乱斗", fallbackToAllModes: true);
        Assert.Equal(1, result.SampleSize);
        Assert.Equal(!sameMode, result.UsesAllModes);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, result.Label);
    }

    [Fact]
    public async Task MissingKdaDoesNotFetchDetailsOrBecomeAZeroSample()
    {
        var game = Game(1); game.Participants[0].stats!.HasKdaResult = false;
        var source = new FakeHistory([game]);
        var result = await new RecentModePerformanceService(source).EvaluateAsync("self", 450, "ARAM", "大乱斗");
        Assert.Equal(0, result.SampleSize);
        Assert.Equal(1, result.WinSampleSize);
        Assert.False(game.Participants[0].stats!.HasKdaResult);
        Assert.Equal(0, source.DetailRequests);
    }

    private static MatchHistoryGame Game(int id) => new()
    {
        GameId = id, GameCreation = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds(),
        QueueId = 450, GameMode = "ARAM", GameDuration = 1200,
        Participants = [new() { participantId = 1, stats = new() { kills = 10, deaths = 4, assists = 8, Win = true } }]
    };

    private sealed class FakeHistory(List<MatchHistoryGame> games) : IMatchHistoryService
    {
        public List<(int Begin, int End)> Requests { get; } = [];
        public int DetailRequests { get; private set; }
        public bool Unavailable { get; init; }
        public Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((beginIndex, endIndex));
            return Task.FromResult<MatchHistoryResponse?>(Unavailable ? null : new() { Games = new()
            { GameCount = games.Count, Games = games.Skip(beginIndex).Take(endIndex - beginIndex + 1).ToList() } });
        }
        public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = 5000,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true,
            CancellationToken cancellationToken = default)
        { DetailRequests++; throw new InvalidOperationException("Scoring must not request details"); }
        public void ClearDetailCache() { }
    }
}
