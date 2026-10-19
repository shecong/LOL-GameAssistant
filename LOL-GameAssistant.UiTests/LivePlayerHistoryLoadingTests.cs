using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LivePlayerHistoryLoadingTests
{
    [Fact]
    public void CustomClassicLobbyShowsKdaFromRankedSummaries() => MatchListScrollingTests.OnUiThread(() =>
    {
        var batch = Page(DateTime.UtcNow.Ticks, 10);
        foreach (var game in batch.Games!.Games)
        {
            game.GameDuration = 1200;
            game.Participants = [new() { stats = new() { kills = 5, deaths = 2, assists = 8, Win = true } }];
        }
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(batch) : Task.FromResult<MatchDetail?>(null), queueId: 3110);
        using var form = Mount(card);
        Assert.Contains("KDA 6.50", Child(card, "lblRecentKda").Text);
        Assert.Contains("10场", Child(card, "lblRecentKda").Text);
        Assert.Equal("上等马", Child(card, "lblSummary").Text);
    });

    [Fact]
    public void VisibleCardsRequestOneBatchOfFiveHundredSummaries() => MatchListScrollingTests.OnUiThread(() =>
    {
        var card = CreateCard((method, args) => {
            Assert.Equal("GetPageAsync", method.Name);
            Assert.Equal(0, (int)args[1]!);
            Assert.Equal(499, (int)args[2]!);
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() });
        });
        using var form = Mount(card);
        Assert.Contains(Child(card, "panelMatches").Controls.Cast<Control>(), control => control.Text == "暂无战绩");
    });

    [Fact]
    public void ValidSingleSummaryIsScoredEvenWhenEveryDetailRequestFails() => MatchListScrollingTests.OnUiThread(() =>
    {
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [new() {
                GameId = DateTime.UtcNow.Ticks, GameCreation = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameDuration = 1200, QueueId = 430, GameMode = "CLASSIC",
                Participants = [new() { stats = new() { kills = 5, deaths = 2, assists = 8, Win = true } }]
            }] } }) : Task.FromResult<MatchDetail?>(null));
        using var form = Mount(card);
        Assert.Equal("数据不足", Child(card, "lblSummary").Text);
        Assert.Contains("KDA 6.50", Child(card, "lblRecentKda").Text);
        Assert.Contains("1场", Child(card, "lblRecentKda").Text);
    });

    [Fact]
    public void HeaderShowsKdaAndRecentEncounterWithoutCountingDuplicateMatches() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        var card = CreateCard((_, _) => pending.Task, puuid: "encounter-other");
        card.Width = 320;
        using var form = Mount(card);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MatchHistoryGame Game(long id, long created) => new() { GameId = id, QueueId = 420, GameCreation = created, GameDuration = 1200 };
        card.SetRecentEncounterHistory(new() { Games = new() { Games = [Game(1, now), Game(1, now),
            Game(2, DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds()), Game(3, now)] } });
        card.UpdateRecentEncounter("self", null);
        Assert.Equal("等待读取本人战绩", Child(card, "lblRecentEncounter").Text);
        card.UpdateRecentEncounter("self", [Game(1, now), Game(2, now)]);
        Assert.Equal("近期遇到 2 次", Child(card, "lblRecentEncounter").Text);
        card.UpdateRecentEncounter("self", [Game(99, now)]);
        Assert.Equal("近期未遇到", Child(card, "lblRecentEncounter").Text);
        var result = RecentModePerformanceEvaluator.Evaluate("测试", [new(MatchPerformanceTier.Upper, 90, "", 10, 4, 8)], [true]);
        card.ShowRecentAssessment(result);
        var kda = Child(card, "lblRecentKda");
        Assert.Contains("KDA 4.50", kda.Text);
        Assert.Contains("1场", kda.Text);
        Assert.False(kda.Bounds.IntersectsWith(Child(card, "lblRecentEncounter").Bounds));
        Assert.True(kda.Bottom <= Child(card, "panelMatches").Top);
        card.ShowRecentAssessment(result with { UsesAllModes = true });
        Assert.Contains("各模式", kda.Text);
        card.UpdateRecentEncounter("encounter-other", []);
        Assert.False(Child(card, "lblRecentEncounter").Visible);
    });

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
    public void IncompleteSummaryStillShowsARowWithoutFetchingDetails() => MatchListScrollingTests.OnUiThread(() =>
    {
        long gameId = DateTime.UtcNow.Ticks;
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [new() { GameId = gameId }] } })
            : Task.FromResult<MatchDetail?>(null));
        using var form = Mount(card);
        Assert.Empty(Child(card, "panelMatches").Controls.OfType<ShimmerPanel>());
        Assert.Single(Child(card, "panelMatches").Controls.OfType<RecentMatchRow>());
        Assert.Equal("数据不足", Child(card, "lblSummary").Text);
    });

    [Fact]
    public void FiveHundredSummariesAreScoredAndTenNewestRowsDoNotFetchDetails() => MatchListScrollingTests.OnUiThread(() =>
    {
        long firstId = DateTime.UtcNow.Ticks;
        string puuid = Guid.NewGuid().ToString();
        var page = Page(firstId, 500);
        foreach (var game in page.Games!.Games)
        {
            game.QueueId = 430;
            game.GameDuration = 1200;
            game.Participants = [new() { participantId = 1,
                stats = new() { kills = 5, deaths = 2, assists = 8, Win = true } }];
        }
        int batchRequests = 0;
        var detailRequests = new List<long>();
        var card = CreateCard((method, args) =>
        {
            if (method.Name == "GetPageAsync")
            {
                Assert.Equal(0, args[1]); Assert.Equal(499, args[2]);
                batchRequests++;
                return Task.FromResult<MatchHistoryResponse?>(page);
            }
            long id = (long)args[0]!;
            detailRequests.Add(id);
            return Task.FromResult<MatchDetail?>(Detail(id, puuid));
        }, puuid: puuid);
        using var form = Mount(card);
        Assert.Equal(1, batchRequests);
        Assert.Empty(detailRequests);
        Assert.Equal(10, Child(card, "panelMatches").Controls.OfType<RecentMatchRow>().Count());
        Assert.Contains("50场", Child(card, "lblRecentKda").Text);
        Assert.Contains("KDA 6.50", Child(card, "lblRecentKda").Text);
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
    public void SummaryRowsStayNewestFirstWithoutAnyDetailRequests() => MatchListScrollingTests.OnUiThread(() =>
    {
        long firstId = DateTime.UtcNow.Ticks;
        int details = 0;
        var history = Page(firstId, 10);
        history.Games!.Games.Reverse();
        var card = CreateCard((method, _) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(history);
            details++; return Task.FromResult<MatchDetail?>(null);
        });
        using var form = Mount(card);
        var rows = Child(card, "panelMatches").Controls.OfType<RecentMatchRow>().ToArray();
        Assert.Equal(10, rows.Length);
        Assert.Equal(0, details);
        Assert.Equal(firstId, SecondPhaseRegressionTests.Field<long>(rows[0], "_summaryGameId"));
        Assert.Equal(firstId + 9, SecondPhaseRegressionTests.Field<long>(rows[9], "_summaryGameId"));
        Assert.Equal("未知", Child(rows[0], "lblResult").Text);
        Assert.Equal("—/—/—", Child(rows[0], "lblKda").Text);
        Assert.True(rows[0].Top < rows[1].Top);
    });

    [Fact]
    public void OpeningOneSummaryRowFetchesOnlyItsDetailAndReusesIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        long firstId = DateTime.UtcNow.Ticks;
        string puuid = Guid.NewGuid().ToString();
        var requested = new List<long>();
        var card = CreateCard((method, args) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(firstId, 10));
            long id = (long)args[0]!;
            requested.Add(id);
            return Task.FromResult<MatchDetail?>(Detail(id, puuid));
        }, puuid: puuid);
        using var form = Mount(card);
        Assert.Empty(requested);
        var rows = Child(card, "panelMatches").Controls.OfType<RecentMatchRow>().ToArray();
        var task = rows[3].LoadDetailAsync();
        SecondPhaseRegressionTests.Pump(task);
        Assert.Equal(firstId + 3, task.Result!.GameId);
        Assert.Equal(new[] { firstId + 3 }, requested);
        SecondPhaseRegressionTests.Pump(rows[3].LoadDetailAsync());
        Assert.Single(requested);
        Assert.Null(SecondPhaseRegressionTests.Field<MatchDetail?>(rows[0], "_detail"));
    });

    [Fact]
    public void FailedClickedDetailCanBeRetriedWithoutRemovingSummaryRows() => MatchListScrollingTests.OnUiThread(() =>
    {
        long id = DateTime.UtcNow.Ticks;
        string puuid = Guid.NewGuid().ToString();
        int requests = 0;
        var card = CreateCard((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(Page(id, 2))
            : Task.FromResult<MatchDetail?>(++requests == 1 ? null : Detail(id, puuid)), puuid: puuid);
        using var form = Mount(card);
        var panel = Child(card, "panelMatches");
        var row = panel.Controls.OfType<RecentMatchRow>().First();
        var failed = row.LoadDetailAsync();
        SecondPhaseRegressionTests.Pump(failed);
        Assert.Null(failed.Result);
        Assert.Equal(2, panel.Controls.OfType<RecentMatchRow>().Count());
        var retried = row.LoadDetailAsync();
        SecondPhaseRegressionTests.Pump(retried);
        Assert.Equal(id, retried.Result!.GameId);
        Assert.Equal(2, requests);
    });

    [Fact]
    public void PendingClickedDetailDoesNotBlockOtherRowsOrChangeTheAssessment() => MatchListScrollingTests.OnUiThread(() =>
    {
        long firstId = DateTime.UtcNow.Ticks;
        string puuid = Guid.NewGuid().ToString();
        var pending = new TaskCompletionSource<MatchDetail?>();
        int requests = 0;
        var card = CreateCard((method, _) =>
        {
            if (method.Name == "GetPageAsync") return Task.FromResult<MatchHistoryResponse?>(Page(firstId, 10));
            requests++; return pending.Task;
        }, puuid: puuid);
        using var form = Mount(card);
        Assert.False(SecondPhaseRegressionTests.Field<bool>(card, "_loading"));
        Assert.Equal(0, requests);
        string assessment = Child(card, "lblRecentKda").Text;
        var rows = Child(card, "panelMatches").Controls.OfType<RecentMatchRow>().ToArray();
        var first = rows[0].LoadDetailAsync();
        var second = rows[0].LoadDetailAsync();
        Assert.Equal(1, requests);
        Assert.False(first.IsCompleted);
        Assert.Equal(10, rows.Length);
        Assert.Equal(assessment, Child(card, "lblRecentKda").Text);
        pending.SetResult(Detail(firstId, puuid));
        SecondPhaseRegressionTests.Pump(Task.WhenAll(first, second));
        Assert.Equal(1, requests);
    });
    [Fact]
    public void ClosingFirstCardDoesNotCancelAnotherCardsSharedHistoryBatch() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        var response = new TaskCompletionSource<MatchHistoryResponse?>();
        int requests = 0;
        CancellationToken sourceToken = default;
        object? Handler(MethodInfo method, object?[] args)
        {
            Assert.Equal("GetPageAsync", method.Name);
            requests++;
            sourceToken = (CancellationToken)args[3]!;
            return response.Task.WaitAsync(sourceToken);
        }
        var source = Service<IMatchHistoryService>(Handler);
        var first = CreateCard(Handler, puuid: puuid, source: source);
        var second = CreateCard(Handler, puuid: puuid, source: source);
        using var firstForm = Mount(first);
        using var secondForm = Mount(second);
        Assert.Equal(1, requests);
        first.Dispose();
        Assert.False(sourceToken.IsCancellationRequested);
        response.SetResult(new() { Games = new() });
        SecondPhaseRegressionTests.PumpUntil(() => Child(second, "panelMatches").Controls.Cast<Control>()
            .Any(control => control.Text == "暂无战绩"));
        Assert.Equal(1, requests);
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
        string? puuid = null, Task<byte[]?>? icon = null, Task<PlayerProfile?>? profile = null,
        IMatchHistoryService? source = null, int queueId = 430) => new(
        puuid ?? Guid.NewGuid().ToString(), "测试玩家", 0, "", false, false, true, queueId, "CLASSIC",
        Service<IPlayerProfileService>((_, _) => profile ?? Task.FromResult<PlayerProfile?>(new("", "测试玩家", "", 1, 100, 0, 0))),
        Service<IProfileIconService>((_, _) => icon ?? Task.FromResult<byte[]?>(null)),
        source ?? Service<IMatchHistoryService>(history),
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
