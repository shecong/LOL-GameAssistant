using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class HomeHistoryLoadingTests
{
    [Theory]
    [InlineData(620)]
    [InlineData(800)]
    [InlineData(1060)]
    public void LoadedDetailsRestoreBothTeamsAfterSummary(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>();
        var matches = Service<IMatchHistoryService>((_, _) => pending.Task);
        using var record = new RecordForm(matches,
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null))) { Width = width };
        var history = History(20);
        var loading = record.setInfo(history.Games!.Games[0], "self", history: history);
        Assert.False(Field<Control>(record, "flowAlly").Visible);
        Assert.False(Field<Control>(record, "flowEnemy").Visible);

        var detail = Detail(1);
        detail.participants[0].teamId = 200;
        detail.participants.Add(new() { participantId = 2, teamId = 100, championId = 1 });
        detail.participantIdentities.Add(new() { participantId = 2, player = new() { Puuid = "enemy", GameName = "对手" } });
        pending.SetResult(detail);
        Pump(loading);

        foreach (string name in new[] { "lblAlly", "lblEnemy", "flowAlly", "flowEnemy" })
        {
            Control control = Field<Control>(record, name);
            Assert.True(control.Visible, name);
            Assert.True(record.ClientRectangle.Contains(control.Bounds), name);
        }
        Assert.Single(Field<Control>(record, "flowAlly").Controls.Cast<Control>());
        Assert.Equal("对手", Assert.Single(Field<Control>(record, "flowEnemy").Controls.Cast<Control>()).AccessibleName);

        pending = new TaskCompletionSource<MatchDetail?>();
        var reloading = record.setInfo(history.Games.Games[1], "self", history: history);
        pending.SetResult(null);
        Pump(reloading);
        Assert.False(Field<Control>(record, "lblAlly").Visible);
        Assert.False(Field<Control>(record, "lblEnemy").Visible);
        Assert.False(Field<Control>(record, "flowAlly").Visible);
        Assert.False(Field<Control>(record, "flowEnemy").Visible);
    });

    [Fact]
    public void PendingDetailsDoNotBlockSummaryRatingOrPaging() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>();
        var history = History(20);
        var matches = Service<IMatchHistoryService>((method, _) => method.Name == "GetPageAsync"
            ? Task.FromResult<MatchHistoryResponse?>(history) : pending.Task);
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        using var home = CreateHome(profiles: Service<IPlayerProfileService>((_, _) => Task.FromResult(Player("self"))),
            matches: matches, recordFactory: () => new RecordForm(matches, assets));
        var loading = home.LoadPlayerAsync(null);
        PumpUntil(() => Field<AntdUI.Pagination>(home, "game_pagin").Total == 20);
        Assert.False(loading.IsCompleted);
        Assert.Equal(20, Field<AntdUI.Pagination>(home, "game_pagin").Total);
        var card = Field<Control>(home, "stackPanel1").Controls.OfType<RecordForm>().First();
        Assert.Equal(10, Field<RecentModePerformanceAssessment>(card, "_performanceAssessment").SampleSize);
        Assert.Equal("胜利", card.Controls.Find("game_win", true).Single().Text);
        pending.SetResult(null);
        Pump(loading);
    });

    [Fact]
    public void InitialLoadAndLocalPagingReuseOneBatchAndOnlyLoadVisibleDetails() => MatchListScrollingTests.OnUiThread(() =>
    {
        var history = History(250);
        int batchRequests = 0;
        var detailIds = new List<long>();
        var matches = Service<IMatchHistoryService>((method, args) =>
        {
            if (method.Name == "GetPageAsync")
            {
                Assert.Equal("self", args[0]);
                Assert.Equal(0, args[1]);
                Assert.Equal(249, args[2]);
                batchRequests++;
                return Task.FromResult<MatchHistoryResponse?>(history);
            }
            Assert.Equal("GetDetailAsync", method.Name);
            long id = (long)args[0]!;
            detailIds.Add(id);
            return Task.FromResult<MatchDetail?>(Detail(id));
        });
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        using var home = CreateHome(
            profiles: Service<IPlayerProfileService>((_, _) => Task.FromResult(Player("self"))),
            matches: matches, recordFactory: () => new RecordForm(matches, assets));
        Pump(home.LoadPlayerAsync(null));

        Assert.Equal(1, batchRequests);
        Assert.Equal(Enumerable.Range(1, 10).Select(id => (long)id), detailIds);
        Assert.Equal(250, Field<AntdUI.Pagination>(home, "game_pagin").Total);
        AssertAssessments(Field<Control>(home, "stackPanel1"));

        var pageTask = (Task)typeof(HomeForm).GetMethod("GetGameInfo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(home, [Player("self"), 2])!;
        Pump(pageTask);
        Assert.Equal(1, batchRequests);
        Assert.Equal(Enumerable.Range(1, 20).Select(id => (long)id), detailIds);
        AssertAssessments(Field<Control>(home, "stackPanel1"));
    });

    [Fact]
    public void UnavailableVisibleDetailStillUsesTheProvidedSummaryForRating() => MatchListScrollingTests.OnUiThread(() =>
    {
        int detailRequests = 0;
        var matches = Service<IMatchHistoryService>((method, _) =>
        {
            Assert.Equal("GetDetailAsync", method.Name);
            detailRequests++;
            return Task.FromResult<MatchDetail?>(null);
        });
        using var record = new RecordForm(matches,
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));
        var history = History(20);
        Pump(record.setInfo(history.Games!.Games[0], "self", history: history));
        var assessment = Field<RecentModePerformanceAssessment>(record, "_performanceAssessment");
        Assert.Equal(10, assessment.SampleSize);
        Assert.Equal(9d, assessment.Kda);
        Assert.Equal(RecentPerformanceLabel.Upper, assessment.Label);
        Assert.Equal(1, detailRequests);
    });

    private static void AssertAssessments(Control host)
    {
        var cards = host.Controls.OfType<RecordForm>().ToArray();
        Assert.Equal(10, cards.Length);
        foreach (var card in cards)
        {
            var assessment = Field<RecentModePerformanceAssessment>(card, "_performanceAssessment");
            Assert.Equal(50, assessment.SampleSize);
            Assert.False(assessment.UsesAllModes);
            // Queue 450 uses 10/2/8 => 9; queue 420 uses 1/10/1 => .2.
            // The visible detail's 99/1/0 must not replace the batch's statistics.
            Assert.True(assessment.Kda == 9d || assessment.Kda == .2d);
            Assert.Equal(assessment.Kda == 9d ? RecentPerformanceLabel.Upper : RecentPerformanceLabel.Human, assessment.Label);
        }
    }

    private static MatchHistoryResponse History(int count) => new()
    {
        Games = new() { GameCount = count, Games = Enumerable.Range(1, count).Select(id => new MatchHistoryGame
        {
            GameId = id, GameCreation = DateTimeOffset.UtcNow.AddMinutes(-id).ToUnixTimeMilliseconds(),
            GameDuration = 1200, QueueId = id % 2 == 1 ? 450 : 420, GameMode = id % 2 == 1 ? "ARAM" : "CLASSIC",
            Participants = [new() { participantId = 1, championId = 22, stats = id % 2 == 1
                ? new() { kills = 10, deaths = 2, assists = 8, Win = true }
                : new() { kills = 1, deaths = 10, assists = 1, Win = false } }]
        }).ToList() }
    };

    private static MatchDetail Detail(long id) => new()
    {
        GameId = id, queueId = id % 2 == 1 ? "450" : "420", gameMode = id % 2 == 1 ? "ARAM" : "CLASSIC",
        gameDuration = 1200, participantIdentities = [new() { participantId = 1, player = new() { Puuid = "self" } }],
        participants = [new() { participantId = 1, championId = 22, stats = new() { kills = 99, deaths = 1, Win = true } }]
    };
}
