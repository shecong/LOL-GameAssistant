using System.Reflection;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class LiveGameLayoutTests
{
    [Fact]
    public void NavigationSelectorRestoresSavesAndOnlyAppearsOnMatchTab() => MatchListScrollingTests.OnUiThread(() =>
    {
        var stored = new AssistantSettings { LiveGameLayout = LiveGameLayout.CompactCards, AutoRefresh = false };
        int saves = 0;
        var store = Service<IApplicationSettingsStore>((method, args) => method.Name switch
        {
            "Load" => stored,
            "Save" => Save((AssistantSettings)args[0]!),
            "GetStoragePath" => "test-settings.json", _ => null
        });
        object? Save(AssistantSettings value) { stored = value; saves++; return null; }
        using var main = new GameMain(Service<ILeagueClientEventStream>((_, _) => null),
            Service<ILobbyService>((_, _) => null), Service<IChampionSelectService>((_, _) => null),
            Service<IClientFeatureService>((_, _) => null), store, Service<IGameClientLauncher>((_, _) => null),
            Service<IRecommendationCoordinator>((_, _) => null));
        // Exercise native navigation without starting client monitoring, automation, or network services.
        main.Load -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), main, nameof(GameMain.GameMain_Load));
        main.Opacity = 0; main.ShowInTaskbar = false;
        main.ClientSize = new Size(1440, 840);
        main.liveGameForm.Dock = DockStyle.Fill;
        Find(main, "tab1_grid1").Controls.Add(main.liveGameForm);
        var selector = (AntdUI.Select)Find(main, "liveLayoutSelector");
        var tabs = (AntdUI.Tabs)Find(main, "tabs1");
        Assert.Equal(5, selector.Items.Count);
        Assert.Equal(4, selector.SelectedIndex);
        Assert.Equal(LiveGameLayout.CompactCards, main.liveGameForm.DisplayLayout);
        Assert.Equal(LiveGameLayout.CompactCards, ((LiveGameWorkspace)Find(main.liveGameForm, "liveGameWorkspace")).LayoutMode);
        Assert.Equal(0, saves);
        main.Show();
        System.Windows.Forms.Application.DoEvents();
        Assert.False(selector.Visible);
        tabs.SelectedIndex = 1;
        System.Windows.Forms.Application.DoEvents();
        Assert.True(selector.Visible);
        Assert.True(selector.Right <= main.ClientSize.Width);
        var settingsButton = Find(main, "liveGameSettings");
        Assert.True(settingsButton.Visible);
        Assert.True(settingsButton.Left >= selector.Right);
        selector.SelectedIndex = 2;
        Assert.Equal(LiveGameLayout.RoleComparison, stored.LiveGameLayout);
        Assert.Equal(LiveGameLayout.RoleComparison, main.liveGameForm.DisplayLayout);
        Assert.Equal(1, saves);
        main.ClientSize = new Size(1080, 740);
        System.Windows.Forms.Application.DoEvents();
        Assert.True(selector.Right <= main.ClientSize.Width);
        Assert.True(settingsButton.Left >= selector.Right);
        string? screenshots = Environment.GetEnvironmentVariable("LIVE_LAYOUT_SCREENSHOTS");
        if (!string.IsNullOrEmpty(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            string previous = UiTheme.Mode;
            try
            {
                UiTheme.SetMode("Dark"); UiTheme.Apply(main);
                main.ClientSize = new Size(1516, 860);
                var players = Enumerable.Range(0, 10).Select(index => CreatePlayer(index, () => { })).ToArray();
                var blue = Find(main.liveGameForm, "panelTeam1"); var red = Find(main.liveGameForm, "panelTeam2");
                for (int i = 0; i < players.Length; i++) (i < 5 ? blue : red).Controls.Add(players[i]);
                foreach (var player in players) player.EnsureDataLoadedAsync().GetAwaiter().GetResult();
                Find(main.liveGameForm, "lblGameInfo").Text = "布局预览 · 测试阵容 · 每名玩家最近十场";
                Find(main.liveGameForm, "lblTeamTitle1").Text = "蓝方 (5)";
                Find(main.liveGameForm, "lblTeamTitle2").Text = "红方 (5)";
                typeof(LiveGameForm).GetMethod("UpdateTeamOverview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main.liveGameForm, null);
                foreach (var layout in Enum.GetValues<LiveGameLayout>())
                {
                    selector.SelectedIndex = (int)layout;
                    System.Windows.Forms.Application.DoEvents();
                    if (layout == LiveGameLayout.Classic)
                        Assert.All(players, player => Assert.False(((Panel)Find(player, "panelMatches")).HorizontalScroll.Visible));
                    using var capture = new Bitmap(main.Width, main.Height);
                    main.DrawToBitmap(capture, new Rectangle(Point.Empty, main.Size));
                    capture.Save(Path.Combine(screenshots, "Full-" + layout + ".png"));
                }
                System.Windows.Forms.Application.DoEvents();
                using var bitmap = new Bitmap(main.Width, main.Height);
                main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size));
                bitmap.Save(Path.Combine(screenshots, "Navigation-Dark.png"));
            }
            finally { UiTheme.SetMode(previous); }
        }
        tabs.SelectedIndex = 0;
        Assert.False(selector.Visible);
    });

    [Fact]
    public void LayoutPreferenceRoundTripsAndUnknownValuesFallBackToClassic()
    {
        foreach (var layout in Enum.GetValues<LiveGameLayout>())
        {
            var saved = LegacySettingsMapper.ToLegacy(new AssistantSettings { LiveGameLayout = layout });
            Assert.Equal(layout, LegacySettingsMapper.ToDomain(saved).LiveGameLayout);
        }
        Assert.Equal(LiveGameLayout.Classic, LegacySettingsMapper.ToDomain(new() { LiveGameLayout = (LiveGameLayout)999 }).LiveGameLayout);
    }

    [Fact]
    public void ClassicAndAlternateDetailsKeepAllTenMatchesReachableAfterSwitching() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var view = new LiveGameForm(Service<ILobbyService>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => null), Service<IChampionSelectService>((_, _) => null)) { Dock = DockStyle.Fill };
        using var window = new Form { ClientSize = new Size(1516, 760), Opacity = 0, ShowInTaskbar = false };
        window.Controls.Add(view);
        var player = CreatePlayer(0, () => { });
        Find(view, "panelTeam1").Controls.Add(player);
        window.Show();
        player.EnsureDataLoadedAsync().GetAwaiter().GetResult();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(10, player.LayoutHistory.Count);
        CheckScroll((Panel)Find(player, "panelMatches"));
        Assert.Contains("10 场", Find(player, "recentHistoryHeading").Text);
        foreach (var layout in Enum.GetValues<LiveGameLayout>().Where(mode => mode != LiveGameLayout.Classic))
        {
            view.SetDisplayLayout(layout);
            var workspace = (LiveGameWorkspace)Find(view, "liveGameWorkspace");
            workspace.SelectPlayer(player);
            System.Windows.Forms.Application.DoEvents();
            CheckScroll((Panel)Find(workspace, "layoutRecentMatches"));
            view.SetDisplayLayout(LiveGameLayout.Classic);
            System.Windows.Forms.Application.DoEvents();
            CheckScroll((Panel)Find(player, "panelMatches"));
        }
        static void CheckScroll(Panel panel)
        {
            var rows = panel.Controls.OfType<RecentMatchRow>().ToArray();
            Assert.Equal(10, rows.Length);
            Assert.True(panel.VerticalScroll.Visible);
            Assert.True(!panel.HorizontalScroll.Visible, $"Panel {panel.Name}: client={panel.ClientSize}, display={panel.DisplayRectangle}, padding={panel.Padding}, first={rows[0].Bounds}, last={rows[^1].Bounds}");
            panel.AutoScrollPosition = new Point(0, 100000);
            System.Windows.Forms.Application.DoEvents();
            Assert.True(rows[^1].Top >= 0 && rows[^1].Bottom <= panel.ClientSize.Height,
                $"Tenth match must be reachable: {rows[^1].Bounds}, viewport={panel.ClientSize}");
            string? screenshots = Environment.GetEnvironmentVariable("LIVE_LAYOUT_SCREENSHOTS");
            if (panel.Name == "panelMatches" && screenshots != null && panel.Parent is { } card)
            {
                using var bitmap = new Bitmap(card.Width, card.Height);
                card.DrawToBitmap(bitmap, card.ClientRectangle);
                bitmap.Save(Path.Combine(screenshots, "Classic-TenthMatch.png"));
            }
            panel.AutoScrollPosition = Point.Empty;
        }
    });

    [Fact]
    public void SwitchingEveryLayoutKeepsOriginalCardsAndDoesNotRequestHistory() => MatchListScrollingTests.OnUiThread(() =>
    {
        int requests = 0;
        using var view = new LiveGameForm(Service<ILobbyService>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => null), Service<IChampionSelectService>((_, _) => null));
        using var window = new Form { ClientSize = new Size(1440, 820), Opacity = 0, ShowInTaskbar = false };
        view.Dock = DockStyle.Fill;
        window.Controls.Add(view);
        var blue = Find(view, "panelTeam1"); var red = Find(view, "panelTeam2");
        var players = Enumerable.Range(0, 10).Select(index => CreatePlayer(index, () => requests++)).ToArray();
        for (int i = 0; i < 10; i++) (i < 5 ? blue : red).Controls.Add(players[i]);
        window.Show();
        foreach (var player in players) player.EnsureDataLoadedAsync().GetAwaiter().GetResult();
        System.Windows.Forms.Application.DoEvents();
        int loadedRequests = requests;
        Assert.Equal(10, requests);
        Assert.All(players, player => Assert.NotNull(player.LayoutRankEmblem));
        foreach (var layout in Enum.GetValues<LiveGameLayout>().Concat(Enum.GetValues<LiveGameLayout>().Reverse()))
        {
            view.SetDisplayLayout(layout);
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(layout, view.DisplayLayout);
            Assert.Equal(5, blue.Controls.OfType<LivePlayerForm>().Count());
            Assert.Equal(5, red.Controls.OfType<LivePlayerForm>().Count());
            Assert.All(players, player => Assert.False(player.IsDisposed));
            if (layout != LiveGameLayout.Classic)
            {
                var workspace = (LiveGameWorkspace)Find(view, "liveGameWorkspace");
                workspace.SelectPlayer(players[7]);
                Assert.Same(players[7], workspace.SelectedPlayer);
            }
            Assert.Equal(loadedRequests, requests);
        }
        Assert.True(blue.Visible);
        Assert.True(red.Visible);
        Assert.False(Find(view, "liveGameWorkspace").Visible);
        Assert.All(players, player =>
        {
            var matches = (Panel)Find(player, "panelMatches");
            Assert.Equal(10, matches.Controls.OfType<RecentMatchRow>().Count());
            Assert.False(matches.HorizontalScroll.Visible);
        });
    });

    [Fact]
    public void AllNewLayoutsFitWithoutHorizontalScrollingAndPreserveSelection() => MatchListScrollingTests.OnUiThread(() =>
    {
        var players = Enumerable.Range(0, 10).Select(index => CreatePlayer(index, () => { })).ToArray();
        using var originals = new Panel();
        originals.Controls.AddRange(players);
        using var workspace = new LiveGameWorkspace { Dock = DockStyle.Fill };
        using var window = new Form { ClientSize = new Size(1440, 740), Opacity = 0, ShowInTaskbar = false };
        window.Controls.Add(workspace);
        window.Show();
        workspace.SetPlayers(players[..5], players[5..], "蓝方 5人", "红方 5人");
        foreach (var player in players) player.EnsureDataLoadedAsync().GetAwaiter().GetResult();
        foreach (var layout in Enum.GetValues<LiveGameLayout>().Where(layout => layout != LiveGameLayout.Classic))
        {
            workspace.SetLayout(layout);
            workspace.SelectPlayer(players[3]);
            foreach (var size in new[] { new Size(1440, 740), new Size(1080, 640), new Size(1600, 900) })
            {
                window.ClientSize = size;
                System.Windows.Forms.Application.DoEvents();
                Assert.Same(players[3], workspace.SelectedPlayer);
                Assert.Equal(10, All(workspace).Count(control => control.GetType().Name == "PlayerTile"));
                var canvas = (Panel)Find(workspace, "layoutCanvas");
                Assert.True(!canvas.HorizontalScroll.Visible, $"{layout} {size}: canvas={canvas.ClientSize}, grid={canvas.Controls[0].Bounds}, scroll={canvas.AutoScrollPosition}");
                Assert.All(All(workspace).Where(control => control.GetType().Name == "PlayerTile"), tile => Assert.True(tile.Width >= 130));
            }
            string? screenshots = Environment.GetEnvironmentVariable("LIVE_LAYOUT_SCREENSHOTS");
            if (!string.IsNullOrEmpty(screenshots))
            {
                Directory.CreateDirectory(screenshots);
                window.ClientSize = new Size(1516, 700);
                // Rebuild to capture the unobscured overview rather than the detail drawer.
                workspace.SetLayout(LiveGameLayout.Classic); workspace.SetLayout(layout);
                System.Windows.Forms.Application.DoEvents();
                string previousMode = UiTheme.Mode;
                try
                {
                    foreach (string mode in new[] { "Light", "Dark" })
                    {
                        UiTheme.SetMode(mode); UiTheme.Apply(workspace);
                        System.Windows.Forms.Application.DoEvents();
                        using var bitmap = new Bitmap(workspace.Width, workspace.Height);
                        workspace.DrawToBitmap(bitmap, workspace.ClientRectangle);
                        bitmap.Save(Path.Combine(screenshots, layout + "-" + mode + ".png"));
                    }
                }
                finally { UiTheme.SetMode(previousMode); }
            }
        }
    });

    [Fact]
    public void RoleComparisonRetainsUnassignedPlayersAndAllowsManualAssignment() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var player = CreatePlayer(1, () => { }, position: "");
        using var workspace = new LiveGameWorkspace { Size = new Size(1200, 740) };
        using var host = new Form { ClientSize = workspace.Size, ShowInTaskbar = false, Opacity = 0 };
        host.Controls.Add(workspace);
        host.Show();
        workspace.SetLayout(LiveGameLayout.RoleComparison);
        workspace.SetPlayers([player], [], "蓝方", "红方");
        Assert.Contains(All(workspace).OfType<AntdUI.Label>(), label => label.Text == "待确认");
        var tile = All(workspace).Single(control => control.GetType().Name == "PlayerTile");
        ContextMenuTestDriver.Click(tile, "打野");
        Assert.Equal("打野", workspace.PositionFor(player));
        Assert.Single(All(workspace), control => control.GetType().Name == "PlayerTile");
    });

    [Fact]
    public void ReplacingRosterDropsOldSelectionAndSubscriptions() => MatchListScrollingTests.OnUiThread(() =>
    {
        var first = CreatePlayer(1, () => { });
        using var next = CreatePlayer(2, () => { });
        using var workspace = new LiveGameWorkspace { Size = new Size(1200, 740) };
        workspace.SetLayout(LiveGameLayout.DetailWorkspace);
        workspace.SetPlayers([first], [], "蓝方", "红方");
        workspace.SelectPlayer(first);
        workspace.SetPlayers([], [next], "蓝方", "红方");
        Assert.Same(next, workspace.SelectedPlayer);
        first.Dispose();
        Assert.Single(All(workspace), control => control.GetType().Name == "PlayerTile");
        workspace.SetPlayers([], [], "蓝方", "红方");
        Assert.Null(workspace.SelectedPlayer);
    });

    [Theory]
    [InlineData(1280)]
    [InlineData(1600)]
    public void ClassicPrioritizesMatchViewportAndShowsAtLeastSixCompleteRows(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var view = new LiveGameForm(Service<ILobbyService>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => null), Service<IChampionSelectService>((_, _) => null)) { Dock = DockStyle.Fill };
        using var window = new Form { ClientSize = new Size(width, 800), Opacity = 0, ShowInTaskbar = false };
        window.Controls.Add(view);
        var players = Enumerable.Range(0, 10).Select(index => CreatePlayer(index, () => { })).ToArray();
        for (int i = 0; i < players.Length; i++)
            Find(view, i < 5 ? "panelTeam1" : "panelTeam2").Controls.Add(players[i]);
        window.Show();
        foreach (var player in players) player.EnsureDataLoadedAsync().GetAwaiter().GetResult();
        System.Windows.Forms.Application.DoEvents();
        Assert.True(Find(view, "infoBar").Height <= 48);
        Assert.True(Find(view, "headerTeam1").Height <= 40);
        foreach (var player in players)
        {
            var history = Find(player, "panelMatches");
            var rows = history.Controls.OfType<RecentMatchRow>().ToArray();
            Assert.Equal(10, rows.Length);
            Assert.True(history.Height >= player.Height * .7);
            Assert.True(rows.Count(row => row.Top >= 0 && row.Bottom <= history.ClientSize.Height) >= 6);
            Assert.All(rows, row =>
            {
                Assert.False(Find(row, "lblChampion").Bounds.IntersectsWith(Find(row, "lblKda").Bounds));
                Assert.False(Find(row, "matchHonors").Visible);
            });
        }
    });

    private static LivePlayerForm CreatePlayer(int index, Action onHistory, string? position = null)
    {
        string puuid = "layout-test-" + index + "-" + Guid.NewGuid();
        string[] names = ["666", "小熊软糖", "男明星", "壹刀九九", "林间风", "呆萌是天生", "冰淇淋", "777", "Story男段", "星河"];
        byte[] portrait = FixturePortrait(index);
        var rank = new RankedOverview();
        rank.Queues[RankedQueues.Solo5x5] = new RankedQueue { QueueType = RankedQueues.Solo5x5,
            Tier = new[] { "DIAMOND", "DIAMOND", "EMERALD", "DIAMOND", "GOLD" }[index % 5],
            Division = new[] { "I", "II", "IV", "III", "I" }[index % 5], LeaguePoints = 42 };
        return new(puuid, names[index % 10], 0, position ?? new[] { "top", "jungle", "middle", "bottom", "utility" }[index % 5],
            false, index < 5, true, 420, "CLASSIC",
            Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new(puuid, names[index % 10], "12345", 1, 200 + index, 0, 0))),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(portrait)),
            Service<IMatchHistoryService>((method, _) =>
            {
                Assert.Equal("GetPageAsync", method.Name); onHistory();
                return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = Enumerable.Range(1, 10).Select(game => new MatchHistoryGame {
                    GameId = 1000L * (index + 1) + game, GameCreation = DateTimeOffset.UtcNow.AddHours(-game).ToUnixTimeMilliseconds(),
                    QueueId = 420, GameMode = "CLASSIC", GameDuration = 1400 + game * 47,
                    Participants = [new() { championId = new[] { 17, 64, 117, 157, 103, 20, 34, 154, 238, 89 }[index],
                        stats = new() { kills = 5 + index, deaths = 3 + game % 4, assists = 8 + game % 5, Win = (game + index) % 10 < 4 + index % 4 } }]
                }).ToList() } });
            }),
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(new(portrait))),
            Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(rank)));
    }

    private static byte[] FixturePortrait(int index)
    {
        // Optional real static game artwork for visual review. Tests remain offline and self-contained.
        string? screenshotPath = Environment.GetEnvironmentVariable("LIVE_LAYOUT_SCREENSHOTS");
        string file = Path.Combine(screenshotPath ?? "", "fixtures", index + ".png");
        if (screenshotPath != null && File.Exists(file)) return File.ReadAllBytes(file);
        using var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(35 + index * 12, 75, 120 + index * 8));
        using var font = new Font(UiMetrics.FontFamily, 22, FontStyle.Bold);
        g.DrawString((index + 1).ToString(), font, Brushes.White, 12, 12);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    private static Control Find(Control parent, string name) => parent.Controls.Find(name, true).Single();
    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; }
    }
}
