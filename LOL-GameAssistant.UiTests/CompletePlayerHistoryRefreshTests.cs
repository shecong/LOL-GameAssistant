using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class CompletePlayerHistoryRefreshTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static List<MatchHistoryGame?> History(int count) => Enumerable.Range(0, count)
        .Select(index => (MatchHistoryGame?)new MatchHistoryGame { GameId = 10000 - index, QueueId = index % 2 == 0 ? 420 : 450 }).ToList();
    private static MatchHistoryResponse Page(List<MatchHistoryGame?> rows, int begin, int count)
    {
        int taken = Math.Min(count, Math.Max(0, rows.Count - begin));
        var games = rows.Skip(begin).Take(taken).Select((game, index) => game?.CopyAtHistoryIndex(begin + index)).OfType<MatchHistoryGame>().ToList();
        bool more = begin + taken < rows.Count;
        return new() { Games = new() { Games = games, GameCount = more ? begin + taken + 1 : begin + taken,
            MissingSummaryCount = taken - games.Count, GameIndexBegin = begin, GameIndexEnd = begin + taken - 1,
            HasMore = more, IsTotalKnown = !more, Source = "SGP" } };
    }
    private static CompletePlayerHistorySnapshot Snapshot(List<MatchHistoryGame?> rows) => new(Page(rows, 0, rows.Count), Now.AddHours(-1), Now.AddMinutes(-5));
    private static IMatchHistoryService Source(List<MatchHistoryGame?> rows, List<int> requests) => Service<IMatchHistoryService>((method, args) =>
    {
        Assert.Equal("GetPageAsync", method.Name);
        int begin = (int)args[1]!, count = (int)args[2]! - begin + 1;
        requests.Add(begin);
        return Task.FromResult<MatchHistoryResponse?>(Page(rows, begin, count));
    });

    [Fact]
    public async Task UnchangedHistoryOnlyChecksTheTailAndKeepsTheBaselineImmutable()
    {
        var rows = History(1000); var old = Snapshot(rows); var requests = new List<int>();
        var result = await CompletePlayerHistoryRefresh.ReadAsync(Source(rows, requests), "self", Page(rows, 0, 250), old, _ => { }, default, Now);
        Assert.Equal(new[] { 999 }, requests);
        Assert.Equal(1000, result.Data.Games!.Games.Count);
        Assert.Equal(old.FullScanAt, result.FullScanAt);
        Assert.Equal(0, old.Data.Games!.Games[0].HistoryIndex);
        Assert.Equal(999, result.Data.Games.Games[^1].HistoryIndex);
    }

    [Fact]
    public async Task NewMatchesAndUnreadableRowsUseRawOffsetsWithoutDoubleCounting()
    {
        var rows = History(1000); rows[1] = rows[101] = rows[501] = null;
        var old = Snapshot(rows);
        var current = new List<MatchHistoryGame?> { new() { GameId = 20001, QueueId = 2400 }, null, new() { GameId = 20002, QueueId = 2400 } };
        current.AddRange(rows);
        var requests = new List<int>();
        var result = await CompletePlayerHistoryRefresh.ReadAsync(Source(current, requests), "self", Page(current, 0, 250), old, _ => { }, default, Now);
        Assert.Equal(new[] { 1002 }, requests);
        Assert.Equal(1003, result.Data.Games!.GameCount);
        Assert.Equal(999, result.Data.Games.Games.Count);
        Assert.Equal(4, result.Data.Games.MissingSummaryCount);
        Assert.Equal(2, result.Data.Games.Games.Count(game => game.QueueId == 2400));
        Assert.Equal(0, old.Data.Games!.Games[0].HistoryIndex);
    }

    [Fact]
    public async Task RecoveredSummaryReplacesAMissingRowWithoutAddingARawRecord()
    {
        var rows = History(600); rows[5] = null;
        var old = Snapshot(rows);
        rows[5] = new() { GameId = 30000, QueueId = 2400 };
        var result = await CompletePlayerHistoryRefresh.ReadAsync(Source(rows, []), "self", Page(rows, 0, 250), old, _ => { }, default, Now);
        Assert.Equal(600, result.Data.Games!.GameCount);
        Assert.Equal(600, result.Data.Games.Games.Count);
        Assert.Equal(0, result.Data.Games.MissingSummaryCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredOrChangedHistoryIsFullyReconciled(bool expired)
    {
        var rows = History(1000); var old = Snapshot(rows);
        if (expired) old = old with { FullScanAt = Now.AddDays(-2) }; else rows.RemoveAt(900);
        var requests = new List<int>();
        var result = await CompletePlayerHistoryRefresh.ReadAsync(Source(rows, requests), "self", Page(rows, 0, 250), old, _ => { }, default, Now);
        Assert.Contains(expired ? 250 : 0, requests);
        Assert.Equal(rows.Count, result.Data.Games!.GameCount);
        Assert.Equal(Now, result.FullScanAt);
    }

    [Fact]
    public async Task OldHeadSnapshotCannotBeCombinedWithNewServerOffsets()
    {
        var oldRows = History(600); var old = Snapshot(oldRows);
        var current = new List<MatchHistoryGame?> { new() { GameId = 40000, QueueId = 2400 } };
        current.AddRange(oldRows);
        var requests = new List<int>();
        var result = await CompletePlayerHistoryRefresh.ReadAsync(Source(current, requests), "self", Page(oldRows, 0, 250), old, _ => { }, default, Now);
        Assert.Contains(0, requests);
        Assert.Equal(601, result.Data.Games!.GameCount);
        Assert.Equal(40000, result.Data.Games.Games[0].GameId);
    }

    [Fact]
    public async Task StaleNetworkFallbackCannotConfirmAnIncrementalTotal()
    {
        var rows = History(600); var old = Snapshot(rows);
        var source = Service<IMatchHistoryService>((_, args) =>
        {
            var page = Page(rows, (int)args[1]!, 100); page.IsCached = true;
            return Task.FromResult<MatchHistoryResponse?>(page);
        });
        await Assert.ThrowsAsync<IOException>(() => CompletePlayerHistoryRefresh.ReadAsync(source, "self", Page(rows, 0, 250), old, _ => { }, default, Now));
        Assert.Equal(600, old.Data.Games!.GameCount);
    }

    [Fact]
    public async Task CompressedHistorySurvivesRestartAndIsIsolatedByPlayerAndRegion()
    {
        string root = Path.Combine(Path.GetTempPath(), "sc-complete-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Source(History(1), []);
            CachedMatchHistoryService Cache(string region) => new(source, root, _ => Task.FromResult(region));
            await Cache("HN1").SaveCompleteHistoryAsync("self", Snapshot(History(1000)), default);
            var read = await Cache("HN1").ReadCompleteHistoryAsync("self", default);
            Assert.Equal(1000, read!.Data.Games!.Games.Count);
            Assert.Equal(999, read.Data.Games.Games[^1].HistoryIndex);
            Assert.Null(await Cache("HN10").ReadCompleteHistoryAsync("self", default));
            Assert.Null(await Cache("HN1").ReadCompleteHistoryAsync("other-player", default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InitialSgpScanPrefetchesTwoPagesButMergesInOrder()
    {
        var rows = History(300);
        var first = new TaskCompletionSource<MatchHistoryResponse?>();
        var second = new TaskCompletionSource<MatchHistoryResponse?>();
        var requests = new List<int>();
        var source = Service<IMatchHistoryService>((_, args) =>
        {
            int begin = (int)args[1]!; requests.Add(begin);
            return begin == 100 ? first.Task : begin == 200 ? second.Task : Task.FromResult<MatchHistoryResponse?>(Page(rows, begin, 100));
        });
        var reading = CompletePlayerHistoryReader.ReadAsync(source, "self", Page(rows, 0, 100), _ => { }, default);
        Assert.Equal(new[] { 100, 200 }, requests);
        second.SetResult(Page(rows, 200, 100));
        Assert.False(reading.IsCompleted);
        first.SetResult(Page(rows, 100, 100));
        var result = await reading;
        Assert.Equal(300, result.Games!.Games.Count);
        Assert.Equal(Enumerable.Range(0, 300), result.Games.Games.Select(game => game.HistoryIndex!.Value));
    }

    [Fact]
    public void HomeShowsCachedModeCountsWhileFreshHistoryIsStillPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), "sc-complete-home-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rows = History(600);
            var pending = new TaskCompletionSource<MatchHistoryResponse?>();
            var source = Service<IMatchHistoryService>((method, args) => method.Name == "GetDetailAsync"
                ? Task.FromResult<MatchDetail?>(null) : (int)args[1]! == 0 ? pending.Task : Task.FromResult<MatchHistoryResponse?>(Page(rows, (int)args[1]!, 100)));
            var matches = new CachedMatchHistoryService(source, root, _ => Task.FromResult("HN1"));
            Pump(matches.SaveCompleteHistoryAsync("self", Snapshot(rows), default));
            var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
            using var home = CreateHome(profiles: Service<IPlayerProfileService>((_, _) => Task.FromResult(Player("self"))),
                matches: matches, recordFactory: () => new RecordForm(matches, assets));
            var loading = home.LoadPlayerAsync(null);
            PumpUntil(() => Field<AntdUI.Label>(home, "_modeScope").Text?.StartsWith("缓存统计：600") == true);
            Assert.False(loading.IsCompleted);
            pending.SetResult(Page(rows, 0, 250));
            Pump(loading);
            Assert.Equal("接口全部可读记录：600 场", Field<AntdUI.Label>(home, "_modeScope").Text);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });
}
