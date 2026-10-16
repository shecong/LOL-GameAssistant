using System.Net;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchHistoryRequestPolicyTests
{
    [Theory]
    [InlineData(LcuFailure.Timeout, null)]
    [InlineData(LcuFailure.Network, null)]
    [InlineData(LcuFailure.HttpStatus, 429)]
    [InlineData(LcuFailure.HttpStatus, 503)]
    public async Task TransientReadFailureBacksOffOnceThenRecovers(LcuFailure failure, int? status)
    {
        int calls = 0, delays = 0;
        var policy = new MatchHistoryRequestPolicy(backoff: _ => { delays++; return Task.CompletedTask; });
        int result = await policy.ExecuteAsync(_ => ++calls == 1
            ? Task.FromException<int>(new LcuRequestException(failure, (HttpStatusCode?)status))
            : Task.FromResult(42), default);
        Assert.Equal(42, result); Assert.Equal(2, calls); Assert.Equal(1, delays);
    }

    [Theory]
    [InlineData(LcuFailure.HttpStatus, 401)]
    [InlineData(LcuFailure.HttpStatus, 404)]
    [InlineData(LcuFailure.NoCredentials, null)]
    [InlineData(LcuFailure.InvalidJson, null)]
    public async Task PermanentFailuresAreNotRetried(LcuFailure failure, int? status)
    {
        int calls = 0;
        var policy = new MatchHistoryRequestPolicy(backoff: _ => throw new Exception("Must not retry"));
        await Assert.ThrowsAsync<LcuRequestException>(() => policy.ExecuteAsync<int>(_ => {
            calls++; throw new LcuRequestException(failure, (HttpStatusCode?)status);
        }, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RepeatedTimeoutStopsAfterTwoAttemptsAndReleasesCapacity()
    {
        int calls = 0;
        var policy = new MatchHistoryRequestPolicy(concurrency: 1, requestTimeout: TimeSpan.FromMilliseconds(20),
            backoff: _ => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<LcuRequestException>(() => policy.ExecuteAsync(async token => {
            calls++; await Task.Delay(Timeout.InfiniteTimeSpan, token); return 1;
        }, default));
        Assert.Equal(LcuFailure.Timeout, error.Failure); Assert.Equal(2, calls);
        Assert.Equal(42, await policy.ExecuteAsync(_ => Task.FromResult(42), default));
    }

    [Fact]
    public async Task ConcurrentPlayersShareLimitAndQueuedCancellationNeverSendsRequest()
    {
        var policy = new MatchHistoryRequestPolicy(concurrency: 1);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = policy.ExecuteAsync(_ => release.Task, default);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var queued = policy.ExecuteAsync(_ => { calls++; return Task.FromResult(2); }, cancellation.Token);
        Assert.Equal(0, calls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.SetResult(1); Assert.Equal(1, await first);
        Assert.Equal(0, calls);
        Assert.Equal(3, await policy.ExecuteAsync(_ => Task.FromResult(3), default));
    }

    [Fact]
    public async Task BackoffDoesNotOccupySlotAndCancellationPreventsRetry()
    {
        var delaying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var policy = new MatchHistoryRequestPolicy(concurrency: 1, backoff: async token => {
            delaying.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        int calls = 0;
        var first = policy.ExecuteAsync<int>(_ => {
            calls++; throw new LcuRequestException(LcuFailure.Timeout, null);
        }, cancellation.Token);
        await delaying.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, await policy.ExecuteAsync(_ => Task.FromResult(2), default));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CallerCancellationDuringRequestDoesNotRetry()
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        var policy = new MatchHistoryRequestPolicy(backoff: _ => throw new Exception("Must not retry"));
        var request = policy.ExecuteAsync(async token => {
            calls++; await Task.Delay(Timeout.InfiniteTimeSpan, token); return 1;
        }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, calls);
    }
}
