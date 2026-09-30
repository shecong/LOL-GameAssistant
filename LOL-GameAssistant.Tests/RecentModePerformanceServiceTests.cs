using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecentModePerformanceServiceTests
{
    [Fact]
    public async Task ReadsEveryPageWithinThirtyDaysAndCountsAllSameModeGames()
    {
        long recent = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        long old = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds();
        var games = Enumerable.Range(1, 105).Select(id => Game(id, recent, 450)).ToList();
        games.Add(Game(106, old, 450));
        var source = new FakeHistory(games);

        RecentModePerformanceAssessment result = await new RecentModePerformanceService(source)
            .EvaluateAsync("self", 450, "ARAM", "极地大乱斗");

        Assert.Equal(105, result.SampleSize);
        Assert.Equal(2, source.PageRequests);
        Assert.Equal(RecentPerformanceLabel.Upper, result.Label);
    }

    [Fact]
    public async Task FiltersOlderModesAndRemakesBeforeCheckingFiveGameMinimum()
    {
        long recent = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        long old = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds();
        var games = Enumerable.Range(1, 4).Select(id => Game(id, recent, 450)).ToList();
        games.Add(Game(5, recent, 420));
        games.Add(Game(6, old, 450));
        MatchHistoryGame remake = Game(7, recent, 450);
        remake.GameDuration = 90;
        games.Add(remake);

        RecentModePerformanceAssessment result = await new RecentModePerformanceService(new FakeHistory(games))
            .EvaluateAsync("self", 450, "ARAM", "极地大乱斗");

        Assert.Equal(4, result.SampleSize);
        Assert.False(result.HasEnoughSample);
        Assert.Equal(RecentPerformanceLabel.InsufficientData, result.Label);
    }

    private static MatchHistoryGame Game(int id, long created, int queueId) => new()
    {
        GameId = id,
        GameCreation = created,
        GameMode = "ARAM",
        QueueId = queueId,
        GameDuration = 1200,
        Participants = [new MatchParticipant
        {
            participantId = 1,
            stats = new MatchParticipantStats { kills = 10, deaths = 4, assists = 8, Win = true }
        }]
    };

    private sealed class FakeHistory(List<MatchHistoryGame> games) : IMatchHistoryService
    {
        public int PageRequests { get; private set; }

        public Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex,
            CancellationToken cancellationToken = default)
        {
            PageRequests++;
            return Task.FromResult<MatchHistoryResponse?>(new MatchHistoryResponse
            {
                Games = new MatchHistoryGames
                {
                    GameCount = games.Count,
                    Games = games.Skip(beginIndex).Take(endIndex - beginIndex + 1).ToList()
                }
            });
        }

        public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = 5000,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void ClearDetailCache() { }
    }
}
