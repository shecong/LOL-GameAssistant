using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class PlayerHistoryRepositoryTests
{
    [Fact]
    public async Task SharedBatchSurvivesWaiterCancellationAndCopiesMutableModels()
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Page = _ => pending.Task };
        var repository = new PlayerHistoryRepository(source);
        using var cancellation = new CancellationTokenSource();
        var first = repository.GetBatchAsync("a", cancellation.Token);
        var second = repository.GetBatchAsync("a");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(source.RequestToken.IsCancellationRequested);
        pending.SetResult(Batch());
        var snapshot = await second;
        snapshot.Data!.Games!.Games.Clear();
        Assert.Single((await repository.GetBatchAsync("a")).Data!.Games!.Games);
        Assert.Equal(1, source.PageCalls);
        Assert.False(snapshot.IsCompleteHistory);
    }

    [Fact]
    public async Task FreshnessStartsAtCompletionAndFailureRetainsOriginalTimestamp()
    {
        var clock = new Clock();
        var pending = new TaskCompletionSource<MatchHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Page = _ => pending.Task };
        var repository = new PlayerHistoryRepository(source, clock);
        var read = repository.GetBatchAsync("a");
        clock.Advance(TimeSpan.FromMinutes(3));
        pending.SetResult(Batch());
        var fresh = await read;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(fresh.AcquiredAt, (await repository.GetBatchAsync("a")).AcquiredAt);
        Assert.Equal(1, source.PageCalls);
        clock.Advance(TimeSpan.FromMinutes(2));
        source.Page = _ => Task.FromException<MatchHistoryResponse?>(new IOException("offline"));
        var fallback = await repository.GetBatchAsync("a");
        Assert.True(fallback.IsStale);
        Assert.Equal(fresh.AcquiredAt, fallback.AcquiredAt);
        Assert.Single(fallback.Data!.Games!.Games);
        Assert.Equal(2, source.PageCalls);
    }

    [Fact]
    public async Task ContextSwitchCannotReuseAnotherAccountsSnapshot()
    {
        string context = "one";
        var source = new Source();
        var repository = new PlayerHistoryRepository(source, context: () => context);
        await repository.GetBatchAsync("a");
        context = "two";
        Assert.Null(repository.Peek("a"));
        await repository.GetBatchAsync("a");
        Assert.Equal(2, source.PageCalls);
    }

    [Fact]
    public async Task DetailsShareInflightWorkWithoutCancellingOtherReaders()
    {
        var pending = new TaskCompletionSource<MatchDetail?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Detail = pending.Task };
        var repository = new PlayerHistoryRepository(source);
        using var cancellation = new CancellationTokenSource();
        var first = repository.GetDetailAsync(1, cancellation.Token);
        var second = repository.GetDetailAsync(1);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        pending.SetResult(new() { GameId = 1 });
        Assert.Equal(1, (await second)!.GameId);
        Assert.Equal(1, source.DetailCalls);
        Assert.Equal(MatchDataPolicy.DetailConcurrency, repository.AvailableDetailSlots);
    }

    [Fact]
    public async Task ClearDuringReadCannotRestoreAnOldSnapshot()
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Page = _ => pending.Task };
        var repository = new PlayerHistoryRepository(source);
        var old = repository.GetBatchAsync("a");
        repository.Clear();
        pending.SetResult(Batch());
        await old;
        Assert.Null(repository.Peek("a"));
        await repository.GetBatchAsync("a");
        Assert.Equal(2, source.PageCalls);
    }

    private static MatchHistoryResponse Batch() => new() { Games = new() { Games = [new() { GameId = 1 }] } };
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }
    private sealed class Source : IMatchHistoryService
    {
        public Func<CancellationToken, Task<MatchHistoryResponse?>> Page { get; set; } = _ => Task.FromResult<MatchHistoryResponse?>(Batch());
        public Task<MatchDetail?> Detail { get; set; } = Task.FromResult<MatchDetail?>(null);
        public int PageCalls, DetailCalls;
        public CancellationToken RequestToken;
        public Task<MatchHistoryResponse?> GetPageAsync(string puuid, int beginIndex, int endIndex, CancellationToken cancellationToken = default)
        {
            Assert.Equal(0, beginIndex); Assert.Equal(499, endIndex);
            PageCalls++; RequestToken = cancellationToken; return Page(cancellationToken);
        }
        public Task<MatchDetail?> GetDetailAsync(long gameId, bool useCache = true, CancellationToken cancellationToken = default)
        { DetailCalls++; return Detail; }
        public Task<MatchHistoryResponse?> GetAllAsync(string puuid, int maxGames = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ClearDetailCache() { }
    }
}
