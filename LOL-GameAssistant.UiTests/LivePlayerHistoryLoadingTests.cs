using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LivePlayerHistoryLoadingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedHistoryStopsShimmerAndRetryRecovers(bool throws) => MatchListScrollingTests.OnUiThread(() =>
    {
        int requests = 0;
        var card = CreateCard((method, _) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            if (++requests == 1)
                return throws
                    ? Task.FromException<MatchHistoryResponse?>(new IOException("temporary failure"))
                    : Task.FromResult<MatchHistoryResponse?>(null);
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() });
        });
        using var form = Mount(card);
        var panel = Child(card, "panelMatches");
        Assert.Empty(panel.Controls.OfType<ShimmerPanel>());
        Assert.Equal("获取失败", Child(card, "lblSummary").Text);
        var retry = Child(card, "btnRetryHistory");
        // Raise the actual click event so the cached null/faulted request is exercised too.
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(retry, [EventArgs.Empty]);
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(2, requests);
        Assert.Empty(panel.Controls.OfType<ShimmerPanel>());
        Assert.DoesNotContain(panel.Controls.Cast<Control>(), control => control.Name == "btnRetryHistory");
        Assert.Contains(panel.Controls.Cast<Control>(), control => control.Text == "暂无战绩");
    });

    [Fact]
    public void UnavailableDetailsStopShimmerAndOfferRetry() => MatchListScrollingTests.OnUiThread(() =>
    {
        long gameId = DateTime.UtcNow.Ticks;
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [new() { GameId = gameId }] } })
            : Task.FromResult<MatchDetail?>(null));
        using var form = Mount(card);
        Assert.Empty(Child(card, "panelMatches").Controls.OfType<ShimmerPanel>());
        Assert.NotNull(Child(card, "btnRetryHistory"));
        Assert.Equal("获取失败", Child(card, "lblSummary").Text);
    });

    [Fact]
    public void PendingAndFailedScoringKeepLoadedRows() => MatchListScrollingTests.OnUiThread(() =>
    {
        var nextPage = new TaskCompletionSource<MatchHistoryResponse?>();
        long firstId = DateTime.UtcNow.Ticks;
        var page = new MatchHistoryResponse
        {
            Games = new()
            {
                GameCount = 101,
                Games = Enumerable.Range(0, 100).Select(index => new MatchHistoryGame
                {
                    GameId = firstId + index, GameCreation = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    QueueId = 420, GameMode = "CLASSIC"
                }).ToList()
            }
        };
        string puuid = Guid.NewGuid().ToString();
        var card = CreateCard((method, args) => method.Name == "GetPageAsync"
            ? ((int)args[1]! == 0 ? Task.FromResult<MatchHistoryResponse?>(page) : nextPage.Task)
            : Task.FromResult<MatchDetail?>(new()
            {
                GameId = (long)args[0]!, gameDuration = 1200, queueId = "420",
                participantIdentities = [new() { participantId = 1, player = new() { Puuid = puuid } }],
                participants = [new() { participantId = 1, stats = new() { kills = 5, deaths = 2, assists = 8 } }]
            }), puuid: puuid);
        using var form = Mount(card);
        var panel = Child(card, "panelMatches");
        Assert.False(nextPage.Task.IsCompleted);
        Assert.Equal(10, panel.Controls.OfType<RecentMatchRow>().Count());
        Assert.Empty(panel.Controls.OfType<ShimmerPanel>());
        Assert.Equal("评分中", Child(card, "lblSummary").Text);
        nextPage.SetException(new IOException("scoring failed"));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(10, panel.Controls.OfType<RecentMatchRow>().Count());
        Assert.Equal("评分暂不可用", Child(card, "lblSummary").Text);
    });

    [Fact]
    public void PendingProfileIconDoesNotBlockHistory() => MatchListScrollingTests.OnUiThread(() =>
    {
        var icon = new TaskCompletionSource<byte[]?>();
        int requests = 0;
        var card = CreateCard((_, _) =>
        {
            requests++;
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() });
        }, icon: icon.Task);
        using var form = Mount(card);
        Assert.False(icon.Task.IsCompleted);
        Assert.Equal(1, requests);
        Assert.Empty(Child(card, "panelMatches").Controls.OfType<ShimmerPanel>());
        icon.SetResult(null);
        System.Windows.Forms.Application.DoEvents();
    });

    [Fact]
    public void PendingProfileDoesNotBlockHistoryOrAssessment() => MatchListScrollingTests.OnUiThread(() =>
    {
        var profile = new TaskCompletionSource<PlayerProfile?>();
        int requests = 0;
        var card = CreateCard((_, _) =>
        {
            requests++;
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() });
        }, profile: profile.Task);
        int assessments = 0;
        card.RecentPerformanceReady += (_, _) => assessments++;
        using var form = Mount(card);
        Assert.False(profile.Task.IsCompleted);
        Assert.Equal(1, requests);
        Assert.Equal(1, assessments);
        Assert.Empty(Child(card, "panelMatches").Controls.OfType<ShimmerPanel>());
        profile.SetResult(new("", "补齐名称", "12345", 0, 123, 0, 0));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal("补齐名称", Child(card, "lblName").Text);
        Assert.Contains("12345", Child(card, "lblSub").Text);
        Assert.Equal(1, requests);
    });

    [Fact]
    public void EachFinishedDetailAppearsImmediatelyAndRowsStayNewestFirst() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        long newestId = DateTime.UtcNow.Ticks;
        var newest = new TaskCompletionSource<MatchDetail?>();
        var older = new TaskCompletionSource<MatchDetail?>();
        var card = CreateCard((method, args) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(newestId, 2))
            : ((long)args[0]! == newestId ? newest.Task : older.Task), puuid: puuid);
        using var form = Mount(card);
        var panel = Child(card, "panelMatches");
        older.SetResult(Detail(newestId + 1, puuid));
        System.Windows.Forms.Application.DoEvents();
        var olderRow = Assert.Single(panel.Controls.OfType<RecentMatchRow>());
        Assert.False(newest.Task.IsCompleted);
        Assert.Empty(panel.Controls.OfType<ShimmerPanel>());
        newest.SetResult(Detail(newestId, puuid));
        System.Windows.Forms.Application.DoEvents();
        var rows = panel.Controls.OfType<RecentMatchRow>().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Same(olderRow, rows[1]);
        Assert.True(rows[0].Top < rows[1].Top);
    });

    [Fact]
    public void PlayerQueuesAtMostTwoDetailsAtOnce() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        long firstId = DateTime.UtcNow.Ticks;
        var requests = new List<long>();
        var pending = Enumerable.Range(0, 4).ToDictionary(index => firstId + index,
            _ => new TaskCompletionSource<MatchDetail?>());
        var card = CreateCard((method, args) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(firstId, 4));
            long id = (long)args[0]!;
            requests.Add(id);
            return pending[id].Task;
        }, puuid: puuid);
        using var form = Mount(card);
        Assert.Equal(2, requests.Count);
        pending[firstId].SetResult(Detail(firstId, puuid));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(3, requests.Count);
        Assert.False(pending[firstId + 1].Task.IsCompleted);
        foreach (var (id, response) in pending) response.TrySetResult(Detail(id, puuid));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(4, requests.Count);
        Assert.Equal(4, Child(card, "panelMatches").Controls.OfType<RecentMatchRow>().Count());
    });

    [Fact]
    public void TransientDetailFailureRetriesWithoutDiscardingOtherRows() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        long id = DateTime.UtcNow.Ticks;
        int detailRequests = 0;
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(id, 1))
            : (++detailRequests == 1
                ? Task.FromResult<MatchDetail?>(null)
                : Task.FromResult<MatchDetail?>(Detail(id, puuid))), puuid: puuid);
        using var form = Mount(card);
        Assert.Equal(2, detailRequests);
        Assert.Single(Child(card, "panelMatches").Controls.OfType<RecentMatchRow>());
        Assert.Empty(Child(card, "panelMatches").Controls.OfType<ShimmerPanel>());
    });

    [Fact]
    public void TimedOutDetailRetriesWhileCompletedRowRemainsVisible() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        long id = DateTime.UtcNow.Ticks;
        var stalled = new TaskCompletionSource<MatchDetail?>();
        int slowRequests = 0;
        CancellationToken firstToken = default;
        var card = CreateCard((method, args) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(id, 2));
            long requestedId = (long)args[0]!;
            if (requestedId != id) return Task.FromResult<MatchDetail?>(Detail(requestedId, puuid));
            if (++slowRequests == 1)
            {
                firstToken = (CancellationToken)args[2]!;
                return stalled.Task.WaitAsync(firstToken);
            }
            return Task.FromResult<MatchDetail?>(Detail(id, puuid));
        }, puuid: puuid);
        using var form = Mount(card);
        var panel = Child(card, "panelMatches");
        var existingRow = Assert.Single(panel.Controls.OfType<RecentMatchRow>());
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (slowRequests < 2 && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
        System.Windows.Forms.Application.DoEvents();
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(2, slowRequests);
        Assert.Equal(2, panel.Controls.OfType<RecentMatchRow>().Count());
        Assert.Contains(existingRow, panel.Controls.OfType<RecentMatchRow>());
    });

    [Fact]
    public void DisposingCardCancelsRequestsAndDoesNotStartQueuedDetails() => MatchListScrollingTests.OnUiThread(() =>
    {
        long id = DateTime.UtcNow.Ticks;
        int requests = 0;
        var tokens = new List<CancellationToken>();
        var pending = new TaskCompletionSource<MatchDetail?>();
        var card = CreateCard((method, args) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(id, 4));
            requests++;
            var token = (CancellationToken)args[2]!;
            tokens.Add(token);
            return pending.Task.WaitAsync(token);
        });
        using var form = Mount(card);
        Assert.Equal(2, requests);
        card.Dispose();
        System.Windows.Forms.Application.DoEvents();
        Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
        Assert.Equal(2, requests);
    });

    private static MatchHistoryResponse Page(long firstId, int count) => new()
    {
        Games = new()
        {
            GameCount = count,
            Games = Enumerable.Range(0, count).Select(index => new MatchHistoryGame
            {
                GameId = firstId + index, GameCreation = DateTimeOffset.UtcNow.AddMinutes(-index).ToUnixTimeMilliseconds(),
                QueueId = 420, GameMode = "CLASSIC"
            }).ToList()
        }
    };

    private static MatchDetail Detail(long id, string puuid) => new()
    {
        GameId = id, gameDuration = 1200, queueId = "420",
        participantIdentities = [new() { participantId = 1, player = new() { Puuid = puuid } }],
        participants = [new() { participantId = 1, stats = new() { kills = 5, deaths = 2, assists = 8 } }]
    };

    private static LivePlayerForm CreateCard(Func<MethodInfo, object?[], object?> history,
        string? puuid = null, Task<byte[]?>? icon = null, Task<PlayerProfile?>? profile = null) => new(
        puuid ?? Guid.NewGuid().ToString(), "测试玩家", 0, "", false, false, true, 430, "CLASSIC",
        Service<IPlayerProfileService>((_, _) => profile ?? Task.FromResult<PlayerProfile?>(new("", "测试玩家", "", 1, 100, 0, 0))),
        Service<IProfileIconService>((_, _) => icon ?? Task.FromResult<byte[]?>(null)),
        Service<IMatchHistoryService>(history),
        Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static Control Child(Control parent, string name) => parent.Controls.Find(name, true).Single();

    private static Form Mount(Control card)
    {
        var form = new Form { ClientSize = new Size(640, 520), Opacity = 0, ShowInTaskbar = false };
        form.Controls.Add(card);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        return form;
    }
}
