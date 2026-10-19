using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class EncounterAndParallelLoadingTests
{
    [Fact]
    public void LiveGamePageCanBeDisposedTwice() => MatchListScrollingTests.OnUiThread(() =>
    {
        var page = new LiveGameForm();
        page.Dispose();
        page.Dispose();
        Assert.True(page.IsDisposed);
    });

    [Fact]
    public void RetryingFailedPlayerRestoresWaitingUntilTheNewRequestCompletes() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        int requests = 0;
        var source = Service<IMatchHistoryService>((_, _) => ++requests == 1 ? Task.FromResult<MatchHistoryResponse?>(null) : pending.Task);
        using var card = Card("other", source);
        Pump(card.EnsureDataLoadedAsync());
        card.UpdateRecentEncounter("self", Page(1).Games!.Games);
        Assert.Equal("玩家战绩暂不可用", Label(card).Text);
        var retry = card.Controls.Find("btnRetryHistory", true).Single();
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(retry, [EventArgs.Empty]);
        Assert.Equal(2, requests);
        card.UpdateRecentEncounter("self", Page(1).Games!.Games);
        Assert.Equal("战绩加载后检测", Label(card).Text);
        pending.SetResult(Page(1));
        PumpUntil(() => card.RecentEncounterHistory != null);
        card.UpdateRecentEncounter("self", Page(1).Games!.Games);
        Assert.Equal("近期遇到 1 次", Label(card).Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateOldIdentityResponseCannotOverwriteNewIdentityWaitingState(bool oldSucceeded) => MatchListScrollingTests.OnUiThread(() =>
    {
        var old = new TaskCompletionSource<MatchHistoryResponse?>();
        var current = new TaskCompletionSource<MatchHistoryResponse?>();
        var source = Service<IMatchHistoryService>((_, args) => (string)args[0]! == "self" ? old.Task : current.Task);
        using var view = new LiveGameForm(null!, null!, null!, source);
        var card = Card("other", source);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(card);
        card.SetRecentEncounterHistory(Page(1));
        SetSelf(view);
        UpdateEncounters(view);
        var oldObserver = PlayerHistoryRepository.For(source).GetBatchAsync("self");
        SetSelf(view, "next-self");
        UpdateEncounters(view);
        old.SetResult(oldSucceeded ? Page(2) : null);
        Pump(oldObserver);
        UpdateEncounters(view);
        Assert.Equal("等待读取本人战绩", Label(card).Text);
        current.SetResult(Page(1));
        PumpUntil(() => Label(card).Text == "近期遇到 1 次");
    });

    [Fact]
    public void PendingPlayerHistoryTakesPriorityOverAnotherSourcesFailure() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var card = Card("other", Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(null)));
        card.UpdateRecentEncounter("self", null, ownUnavailable: true);
        Assert.Equal("战绩加载后检测", Label(card).Text);
        card.SetRecentEncounterHistory(Page(1));
        card.UpdateRecentEncounter("self", null, ownUnavailable: true);
        Assert.Equal("本人战绩暂不可用", Label(card).Text);
    });

    [Fact]
    public void SelfRosterCardIsAwaitedWithoutStartingAnIndependentEncounterRead() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        int requests = 0;
        var source = Service<IMatchHistoryService>((_, _) => { requests++; return pending.Task; });
        using var view = new LiveGameForm(null!, null!, null!, source);
        var own = Card("self", source);
        var other = Card("other", source);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(own);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(other);
        other.SetRecentEncounterHistory(Page(1));
        own.OverviewChanged += (_, _) => UpdateEncounters(view);
        SetSelf(view);
        UpdateEncounters(view);
        Assert.Equal(0, requests);
        Assert.Equal("等待读取本人战绩", Label(other).Text);
        var loading = own.EnsureDataLoadedAsync();
        Assert.Equal(1, requests);
        UpdateEncounters(view);
        Assert.Equal("等待读取本人战绩", Label(other).Text);
        pending.SetResult(Page(1));
        Pump(loading);
        Assert.Equal("近期遇到 1 次", Label(other).Text);
        Assert.Equal(1, requests);
    });

    [Fact]
    public void SuccessfulSharedRetryReplacesEarlierEncounterFailure() => MatchListScrollingTests.OnUiThread(() =>
    {
        int requests = 0;
        var source = Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(++requests == 1 ? null : Page(1)));
        using var view = new LiveGameForm(null!, null!, null!, source);
        var other = Card("other", source);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(other);
        other.SetRecentEncounterHistory(Page(1));
        SetSelf(view);
        UpdateEncounters(view);
        PumpUntil(() => Label(other).Text == "本人战绩暂不可用");
        Pump(PlayerHistoryRepository.For(source).GetBatchAsync("self"));
        UpdateEncounters(view);
        Assert.Equal("近期遇到 1 次", Label(other).Text);
        Assert.Equal(2, requests);
    });

    [Fact]
    public void EncounterComparisonUsesFiftyNonCustomGamesWithoutAgeCutoff()
    {
        var history = Page(Enumerable.Range(1, 55).Select(id => (long)id).ToArray());
        history.Games!.Games[0].QueueId = 0;
        history.Games.Games[1].GameType = "CUSTOM_GAME";
        var selected = LivePlayerForm.SelectEncounterHistory(history);
        Assert.Equal(50, selected.Count);
        Assert.Equal(3, selected.First().GameId);
        Assert.Equal(52, selected.Last().GameId);
    }

    [Fact]
    public void SelfHistoryIsReadWhenSelfIsNotInTheRosterAndDetectionFinishes() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        int requests = 0;
        var source = Service<IMatchHistoryService>((method, args) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            Assert.Equal("self", args[0]);
            requests++; return pending.Task;
        });
        using var view = new LiveGameForm(null!, null!, null!, source);
        var card = Card("other", source);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(card);
        card.SetRecentEncounterHistory(Page(1));
        SetSelf(view);
        UpdateEncounters(view);
        Assert.Equal("等待读取本人战绩", Label(card).Text);
        Assert.Equal(1, requests);
        pending.SetResult(Page(1));
        PumpUntil(() => Label(card).Text == "近期遇到 1 次");
        UpdateEncounters(view);
        Assert.Equal(1, requests);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOrFailedSelfHistoryHasATerminalState(bool failed) => MatchListScrollingTests.OnUiThread(() =>
    {
        int requests = 0;
        var source = Service<IMatchHistoryService>((_, _) =>
        {
            requests++;
            return Task.FromResult<MatchHistoryResponse?>(failed ? null : Page());
        });
        using var view = new LiveGameForm(null!, null!, null!, source);
        var card = Card("other", source);
        Field<FlowLayoutPanel>(view, "panelTeam1").Controls.Add(card);
        card.SetRecentEncounterHistory(Page(1));
        SetSelf(view);
        UpdateEncounters(view);
        PumpUntil(() => Label(card).Text == (failed ? "本人战绩暂不可用" : "近期未遇到"));
        UpdateEncounters(view);
        Assert.Equal(1, requests);
    });

    [Fact]
    public void AllPlayersStartBeforeAnyResponseAndSlowFirstPlayerDoesNotBlockSecond() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = Enumerable.Range(0, 10).ToDictionary(id => $"player-{id}",
            _ => new TaskCompletionSource<MatchHistoryResponse?>());
        var requested = new HashSet<string>();
        var source = Service<IMatchHistoryService>((method, args) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            string puuid = (string)args[0]!;
            requested.Add(puuid);
            return pending[puuid].Task;
        });
        using var view = new LiveGameForm(null!, null!, null!, source);
        var cards = pending.Keys.Select(puuid => Card(puuid, source)).ToArray();
        var blue = Field<FlowLayoutPanel>(view, "panelTeam1");
        var red = Field<FlowLayoutPanel>(view, "panelTeam2");
        foreach (var card in cards.Take(5)) blue.Controls.Add(card);
        foreach (var card in cards.Skip(5)) red.Controls.Add(card);
        var loading = view.LoadPlayerCardsConcurrentlyAsync();
        Assert.Equal(10, requested.Count);
        Assert.False(loading.IsCompleted);
        pending["player-1"].SetResult(Page(2));
        PumpUntil(() => cards[1].RecentEncounterHistory != null);
        Assert.Null(cards[0].RecentEncounterHistory);
        Assert.Single(cards[1].Controls.Find("panelMatches", true).Single().Controls.OfType<RecentMatchRow>());
        foreach (var response in pending.Values) response.TrySetResult(Page(1));
        Pump(loading);
    });

    [Fact]
    public void FailedPlayerHistoryDoesNotStayInDetectingState() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var card = Card("other", Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(null)));
        Pump(card.EnsureDataLoadedAsync());
        card.UpdateRecentEncounter("self", Page(1).Games!.Games);
        Assert.Equal("玩家战绩暂不可用", Label(card).Text);
    });

    private static MatchHistoryResponse Page(params long[] ids) => new() { Games = new() { Games = ids.Select(id => new MatchHistoryGame
    {
        GameId = id, QueueId = 420, GameMode = "CLASSIC", GameDuration = 1200,
        GameCreation = DateTimeOffset.UtcNow.AddDays(-100).ToUnixTimeMilliseconds(),
        Participants = [new() { stats = new() { kills = 3, deaths = 2, assists = 4, Win = true } }]
    }).ToList() } };
    private static LivePlayerForm Card(string puuid, IMatchHistoryService source) => new(puuid, puuid, 0, "", false, false, true, 420, "CLASSIC",
        Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(null)),
        Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)), source,
        Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));
    private static Control Label(LivePlayerForm card) => card.Controls.Find("lblRecentEncounter", true).Single();
    private static void SetSelf(LiveGameForm view, string puuid = "self") => typeof(LiveGameForm).GetField("_encounterSelfPuuid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, puuid);
    private static void UpdateEncounters(LiveGameForm view) => typeof(LiveGameForm).GetMethod("UpdateRecentEncounters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
}
