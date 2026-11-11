using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class ChampSelectKdaReadinessTests
{
    [Fact]
    public async Task LateConversationAndRejectedPostRetryUntilOneSuccessfulDelivery()
    {
        int lookups = 0, writes = 0;
        var lcu = Service<ILcuRequestSender>((method, _) =>
        {
            if (method.Name == "GetStringAsync")
                return Task.FromResult<string?>(++lookups == 1 ? "[]" :
                    "[{\"id\":\"session\",\"type\":\"championSelect\"}]");
            return Task.FromResult(++writes > 1);
        });
        var chat = new LcuChampionSelectChatService(lcu);
        var delivery = new ChampionSelectAnnouncementDelivery();
        Task<bool> Send(CancellationToken token) => SendCore(token);
        async Task<bool> SendCore(CancellationToken token) => (await chat.SendAsync("KDA", token)).Succeeded;
        await delivery.SendAsync(Send, () => true, default, TimeSpan.Zero, initialDelay: TimeSpan.Zero);
        Assert.True(delivery.IsSent);
        Assert.False(delivery.IsSending);
        Assert.Equal(3, lookups);
        Assert.Equal(2, writes);
        await delivery.SendAsync(Send, () => true, default, TimeSpan.Zero);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task ConcurrentRefreshCannotStartSecondSendWhileFirstIsPending()
    {
        int attempts = 0;
        var pending = new TaskCompletionSource<bool>();
        var delivery = new ChampionSelectAnnouncementDelivery();
        Task<bool> Send(CancellationToken _) { attempts++; return pending.Task; }
        var first = delivery.SendAsync(Send, () => true, default, initialDelay: TimeSpan.Zero);
        Assert.True(delivery.IsSending);
        Assert.False(delivery.IsSent);
        await delivery.SendAsync(Send, () => true, default);
        Assert.Equal(1, attempts);
        pending.SetResult(true);
        await first;
        Assert.True(delivery.IsSent);
    }

    [Fact]
    public async Task RosterCancellationStopsPendingRetryWithoutMarkingSent()
    {
        using var cancellation = new CancellationTokenSource();
        int attempts = 0;
        var delivery = new ChampionSelectAnnouncementDelivery();
        var sending = delivery.SendAsync(_ => { attempts++; return Task.FromResult(false); },
            () => true, cancellation.Token, initialDelay: TimeSpan.Zero);
        cancellation.Cancel();
        await sending;
        Assert.Equal(1, attempts);
        Assert.False(delivery.IsSent);
        Assert.False(delivery.IsSending);
    }

    [Fact]
    public async Task LeavingChampSelectStopsRetry()
    {
        bool current = true;
        int attempts = 0;
        var delivery = new ChampionSelectAnnouncementDelivery();
        await delivery.SendAsync(_ => { attempts++; current = false; return Task.FromResult(false); },
            () => current, default, TimeSpan.Zero, initialDelay: TimeSpan.Zero);
        Assert.Equal(1, attempts);
        Assert.False(delivery.IsSent);
    }

    [Fact]
    public async Task DefaultDelayCanBeCancelledBeforeAnyChatRequest()
    {
        using var cancellation = new CancellationTokenSource();
        int attempts = 0;
        var delivery = new ChampionSelectAnnouncementDelivery();
        var sending = delivery.SendAsync(_ => { attempts++; return Task.FromResult(true); },
            () => true, cancellation.Token);
        Assert.True(delivery.IsSending);
        Assert.Equal(0, attempts);
        cancellation.Cancel();
        await sending;
        Assert.Equal(0, attempts);
        Assert.False(delivery.IsSent);
        Assert.False(delivery.IsSending);
    }

    [Fact]
    public async Task StageIsRecheckedAfterInitialDelay()
    {
        bool current = true;
        int attempts = 0;
        var delivery = new ChampionSelectAnnouncementDelivery();
        var sending = delivery.SendAsync(_ => { attempts++; return Task.FromResult(true); }, () => current, default);
        Assert.Equal(0, attempts);
        current = false;
        await sending;
        Assert.Equal(0, attempts);
        Assert.False(delivery.IsSent);
    }

    private static PlayerRecentPerformanceEventArgs Ready(string puuid, bool ally = true) => new(puuid, puuid, ally,
        new RecentModePerformanceAssessment(MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper));

    [Fact]
    public void MustWaitForEveryAllysCalculatedResultAndIgnoreEnemyEvents()
    {
        HashSet<string> expected = ["a", "b"];
        var results = new Dictionary<string, PlayerRecentPerformanceEventArgs>();
        bool ReadyToSend() => LiveGameForm.AreChampSelectAssessmentsReady(expected, results);
        Assert.False(ReadyToSend());
        results["a"] = Ready("a"); Assert.False(ReadyToSend());
        results["enemy"] = Ready("enemy", false); Assert.False(ReadyToSend());
        results["b"] = new("b", "b", true, null); Assert.False(ReadyToSend());
        // A failed load is not a completed KDA calculation; a successful retry unlocks the announcement.
        results["b"] = Ready("b"); Assert.True(ReadyToSend());
        results["b"] = new("b", "", true, Ready("b").Assessment, isDisplayNameReady: false);
        Assert.False(ReadyToSend());
        results["b"] = Ready("b"); Assert.True(ReadyToSend());
        results["b"] = Ready("b", false); Assert.False(ReadyToSend());
        Assert.False(LiveGameForm.AreChampSelectAssessmentsReady(new HashSet<string>(), results));
    }

    [Fact]
    public async Task StageRosterOrSettingChangeDuringChatLookupPreventsPost()
    {
        bool valid = true; int writes = 0;
        var pending = new TaskCompletionSource<string?>();
        var lcu = Service<ILcuRequestSender>((method, _) => method.Name == "GetStringAsync" ? pending.Task : Write());
        Task<bool> Write() { writes++; return Task.FromResult(true); }
        var service = new LcuChampionSelectChatService(lcu);
        var sending = service.SendAsync("KDA calculated", () => valid);
        Assert.False(sending.IsCompleted);
        valid = false;
        pending.SetResult("[{\"id\":\"old-session\",\"type\":\"championSelect\"}]");
        Assert.False((await sending).Succeeded); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task CancelledOldRosterCannotSendIntoAnotherConversation()
    {
        int writes = 0;
        var pending = new TaskCompletionSource<string?>();
        var lcu = Service<ILcuRequestSender>((method, _) => method.Name == "GetStringAsync" ? pending.Task : Write());
        Task<bool> Write() { writes++; return Task.FromResult(true); }
        using var cancellation = new CancellationTokenSource();
        var sending = new LcuChampionSelectChatService(lcu).SendAsync("old KDA", () => true, cancellation.Token);
        cancellation.Cancel(); pending.SetResult("[{\"id\":\"new-session\",\"type\":\"championSelect\"}]");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending); Assert.Equal(0, writes);
    }

    [Fact]
    public async Task ReadyAndUnchangedStatePostsOnce()
    {
        int writes = 0;
        var lcu = Service<ILcuRequestSender>((method, args) =>
        {
            if (method.Name == "GetStringAsync") return Task.FromResult<string?>("[{\"id\":\"session\",\"type\":\"championSelect\"}]");
            Assert.Equal("PostAsync", method.Name); Assert.Contains("KDA calculated", (string)args[1]!);
            writes++; return Task.FromResult(true);
        });
        Assert.True((await new LcuChampionSelectChatService(lcu).SendAsync("KDA calculated", () => true)).Succeeded);
        Assert.Equal(1, writes);
    }
}
