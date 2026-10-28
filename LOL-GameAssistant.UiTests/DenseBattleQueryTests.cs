using System.Reflection;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class DenseBattleQueryTests
{
    private sealed class Champions : IChampionCatalog
    {
        public string GetDisplayName(int id) => (id % 4) switch { 0 => "九尾妖狐", 1 => "盲僧", 2 => "赏金猎人", _ => "寒冰射手" };
        public IReadOnlyList<ChampionReference> GetAll() => Enumerable.Range(1, 4).Select(id => new ChampionReference(id, GetDisplayName(id))).ToArray();
        public int? FindIdByDisplayName(string? displayName) => null;
    }
    private static IGameAssetService Assets => Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
    private static MatchHistoryGame Game(long id) => new()
    {
        GameId = id, GameCreation = 1792400000000 - id * 3600000, GameDuration = 1800 + (int)id * 10,
        GameMode = "CLASSIC", GameType = "MATCHED_GAME", QueueId = id % 3 == 0 ? 450 : 420, PlatformId = "HN1",
        ParticipantIdentities = [new() { ParticipantId = 1, Player = new() { Puuid = "me", GameName = "预览玩家", TagLine = "123" } }],
        Participants = [new() { participantId = 1, championId = (int)(id % 4 + 1), teamId = 100, stats = new()
        { kills = 9, deaths = 3, assists = 12, Win = id % 3 != 0, HasFarmResult = true, totalMinionsKilled = 181,
            HasDamageResult = true, totalDamageDealtToChampions = 28500, HasVisionResult = true, visionScore = 25 } }]
    };
    private static MatchDetail Detail(long id, bool cross = false)
    {
        var detail = Game(id).ToSummaryDetail(); detail.IsCrossRegion = cross;
        for (int i = 2; i <= 10; i++)
        {
            detail.participantIdentities.Add(new() { participantId = i, player = new() { Puuid = "p" + i, GameName = "玩家" + i, TagLine = i.ToString() } });
            detail.participants.Add(new() { participantId = i, championId = i, teamId = i > 5 ? 200 : 100,
                stats = new() { kills = i, assists = 12, deaths = 4, HasDamageResult = true, totalDamageDealtToChampions = 2000 * i } });
        }
        return detail;
    }
    private static MatchHistoryResponse Page(int count = 20) => new() { Games = new()
    { Source = "SGP", IsTotalKnown = false, HasMore = true, GameCount = count + 1, Games = Enumerable.Range(1, count).Select(i => Game(i)).ToList() } };
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static BattleQueryForm Create(Func<long, bool, CancellationToken, Task<MatchDetail?>> detail, Task<MatchHistoryResponse?>? pendingPage = null)
    {
        var histories = Service<IMatchHistoryService>((method, args) => method.Name switch
        {
            "GetDetailAsync" => detail((long)args[0]!, (bool)args[1]!, (CancellationToken)args[2]!),
            _ => pendingPage ?? Task.FromResult<MatchHistoryResponse?>(Page())
        });
        var query = new BattleQueryForm(Service<IPlayerProfileService>((method, _) => Task.FromResult<PlayerProfile?>(method.Name == "GetCurrentAsync" ? null : new("me", "预览玩家", "123", 0, 411, 0, 0))),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)), histories,
            Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)), Service<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer>()),
            Service<ITextExportService>((_, _) => Task.CompletedTask), listAssets: Assets, listChampions: new Champions());
        query.GetType().GetField("_currentPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(query, new PlayerProfile("me", "预览玩家", "123", 0, 411, 0, 0));
        return query;
    }
    private static Task Render(BattleQueryForm query) => (Task)query.GetType().GetMethod("RenderMatchPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(query, [Page()])!;

    [Fact]
    public void InitialListMakesZeroDetailCallsAndContainsTwentyDenseRows() => MatchListScrollingTests.OnUiThread(() =>
    {
        int calls = 0; using var query = Create((id, _, _) => { calls++; return Task.FromResult<MatchDetail?>(Detail(id)); });
        Pump(Render(query));
        Assert.Equal(20, Field<int>(query, "_pageSize")); Assert.Equal(0, calls);
        Assert.True(Field<SplitContainer>(query, "_matchWorkspace").Panel2Collapsed);
        var rows = Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>().ToArray();
        Assert.Equal(20, rows.Length); Assert.All(rows, r => Assert.InRange(r.Height, 45, 55));
    });

    [Fact]
    public void SwitchingLanguageTranslatesFiltersWithoutChangingTheirMeaningOrReloadingHistory() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiLanguage.Mode;
        try
        {
            UiLanguage.SetMode("zh-CN");
            using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)));
            using var host = new Form { ClientSize = new Size(1100, 760), ShowInTaskbar = false };
            query.Dock = DockStyle.Fill; host.Controls.Add(query); host.Show();
            Pump(Render(query));
            var mode = Field<AntdUI.Select>(query, "_modeSelect");
            var champion = Field<AntdUI.Select>(query, "_championFilter");
            var result = Field<AntdUI.Select>(query, "_resultFilter");
            result.SelectedIndex = 1;
            PumpUntil(() => Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>().Count() == 14);
            var originalRows = Field<Control>(query, "stackMatches").Controls.Cast<Control>().ToArray();
            UiLanguage.SetMode("en-US"); UiLanguage.Refresh();
            Assert.Equal("All", mode.Text);
            Assert.Equal("All champions", champion.Text);
            Assert.Equal("All results", result.Items[0]?.ToString());
            Assert.Equal(UiLanguage.T("胜利"), result.Text);
            Assert.Equal("胜利", Field<string>(query, "_selectedResult"));
            Assert.Equal(originalRows, Field<Control>(query, "stackMatches").Controls.Cast<Control>().ToArray());
            Assert.Equal("Export matches", Field<AntdUI.Button>(query, "btnExport").Text);
            Assert.True(Field<AntdUI.Button>(query, "btnExport").Width >= 110);
            UiLanguage.SetMode("zh-CN"); UiLanguage.Refresh();
            Assert.Equal("全部", mode.Text); Assert.Equal("全部英雄", champion.Text); Assert.Equal("胜利", result.Text);
            Assert.Equal(originalRows, Field<Control>(query, "stackMatches").Controls.Cast<Control>().ToArray());
        }
        finally { UiLanguage.SetMode(previous); }
    });

    [Fact]
    public void AsyncFirstPageDoesNotCreateHorizontalScrollbarWhenVerticalScrollbarAppears() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchHistoryResponse?>();
        using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)), pending.Task);
        using var host = new Form { ClientSize = new(1920, 1080) }; query.Dock = DockStyle.Fill; host.Controls.Add(query); host.Show();
        var loading = query.PerformSearchAsync("11111111-1111-1111-1111-111111111111"); System.Windows.Forms.Application.DoEvents();
        Assert.False(loading.IsCompleted); pending.SetResult(Page()); Pump(loading); System.Windows.Forms.Application.DoEvents();
        var list = Field<Panel>(query, "stackMatches");
        Assert.False(list.HorizontalScroll.Visible);
        Assert.All(list.Controls.Cast<Control>(), row => Assert.True(row.Right <= list.ClientSize.Width));
        host.Close();
    });

    [Fact]
    public void SelectionLoadsOnlyOneMatchAndReusesSelectedDetailCache() => MatchListScrollingTests.OnUiThread(() =>
    {
        var calls = new List<long>(); using var query = Create((id, _, _) => { calls.Add(id); return Task.FromResult<MatchDetail?>(Detail(id)); });
        Pump(Render(query)); Pump(query.SelectMatchAsync(Game(1))); Pump(query.SelectMatchAsync(Game(2))); Pump(query.SelectMatchAsync(Game(1)));
        Assert.Equal(new long[] { 1, 2 }, calls); Assert.Equal(1, Field<MatchHistoryDetailPane>(query, "_detailPane").Detail!.GameId);
        Assert.Single(Field<Control>(query, "stackMatches").Controls.OfType<MatchHistoryListRow>(), r => r.Selected);
    });

    [Fact]
    public void LateOldDetailsCannotOverwriteNewSelectionAndCloseCancelsWait() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>();
        using var query = Create((id, _, _) => id == 1 ? pending.Task : Task.FromResult<MatchDetail?>(Detail(id)));
        Pump(Render(query)); var old = query.SelectMatchAsync(Game(1)); Pump(query.SelectMatchAsync(Game(2)));
        pending.SetResult(Detail(1)); Pump(old);
        Assert.Equal(2, Field<MatchHistoryDetailPane>(query, "_detailPane").Detail!.GameId);
        query.CloseSelectedDetail(); Assert.True(Field<SplitContainer>(query, "_matchWorkspace").Panel2Collapsed);
    });

    [Fact]
    public void ErrorStaysInDetailPaneAndRetryDoesNotReloadList() => MatchListScrollingTests.OnUiThread(() =>
    {
        int calls = 0; bool? useCache = null;
        using var query = Create((id, cache, _) => { useCache = cache; return ++calls == 1 ? Task.FromException<MatchDetail?>(new IOException("离线")) : Task.FromResult<MatchDetail?>(Detail(id)); });
        Pump(Render(query)); var list = Field<Control>(query, "stackMatches"); var row = list.Controls[0];
        Pump(query.SelectMatchAsync(Game(1))); Assert.Contains("离线", Field<MatchHistoryDetailPane>(query, "_detailPane").StateText);
        Assert.Equal(20, list.Controls.Count); Pump(query.SelectMatchAsync(Game(1), true));
        Assert.False(useCache); Assert.Equal(1, Field<MatchHistoryDetailPane>(query, "_detailPane").Detail!.GameId); Assert.Same(row, list.Controls[0]);
    });

    [Fact]
    public void DisposingWindowCancelsSelectedRequestWithoutUpdatingDisposedControls() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>(); CancellationToken networkToken = default;
        var query = Create((_, _, token) => { networkToken = token; return pending.Task; });
        Pump(Render(query)); var selected = query.SelectMatchAsync(Game(1)); query.Dispose();
        Assert.True(networkToken.IsCancellationRequested); pending.SetResult(Detail(1)); Pump(selected);
        Assert.True(selected.IsCompletedSuccessfully);
    });

    [Fact]
    public void MissingWinAndKdaAreNotRenderedAsDefeatOrZero() => MatchListScrollingTests.OnUiThread(() =>
    {
        var game = Game(1); game.Participants[0].stats!.HasWinResult = false; game.Participants[0].stats!.HasKdaResult = false;
        using var row = new MatchHistoryListRow(game, "me", Assets, new Champions());
        Assert.Contains("未知", row.AccessibleName); Assert.Contains("—", row.AccessibleName); Assert.DoesNotContain("失败", row.AccessibleName);
    });

    [Fact]
    public void ClearingViewCacheDuringRequestPreventsLateCacheRepopulation() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>(); int calls = 0;
        using var query = Create((id, _, _) => ++calls == 1 ? pending.Task : Task.FromResult<MatchDetail?>(Detail(id)));
        Pump(Render(query)); var first = query.SelectMatchAsync(Game(1)); var cache = (ILocalCacheStore)query;
        cache.Clear(); pending.SetResult(Detail(1)); Pump(first); Assert.Equal(0, cache.MeasureMemory().Entries);
        Pump(query.SelectMatchAsync(Game(1))); Assert.Equal(2, calls);
    });

    [Fact]
    public void ClosingWhilePendingDoesNotReopenPanelOrStartOtherMatches() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<MatchDetail?>(); using var query = Create((_, _, _) => pending.Task);
        Pump(Render(query)); var request = query.SelectMatchAsync(Game(1)); query.CloseSelectedDetail();
        pending.SetResult(Detail(1)); Pump(request);
        Assert.True(Field<SplitContainer>(query, "_matchWorkspace").Panel2Collapsed);
        Assert.Null(Field<MatchHistoryDetailPane>(query, "_detailPane").Detail);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(10)]
    public void WorkspaceDefersOrientationChangeUntilBothPanelsFit(int height) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)));
        var workspace = Field<SplitContainer>(query, "_matchWorkspace");
        workspace.Dock = DockStyle.None;
        workspace.Size = new Size(1200, 600);
        workspace.Panel2Collapsed = false;
        workspace.Size = new Size(900, height);
        Assert.Equal(Orientation.Vertical, workspace.Orientation);
        workspace.Size = new Size(900, 600);
        Assert.Equal(Orientation.Horizontal, workspace.Orientation);
        Assert.InRange(workspace.SplitterDistance, 1, workspace.Height - workspace.SplitterWidth - 1);
        workspace.Size = Size.Empty;
        Assert.Equal(Orientation.Horizontal, workspace.Orientation);
        workspace.Size = new Size(1400, 700);
        Assert.Equal(Orientation.Vertical, workspace.Orientation);
        Assert.InRange(workspace.SplitterDistance, 1, workspace.Width - workspace.SplitterWidth - 1);
    });

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void ScaledWorkspaceCanRepeatedlyResizeWithDetailPaneOpen(float scale) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)));
        var workspace = Field<SplitContainer>(query, "_matchWorkspace");
        workspace.Dock = DockStyle.None;
        query.Scale(new SizeF(scale, scale));
        workspace.Panel2Collapsed = false;
        int unit = query.DeviceDpi;
        for (int i = 0; i < 3; i++)
        {
            workspace.Size = new Size(1600 * unit / 96, 700 * unit / 96);
            Assert.Equal(Orientation.Vertical, workspace.Orientation);
            workspace.Size = new Size(900 * unit / 96, workspace.SplitterWidth - 1);
            Assert.Equal(Orientation.Vertical, workspace.Orientation);
            workspace.Size = new Size(900 * unit / 96, 600 * unit / 96);
            Assert.Equal(Orientation.Horizontal, workspace.Orientation);
            Assert.InRange(workspace.SplitterDistance, 1, workspace.Height - workspace.SplitterWidth - 1);
        }
        Assert.False(workspace.Panel2Collapsed);
    });

    [Theory]
    [InlineData(1600, 1000, true)]
    [InlineData(1920, 1080, true)]
    [InlineData(1280, 900, false)]
    [InlineData(900, 820, true)]
    [InlineData(520, 820, true)]
    public void RealLayoutKeepsDenseListAndSelectedDetailsUsable(int width, int height, bool dark) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(dark ? "Dark" : "Light");
            using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)));
            using var host = new Form { ClientSize = new(width, height) }; query.Dock = DockStyle.Fill; host.Controls.Add(query); host.Show(); UiTheme.Apply(query);
            Pump(query.PerformSearchAsync("11111111-1111-1111-1111-111111111111")); System.Windows.Forms.Application.DoEvents();
            var list = Field<Panel>(query, "stackMatches");
            Assert.True(list.Height >= 190, $"Insufficient list height {list.Height} at {width}");
            Assert.False(list.HorizontalScroll.Visible);
            Save(host, $"dense-query-list-{width}");
            list.AutoScrollPosition = new Point(0, 200); int scroll = list.AutoScrollPosition.Y;
            Pump(query.SelectMatchAsync(Game(3))); System.Windows.Forms.Application.DoEvents();
            Assert.False(Field<SplitContainer>(query, "_matchWorkspace").Panel2Collapsed);
            Assert.Equal(3, Field<MatchHistoryDetailPane>(query, "_detailPane").Detail!.GameId);
            Assert.False(list.HorizontalScroll.Visible);
            if (width >= 1000) Assert.Equal(scroll, list.AutoScrollPosition.Y);
            Save(host, $"dense-query-detail-{width}"); host.Close();
        }
        finally { UiTheme.SetMode(previous); }
    });
    [Theory]
    [InlineData(1600, 900)]
    [InlineData(900, 820)]
    [InlineData(520, 820)]
    public void StatisticsChartsAndAllSummaryTilesFitTheViewport(int width, int height) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var query = Create((id, _, _) => Task.FromResult<MatchDetail?>(Detail(id)));
        using var host = new Form { ClientSize = new(width, height) };
        query.Dock = DockStyle.Fill; host.Controls.Add(query); host.Show();
        Pump(query.PerformSearchAsync("11111111-1111-1111-1111-111111111111"));
        typeof(BattleQueryForm).GetMethod("BtnViewStats_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(query, [null, EventArgs.Empty]);
        PumpUntil(() => Field<bool>(query, "_statsLoaded"));
        System.Windows.Forms.Application.DoEvents();
        var stats = Field<Control>(query, "panelStats");
        var viewport = stats.Controls.OfType<AntdUI.Panel>().Single(control => control.Dock == DockStyle.Fill);
        var content = viewport.Controls.OfType<FlowLayoutPanel>().Single();
        var summaries = content.Controls.OfType<FlowLayoutPanel>().Single();
        Save(host, $"dense-query-stats-{width}");
        Assert.True(content.Width <= viewport.ClientSize.Width);
        Assert.Equal(3, summaries.Controls.Count);
        foreach (Control tile in summaries.Controls)
        {
            Assert.True(summaries.ClientRectangle.Contains(tile.Bounds), $"Tile {tile.Bounds} clipped by {summaries.ClientRectangle}");
            Assert.Equal(2, tile.Controls.Count);
        }
        Assert.All(content.Controls.Cast<Control>(), chart => Assert.InRange(chart.Width, viewport.ClientSize.Width - 50, viewport.ClientSize.Width));
        Assert.True(content.Height >= content.Controls.Cast<Control>().Sum(chart => chart.Height));
        host.Close();
    });

    private static void Save(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, name + ".png"));
    }
}
