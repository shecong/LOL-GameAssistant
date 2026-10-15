using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UiRedesignTests
{
    [Fact]
    public void TenPlayerRosterUsesTwoColumnsAndShowsLoadedTeamOverview() => MatchListScrollingTests.OnUiThread(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        LivePlayerForm.ClearSharedCaches();
        using var live = new LiveGameForm { Size = new Size(1440, 850) };
        var first = Field<FlowLayoutPanel>(live, "panelTeam1");
        var second = Field<FlowLayoutPanel>(live, "panelTeam2");
        var cards = new List<LivePlayerForm>();
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        var match = SampleMatch(true);
        for (int i = 1; i <= 10; i++)
        {
            int id = i;
            var rank = new RankedOverview();
            rank.Queues[RankedQueues.Solo5x5] = new RankedQueue { Tier = "EMERALD", Division = "II" };
            var card = new LivePlayerForm($"player-{id}", $"召唤师 {id}", 22, "TOP", false, id <= 5, true, 420, "CLASSIC",
                Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new($"player-{id}", $"召唤师 {id}", "12345", 0, 100 + id, 0, 100))),
                Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
                Service<IMatchHistoryService>((method, _) => method.Name == "GetDetailAsync"
                    ? Task.FromResult<MatchDetail?>(match)
                    : Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = Enumerable.Range(101, 10).Select(gameId => new MatchHistoryGame {
                        GameId = gameId, GameCreation = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                        QueueId = 420, GameMode = "CLASSIC", GameDuration = 1456 }).ToList() } })),
                assets, Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(rank)))
                { Margin = new Padding(0, 0, 10, 10), Height = 440 };
            cards.Add(card);
            (id <= 5 ? first : second).Controls.Add(card);
        }
        match.participants[9].stats!.HasWinResult = false;
        using var host = new Form { ClientSize = new Size(1440, 850), ShowInTaskbar = false };
        live.Dock = DockStyle.Fill;
        host.Controls.Add(live);
        host.Show();
        System.Windows.Forms.Application.DoEvents();
        typeof(LiveGameForm).GetField("_encounterSelfPuuid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(live, "player-1");
        typeof(LiveGameForm).GetMethod("UpdateRecentEncounters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(live, null);
        Assert.Contains("KDA", cards[0].Controls.Find("lblRecentKda", true).Single().Text);
        Assert.Equal("近期遇到 10 次", cards[1].Controls.Find("lblRecentEncounter", true).Single().Text);
        Assert.Equal(cards[0].Top, cards[1].Top);
        Assert.True(cards[1].Left > cards[0].Left);
        Assert.Equal(cards[5].Top, cards[6].Top);
        Assert.All(cards.Take(9), card => Assert.Equal(10, card.RecentSampleSize));
        Assert.Equal(0, cards[9].RecentSampleSize);
        Assert.Contains("103", LiveGameForm.DescribeTeam(cards.Take(5)));
        Assert.Contains("100%", LiveGameForm.DescribeTeam(cards.Take(5)));
        Assert.Contains("0%", LiveGameForm.DescribeTeam(cards.Skip(5)));
        var rows = Field<Control>(cards[0], "panelMatches").Controls.OfType<RecentMatchRow>().ToArray();
        Assert.Equal(10, rows.Length);
        Assert.True(Field<Control>(cards[0], "_rankLabel").Width > 0);
        Assert.True(Field<Control>(rows[0], "lblMode").Visible);
        foreach (int width in new[] { 1440, 1670 })
        {
            host.ClientSize = new Size(width, 850);
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(cards[0].Top, cards[1].Top);
            Assert.True(cards[1].Right <= first.ClientSize.Width);
        }
        var heading = Field<Control>(cards[0], "_historyHeading");
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(heading, [EventArgs.Empty]);
        Assert.True(Field<Control>(cards[0], "panelMatches").Visible);
        // 模拟自动刷新重挂控件后的 DPI 外边距变化，不改变窗口宽度。
        foreach (var card in cards) card.Margin = new Padding(0, 0, 18, 12);
        first.AutoScrollPosition = new Point(0, 200);
        second.AutoScrollPosition = new Point(0, 200);
        first.PerformLayout();
        second.PerformLayout();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(cards[0].Top, cards[1].Top);
        Assert.Equal(cards[5].Top, cards[6].Top);
        first.AutoScrollPosition = second.AutoScrollPosition = Point.Empty;
        host.ClientSize = new Size(1440, 850);
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(440, cards[0].Height);
        Assert.Equal("最近战绩", heading.Text);
        Assert.True(Field<Control>(cards[0], "panelMatches").Visible);
        host.Controls.Remove(live);
        typeof(LiveGameForm).GetMethod("UpdateTeamOverview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(live, null);
        Field<Control>(live, "lblGameInfo").Text = "位于大厅 · 模式: 峡谷单排 · 队列: 420";
        Capture(live, "live-roster");
        SynchronizationContext.SetSynchronizationContext(null);
    });

    [Fact]
    public void WinAndLossHomeRecordsHaveDifferentSurfacesAndVisibleSeparators() => MatchListScrollingTests.OnUiThread(() =>
    {
        var matches = Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(null));
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        using var win = new RecordForm(matches, assets);
        using var loss = new RecordForm(matches, assets);
        foreach (var (record, won) in new[] { (win, true), (loss, false) })
        {
            record.setInfo(new MatchHistoryGame { Participants = [new() { stats = new() { Win = won } }] }, "player").GetAwaiter().GetResult();
            record.ApplyTheme(UiTheme.Palette with { IsDark = true });
            using var bitmap = new Bitmap(record.Width, record.Height);
            record.DrawToBitmap(bitmap, record.ClientRectangle);
            Assert.NotEqual(record.BackColor.ToArgb(), bitmap.GetPixel(3, record.Height - 20).ToArgb());
        }
        Assert.NotEqual(win.BackColor, loss.BackColor);
    });

    [Fact]
    public void SearchByNameRefreshesViewedPlayerAndFilterRepaginatesLoadedGames() => MatchListScrollingTests.OnUiThread(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var requested = new List<string>();
        var profiles = Service<IPlayerProfileService>((method, args) =>
        {
            requested.Add(method.Name == "GetByPuuidAsync" ? args[0]!.ToString()! : method.Name);
            return Task.FromResult<PlayerProfile?>(new("other-player", "测试召唤师", "CN", 0, 120, 20, 100));
        });
        var games = Enumerable.Range(1, 12).Select(id => new MatchHistoryGame
            { GameId = id, GameCreation = id, QueueId = id % 2 == 0 ? 450 : 420 }).ToList();
        var matches = Service<IMatchHistoryService>((method, args) => method.Name == "GetDetailAsync"
            ? Task.FromResult<MatchDetail?>(HistoryDetail((long)args[0]!))
            : Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = games } }));
        MatchDetail HistoryDetail(long id)
        {
            var detail = SampleMatch(true);
            detail.GameId = id;
            detail.queueId = id % 2 == 0 ? "450" : "420";
            detail.participantIdentities[0].player!.Puuid = "other-player";
            return detail;
        }
        var assets = Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
        using var home = CreateHome(profiles: profiles, matches: matches, recordFactory: () => new RecordForm(matches, assets));
        Pump(home.LoadPlayerAsync(null, "测试召唤师"));
        Assert.Contains("FindByNameAsync", requested);
        Pump(home.RefreshAsync());
        Assert.Equal("other-player", requested.Last());
        Assert.DoesNotContain("GetCurrentAsync", requested);
        var mode = Field<AntdUI.Select>(home, "_recentMode");
        mode.SelectedValue = games[1].GetModeText();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(6, Field<AntdUI.Pagination>(home, "game_pagin").Total);
        Assert.Equal(6, Field<Control>(home, "stackPanel1").Controls.Count);
        // 渲染已加载的数据，不在预览时重新发起首页 Load 查询。
        var load = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), home,
            typeof(HomeForm).GetMethod("HomeForm_Load", BindingFlags.NonPublic | BindingFlags.Instance)!);
        home.Load -= load;
        Capture(home, "home");
        SynchronizationContext.SetSynchronizationContext(null);
    });

    [Theory]
    [InlineData(650)]
    [InlineData(1300)]
    public void MissingEnemyDoesNotReserveAnEmptyTeamPanel(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        var match = SampleMatch(false);
        using var card = CreateCard(match);
        card.Width = width;
        Assert.False(Field<Control>(card, "_redTeam").Visible);
        Assert.Equal(Field<Control>(card, "_blueTeam").Width,
            Field<Control>(card, "_teamHost").Width - (width >= 1180 ? 24 : 0));
    });

    [Fact]
    public void MatchCardKeepsBothTeamsInsideAtWideAndNarrowWidths() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        bool previousAntd = AntdUI.Config.IsDark;
        UiTheme.SetMode("Dark");
        AntdUI.Config.IsDark = true;
        try
        {
            using var card = CreateCard(SampleMatch(true));
            foreach (int width in new[] { 650, 900, 1300 })
            {
                card.Width = width;
                var host = Field<Control>(card, "_teamHost");
                Assert.True(host.ClientRectangle.Contains(Field<Control>(card, "_blueTeam").Bounds));
                Assert.True(host.ClientRectangle.Contains(Field<Control>(card, "_redTeam").Bounds));
            }
            Capture(card, "match-history");
        }
        finally { UiTheme.SetMode(previous); AntdUI.Config.IsDark = previousAntd; }
    });

    [Fact]
    public void SettingsUseSideNavigationAndLivePageExposesManualFetch() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var settings = new SettingForm(Service<IGameClientLauncher>((_, _) => null),
            Service<IApplicationSettingsStore>((method, _) => method.Name switch
            { "Load" => new AssistantSettings(), "GetStoragePath" => "preview-settings.json", _ => null }),
            Service<ISettingsSecretProtector>((_, _) => null), Service<IClientFeatureService>((_, _) => null));
        var navigation = Field<AntdUI.Segmented>(settings, "_settingsSegmented");
        Assert.True(navigation.Vertical);
        Assert.Equal(DockStyle.Left, navigation.Dock);
        var load = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), settings,
            typeof(SettingForm).GetMethod("SettingForm_Load", BindingFlags.NonPublic | BindingFlags.Instance)!);
        settings.Load -= load;
        Capture(settings, "settings");
        using var live = new LiveGameForm();
        Assert.Contains(Field<Control>(live, "infoBar").Controls.Cast<Control>(), control => control.Name == "fetchAllPlayers");
        Capture(live, "live-game");
    });

    private static MatchHistoryCard CreateCard(MatchDetail match) => new(match, "player-1",
        Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)),
        Service<IPremadeDetectionService>((_, _) => Task.FromResult(new PremadeDetectionResult([]))));

    [Fact]
    public void HomeRecordRetainsSummaryWhenDetailEndpointReturnsNothing() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var record = new RecordForm(
            Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(null)),
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));
        record.setInfo(new MatchHistoryGame { QueueId = 450, GameDuration = 720,
            Participants = [new() { stats = new() { Win = true, kills = 9, deaths = 3, assists = 12 } }] }, "player")
            .GetAwaiter().GetResult();
        Assert.Equal("胜利", Field<AntdUI.Label>(record, "game_win").Text);
        Assert.Equal("9/3/12", Field<AntdUI.Label>(record, "game_msg").Text);
        Assert.Equal("深渊大乱斗", Field<AntdUI.Label>(record, "game_type").Text);
        Assert.False(Field<Control>(record, "flowEnemy").Visible);
    });

    internal static MatchDetail SampleMatch(bool enemy)
    {
        var match = new MatchDetail { GameId = 101, gameCreationDate = "2026-10-14 12:00", gameDuration = 1456, queueId = "420", gameMode = "CLASSIC" };
        for (int id = 1; id <= (enemy ? 10 : 1); id++)
        {
            match.participants.Add(new() { participantId = id, teamId = id <= 5 ? 100 : 200, championId = 22,
                stats = new() { Win = id <= 5, kills = id, deaths = 3, assists = 12, totalDamageDealtToChampions = id * 2500 } });
            match.participantIdentities.Add(new() { participantId = id, player = new() { Puuid = $"player-{id}", GameName = $"召唤师 {id}" } });
        }
        return match;
    }

    private static void Capture(Control control, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("UI_REDESIGN_PREVIEW");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        string previousMode = UiTheme.Mode;
        bool previousDark = AntdUI.Config.IsDark;
        UiTheme.SetMode("Dark");
        AntdUI.Config.IsDark = true;
        var palette = UiTheme.Palette;
        AntdUI.Style.Set(AntdUI.Colour.BgBase, palette.Surface);
        AntdUI.Style.Set(AntdUI.Colour.BgLayout, palette.Surface);
        AntdUI.Style.Set(AntdUI.Colour.BgContainer, palette.SurfaceRaised);
        AntdUI.Style.Set(AntdUI.Colour.BgElevated, palette.SurfaceMuted);
        using var host = new Form { ClientSize = new Size(1440, 850), ShowInTaskbar = false };
        control.Dock = DockStyle.Fill;
        host.Controls.Add(control);
        host.Show();
        UiTheme.Apply(host);
        System.Windows.Forms.Application.DoEvents();
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        bitmap.Save(Path.Combine(directory, name + ".png"));
        host.Controls.Remove(control);
        AntdUI.Style.Clear();
        UiTheme.SetMode(previousMode);
        AntdUI.Config.IsDark = previousDark;
    }
}
