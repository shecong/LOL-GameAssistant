using System.Reflection;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class BattleQueryPagingTests
{
    [Fact]
    public void UnknownSgpTotalOnlyExposesNextPageAndDoesNotClaimCompleteCount() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var query = CreateQuery((method, _) => method.Name switch
        {
            "GetDetailAsync" => Task.FromResult<MatchDetail?>(null),
            _ => null
        });
        var page = new MatchHistoryResponse { Games = new()
        {
            GameCount = 11, IsTotalKnown = false, HasMore = true, Source = "SGP",
            Games = Enumerable.Range(1, 10).Select(id => new MatchHistoryGame { GameId = id }).ToList()
        } };
        var task = (Task)typeof(BattleQueryForm).GetMethod("RenderMatchPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(query, [page])!;
        task.GetAwaiter().GetResult();
        Assert.False(Field<bool>(query, "_historyTotalKnown"));
        Assert.Equal(11, Field<int>(query, "_totalGameCount"));
        Assert.Contains("总数未知", Field<AntdUI.Label>(query, "_historyTitle").Text);
        Assert.Contains("总场数未知", Field<AntdUI.Label>(query, "lblStatus").Text);
        Assert.DoesNotContain("共 11 场", Field<AntdUI.Label>(query, "lblStatus").Text);
    });
    [Fact]
    public void ModeFilterRepaginatesAllAvailableHistoryAndCachesItAcrossModes() => MatchListScrollingTests.OnUiThread(() =>
    {
        int historyReads = 0;
        var details = new List<long>();
        var games = Enumerable.Range(1, 40).Select(id => new MatchHistoryGame
            { GameId = id, GameCreation = id, QueueId = id % 2 == 0 ? 450 : 420 }).ToList();
        using var query = CreateQuery((method, args) => method.Name switch
        {
            "GetAllAsync" => ReadAll(),
            "GetPageAsync" => Task.FromResult<MatchHistoryResponse?>(new() { Games = new()
                { GameCount = 40, Games = games.OrderByDescending(game => game.GameCreation)
                    .Skip((int)args[1]!).Take((int)args[2]! - (int)args[1]! + 1).ToList() } }),
            "GetDetailAsync" => Detail((long)args[0]!),
            _ => throw new InvalidOperationException(method.Name)
        });
        Task<MatchHistoryResponse?> ReadAll()
        {
            historyReads++;
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { GameCount = 40, Games = games } });
        }
        Task<MatchDetail?> Detail(long id) { details.Add(id); return Task.FromResult<MatchDetail?>(null); }

        query.ApplyModeFilterAsync("深渊大乱斗").GetAwaiter().GetResult();
        Assert.Equal(20, Field<int>(query, "_totalGameCount"));
        Assert.Empty(details);
        Assert.Equal(Enumerable.Range(11, 10).Reverse().Select(id => (long)id * 2), Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>().Select(r => r.Game.GameId));
        details.Clear();
        SetField(query, "_currentPage", 2);
        Render(query).GetAwaiter().GetResult();
        Assert.Empty(details);
        Assert.Equal(Enumerable.Range(1, 10).Reverse().Select(id => (long)id * 2), Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>().Select(r => r.Game.GameId));
        details.Clear();
        query.ApplyModeFilterAsync("峡谷单双排").GetAwaiter().GetResult();
        Assert.Equal(1, Field<int>(query, "_currentPage"));
        Assert.Equal(20, Field<int>(query, "_totalGameCount"));
        Assert.Empty(details);
        Assert.All(Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>(), row => Assert.Equal(1, row.Game.GameId % 2));
        Assert.Equal(1, historyReads);
        query.ApplyModeFilterAsync("斗魂竞技场").GetAwaiter().GetResult();
        Assert.Equal(0, Field<int>(query, "_totalGameCount"));
        Assert.Empty(Field<Control>(query, "stackMatches").Controls.Cast<Control>());
        query.ApplyModeFilterAsync("全部").GetAwaiter().GetResult();
        Assert.Equal(40, Field<int>(query, "_totalGameCount"));
        Assert.Equal(10, Field<Control>(query, "stackMatches").Controls.Count);
    });

    [Fact]
    public void SlowOldFilterCannotReplaceTheLatestMode() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        using var query = CreateQuery((method, _) => method.Name switch
        {
            "GetAllAsync" => pending.Task,
            "GetPageAsync" => Task.FromResult<MatchHistoryResponse?>(new() { Games = new()
                { GameCount = 1, Games = [new MatchHistoryGame { GameId = 99, QueueId = 420 }] } }),
            "GetDetailAsync" => Task.FromResult<MatchDetail?>(null),
            _ => null
        });
        Task first = query.ApplyModeFilterAsync("深渊大乱斗");
        Assert.False(first.IsCompleted);
        query.ApplyModeFilterAsync("全部").GetAwaiter().GetResult();
        pending.SetResult(new() { Games = new() { GameCount = 20, Games =
            [new MatchHistoryGame { GameId = 1, QueueId = 450 }] } });
        System.Windows.Forms.Application.DoEvents();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal("全部", Field<string>(query, "_selectedMode"));
        Assert.Equal(1, Field<int>(query, "_totalGameCount"));
    });

    [Fact]
    public void StatisticsUseTheSelectedModeAndEmptyModesKeepTheFilterAvailable() => MatchListScrollingTests.OnUiThread(() =>
    {
        bool statistics = false;
        var games = new List<MatchHistoryGame>
        {
            new() { GameId = 1, QueueId = 450, GameCreation = 1 },
            new() { GameId = 2, QueueId = 420, GameCreation = 2 },
            new() { GameId = 3, QueueId = 450, GameCreation = 3 }
        };
        using var query = CreateQuery((method, _) => method.Name switch
        {
            "GetAllAsync" => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { GameCount = 3, Games = games } }),
            "GetDetailAsync" => Task.FromResult<MatchDetail?>(statistics ? new()
            {
                participantIdentities = [new() { participantId = 1, player = new() { Puuid = "me" } }],
                participants = [new() { participantId = 1, stats = new() { kills = 4, assists = 2, deaths = 2, Win = true } }]
            } : null),
            _ => throw new InvalidOperationException(method.Name)
        });
        query.ApplyModeFilterAsync("深渊大乱斗").GetAwaiter().GetResult();
        statistics = true;
        LoadStats(query).GetAwaiter().GetResult();
        Assert.Contains("深渊大乱斗  最近 2 场", Field<AntdUI.Label>(query, "lblStatus").Text);
        query.ApplyModeFilterAsync("斗魂竞技场").GetAwaiter().GetResult();
        LoadStats(query).GetAwaiter().GetResult();
        Assert.Contains("暂无有效统计数据", Field<AntdUI.Label>(query, "lblStatus").Text);
        Assert.Equal("斗魂竞技场", Descendants(Field<Control>(query, "panelStats"))
            .OfType<AntdUI.Select>().Single().SelectedValue);
    });

    private static IEnumerable<Control> Descendants(Control parent) =>
        new[] { parent }.Concat(parent.Controls.Cast<Control>().SelectMany(Descendants));
    private static Task LoadStats(BattleQueryForm query) =>
        (Task)typeof(BattleQueryForm).GetMethod("LoadAndShowStatsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(query, null)!;

    private static BattleQueryForm CreateQuery(Func<MethodInfo, object?[], object?> history)
    {
        var query = new BattleQueryForm(Proxy<IPlayerProfileService>(), Proxy<IProfileIconService>(),
            Proxy<IMatchHistoryService>(history), Proxy<IRankedStatsService>(),
            Proxy<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer>()), Proxy<ITextExportService>());
        SetField(query, "_currentPlayer", new PlayerProfile("me", "preview", "CN1", 1, 0, 0, 0));
        SetField(query, "_pageSize", 10);
        return query;
    }

    private static T Field<T>(BattleQueryForm query, string name) =>
        (T)typeof(BattleQueryForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(query)!;
    private static void SetField(BattleQueryForm query, string name, object value) =>
        typeof(BattleQueryForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(query, value);
    private static Task Render(BattleQueryForm query) =>
        (Task)typeof(BattleQueryForm).GetMethod("RenderMatchPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(query, [null])!;

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    public void OversizedHistoryResponseNeverCreatesMoreCardsThanTheSelectedPageSize(int pageSize) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            int detailRequests = 0;
            using var query = new BattleQueryForm(Proxy<IPlayerProfileService>(), Proxy<IProfileIconService>(),
                Proxy<IMatchHistoryService>((_, _) => { detailRequests++; return Task.FromResult<MatchDetail?>(null); }), Proxy<IRankedStatsService>(),
                Proxy<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer>()), Proxy<ITextExportService>());
            typeof(BattleQueryForm).GetField("_currentPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(query, new PlayerProfile("me", "preview", "CN1", 1, 0, 0, 0));
            typeof(BattleQueryForm).GetField("_pageSize", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(query, pageSize);
            var page = new MatchHistoryResponse
            {
                Games = new() { GameCount = 101, Games = Enumerable.Range(1, 101)
                    .Select(id => new MatchHistoryGame { GameId = id, GameCreation = id }).ToList() }
            };
            var task = (Task)typeof(BattleQueryForm).GetMethod("RenderMatchPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(query, new object[] { page })!;
            Assert.True(task.IsCompletedSuccessfully);
            var list = (Control)typeof(BattleQueryForm).GetField("stackMatches", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(query)!;
            Assert.Equal(pageSize, list.Controls.Count);
            Assert.Equal(0, detailRequests);
            Assert.All(list.Controls.Cast<Control>(), control => Assert.IsType<MatchHistoryListRow>(control));
        });

    private static T Proxy<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, BattleOverlayTests.ServiceProxy>();
        ((BattleOverlayTests.ServiceProxy)(object)proxy).Handler = handler ?? ((_, _) => null);
        return proxy;
    }
}
