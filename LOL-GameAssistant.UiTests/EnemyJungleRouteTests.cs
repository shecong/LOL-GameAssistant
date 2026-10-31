using System.Reflection;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;
using LOL_GameAssistant.Infrastructure.Sgp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class EnemyJungleRouteTests
{
    [Fact]
    public void TimelineUsesRealTimestampsAndDoesNotTreatKillerOrAssistAsVictimPosition()
    {
        var json = JObject.Parse("""
        { "metadata": {"match_id":"HN1_42"}, "json": { "frames": [
          {"timestamp":125000,"participantFrames":{"1":{"position":{"x":3900,"y":7900}}},
           "events":[{"type":"CHAMPION_KILL","timestamp":143000,"killerId":1,"victimId":6,
             "assistingParticipantIds":[2],"position":{"x":11000,"y":11000}},
             {"type":"ELITE_MONSTER_KILL","timestamp":180000,"killerId":1,"monsterType":"DRAGON","position":{"x":9900,"y":4500}}]},
          {"timestamp":185000,"participantFrames":{"1":{"position":{"x":4000,"y":6500}},"2":{"position":{"x":-1,"y":20}}}},
          {"timestamp":490000,"participantFrames":{"1":{"position":{"x":8000,"y":3000}}}}
        ]} }
        """);
        var timeline = SgpMatchTimelineService.Parse(json, 42, "HN1");
        Assert.Equal([125, 185], timeline.Players[1].Select(p => p.Seconds));
        Assert.Equal(new RiftPoint(11000, 11000), Assert.Single(timeline.Players[6]).Position);
        Assert.False(timeline.Players.ContainsKey(2));
        Assert.Contains(timeline.Events, e => e.Seconds == 143 && e.Participants.Contains(1));
        Assert.Contains(timeline.Events, e => e.Participants.Contains(2) && e.Description.Contains("助攻"));
        Assert.Contains(timeline.Events, e => e.Seconds == 180 && e.Description.Contains("DRAGON"));
    }

    [Fact]
    public void TimelineRejectsWrongMatchRegionAndMissingFrames()
    {
        Assert.Throws<IOException>(() => SgpMatchTimelineService.Parse(JObject.Parse("""{"metadata":{"match_id":"HN2_42"},"json":{"frames":[{}]}}"""), 42, "HN1"));
        Assert.Throws<IOException>(() => SgpMatchTimelineService.Parse(JObject.Parse("""{"json":{"gameId":99,"frames":[{}]}}"""), 42, "HN1"));
        Assert.Throws<IOException>(() => SgpMatchTimelineService.Parse(JObject.Parse("""{"json":{"frames":[]}}"""), 42, "HN1"));
    }

    [Fact]
    public void PredictionMirrorsSidesPrefersSameChampionAndKeepsLowSampleConfidence()
    {
        JungleRouteSample Sample(long id, int champion, int team) => new(id, champion, team, 1, Points);
        var prediction = JungleRoutePredictor.Predict([Sample(1, 64, 100), Sample(2, 64, 100), Sample(3, 99, 100)], 64, 200)!;
        Assert.True(prediction.SameChampion);
        Assert.Equal(2, prediction.TotalGames);
        Assert.Equal(Points[0].Position.Mirror(), prediction.Points[0].Position);
        Assert.Contains("低", prediction.Confidence);
        Assert.Equal(1, prediction.RepresentativeGameId);
        Assert.Null(JungleRoutePredictor.Predict([], 64, 100));
        Assert.Null(JungleRoutePredictor.Predict([Sample(1, 64, 100) with { Points = [new(60, new(500, 500), "基地")] }], 64, 100));
    }

    [Fact]
    public async Task AnalysisLimitsReadsCachesResultAndInvalidatesForNewGame()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.AnalyzeAsync();
        Assert.NotNull(result.Prediction);
        Assert.Equal("enemy", result.Enemy!.Puuid);
        Assert.Equal(200, result.TeamId);
        Assert.Equal(8, fixture.TimelineReads);
        Assert.Equal(8, result.Samples.Count);
        Assert.Equal((0, 29), fixture.Range);
        Assert.Same(result, await fixture.Service.AnalyzeAsync());
        Assert.Equal(1, fixture.HistoryReads);
        fixture.Session = fixture.CreateSession(2);
        Assert.NotSame(result, await fixture.Service.AnalyzeAsync());
        Assert.Equal(2, fixture.HistoryReads);
        await fixture.Service.AnalyzeAsync(force: true);
        Assert.Equal(3, fixture.HistoryReads);
    }

    [Fact]
    public async Task UnknownRoleNeedsConfirmationAndCannotSelectAlly()
    {
        var fixture = new Fixture { Role = "" };
        fixture.Session = fixture.CreateSession(1);
        var unknown = await fixture.Service.AnalyzeAsync();
        Assert.Null(unknown.Prediction);
        Assert.Equal(0, fixture.HistoryReads);
        Assert.Single(unknown.Enemies);
        Assert.Null((await fixture.Service.AnalyzeAsync("me")).Enemy);
        Assert.NotNull((await fixture.Service.AnalyzeAsync("enemy")).Prediction);
    }

    [Fact]
    public async Task MissingTimelinesProduceInsufficientDataAndNeverFabricateRoute()
    {
        var fixture = new Fixture { Timeline = (_, _) => throw new IOException("unavailable") };
        var result = await fixture.Service.AnalyzeAsync();
        Assert.Null(result.Prediction);
        Assert.Empty(result.Samples);
        Assert.Equal(8, result.UnavailableGames);
        Assert.Contains("数据不足", result.Status);
        fixture.Phase = "Lobby";
        Assert.Null((await fixture.Service.AnalyzeAsync()).Enemy);
        Assert.Equal(8, fixture.TimelineReads);
    }

    [Fact]
    public async Task ExcludesNonJungleCustomRemakesAndUnsupportedMode()
    {
        var fixture = new Fixture();
        fixture.Games[0].Participants[0].Spell1Id = 4;
        fixture.Games[1].QueueId = 450;
        fixture.Games[2].GameType = "CUSTOM_GAME";
        fixture.Games[3].GameDuration = 90;
        fixture.Games.RemoveRange(4, fixture.Games.Count - 4);
        Assert.Null((await fixture.Service.AnalyzeAsync()).Prediction);
        Assert.Equal(0, fixture.TimelineReads);
        fixture.Session = new() { GameMode = "ARAM", QueueId = 450 };
        await fixture.Service.AnalyzeAsync();
        Assert.Equal(1, fixture.HistoryReads);
    }

    [Fact]
    public async Task GameChangeDuringTimelineReadDiscardsOldPrediction()
    {
        var fixture = new Fixture();
        fixture.Timeline = (id, _) => { fixture.Session = fixture.CreateSession(2); return Task.FromResult(Timeline(id)); };
        var result = await fixture.Service.AnalyzeAsync();
        Assert.Null(result.Prediction);
        Assert.Contains("对局已变化", result.Status);
    }

    [Fact]
    public void TurningOffCancelsInflightAnalysisAndClearsMap() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<JungleTimeline>();
        var fixture = new Fixture { Timeline = (_, token) => pending.Task.WaitAsync(token) };
        using var controller = new EnemyJungleRouteController(fixture.Service);
        controller.Apply(true);
        Assert.Equal(0, fixture.TimelineReads);
        _ = controller.RefreshAsync(true);
        Assert.Equal(1, fixture.TimelineReads);
        controller.Apply(false);
        pending.TrySetResult(Timeline(1));
        System.Windows.Forms.Application.DoEvents();
        Assert.Null(controller.Current.Prediction);
        Assert.Contains("未开启", controller.Current.Status);
    });

    [Fact]
    public void RouteWindowRendersPredictionAndHistoricalEvidenceInBothThemes() => MatchListScrollingTests.OnUiThread(() =>
    {
        var fixture = new Fixture();
        using var controller = new EnemyJungleRouteController(fixture.Service);
        controller.Apply(true);
        controller.RefreshAsync(true).GetAwaiter().GetResult();
        Assert.NotNull(controller.Current.Prediction);
        string previous = UiTheme.Mode;
        try
        {
            foreach (string mode in new[] { "Dark", "Light" })
            {
                UiTheme.SetMode(mode);
                using var window = new EnemyJungleRouteForm(controller) { ShowInTaskbar = false, Opacity = 0 };
                window.Show();
                System.Windows.Forms.Application.DoEvents();
                var sample = (AntdUI.Select)All(window).Single(c => c.Name == "jungleSample");
                var events = (AntdUI.Table)All(window).Single(c => c.Name == "jungleRouteEvidence");
                Assert.Equal(9, sample.Items.Count);
                sample.SelectedIndex = 1;
                Assert.Contains(Assert.IsType<EnemyJungleRouteForm.EvidenceRow[]>(events.DataSource), row => row.Evidence.Contains("击杀"));
                string? screenshots = Environment.GetEnvironmentVariable("JUNGLE_ROUTE_SCREENSHOTS");
                if (screenshots != null)
                {
                    Directory.CreateDirectory(screenshots);
                    using var image = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(image, window.ClientRectangle);
                    image.Save(Path.Combine(screenshots, mode + ".png"));
                    sample.SelectedIndex = 0;
                    using var forecast = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(forecast, window.ClientRectangle);
                    forecast.Save(Path.Combine(screenshots, mode + "-Prediction.png"));
                    var map = (JungleRouteMap)All(window).Single(c => c.Name == "jungleRouteMap");
                    map.ZoomAt(map.Project(new(8500, 8500)), 2);
                    using var zoomed = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(zoomed, window.ClientRectangle);
                    zoomed.Save(Path.Combine(screenshots, mode + "-Zoom.png"));
                    map.ResetView();
                }
                controller.Apply(false);
                Assert.Empty(Assert.IsType<EnemyJungleRouteForm.EvidenceRow[]>(events.DataSource));
                window.ClientSize = new Size(900, 640);
                System.Windows.Forms.Application.DoEvents();
                Assert.True(All(window).Single(c => c.Name == "jungleRouteMap").Width >= 450);
                if (screenshots != null)
                {
                    using var empty = new Bitmap(window.Width, window.Height);
                    window.DrawToBitmap(empty, window.ClientRectangle);
                    empty.Save(Path.Combine(screenshots, mode + "-Empty.png"));
                }
                controller.Apply(true);
                controller.RefreshAsync(true).GetAwaiter().GetResult();
            }
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void MapZoomKeepsWorldAnchorAndTerrainCoordinatesAligned() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var map = new JungleRouteMap { Size = new(600, 700) };
        var original = map.MapBounds;
        Assert.Equal(new PointF(original.Left, original.Bottom), map.Project(new(0, 0)));
        Assert.Equal(new PointF(original.Right, original.Top), map.Project(new(15000, 15000)));
        var anchor = new RiftPoint(8500, 8500);
        var screen = map.Project(anchor);
        var neighbor = map.Project(new(9500, 8500));
        map.ZoomAt(screen, 2);
        Assert.InRange(Math.Abs(map.Project(anchor).X - screen.X), 0, .01);
        Assert.InRange(Math.Abs(map.Project(anchor).Y - screen.Y), 0, .01);
        Assert.InRange(Math.Abs(map.Project(new(9500, 8500)).X - screen.X - 2 * (neighbor.X - screen.X)), 0, .01);
        Assert.Equal(original.Width * 2, map.MapBounds.Width);
        map.ZoomAt(screen, 20);
        Assert.Equal(original.Width * 3, map.MapBounds.Width);
        map.ResetView();
        Assert.Equal(original, map.MapBounds);
    });

    [Fact]
    public void SettingRoundTripsAndDefaultsOff()
    {
        Assert.False(new AssistantSettings().EnemyJungleRouteEnabled);
        Assert.Equal("F10", new AssistantSettings().EnemyJungleRouteHotkey);
        var settings = LegacySettingsMapper.ToDomain(LegacySettingsMapper.ToLegacy(new AssistantSettings
            { EnemyJungleRouteEnabled = true, EnemyJungleRouteHotkey = "Ctrl+F10" }));
        Assert.True(settings.EnemyJungleRouteEnabled);
        Assert.Equal("Ctrl+F10", settings.EnemyJungleRouteHotkey);
    }

    [Fact]
    public void ShortcutAnalyzesOnlyOnOpeningAndPublishesMapWithoutOpeningAWindow() => MatchListScrollingTests.OnUiThread(() =>
    {
        var fixture = new Fixture();
        using var bridge = new RecordingGameOverlay();
        bool foreground = true;
        using var controller = new EnemyJungleRouteController(fixture.Service, bridge, () => foreground);
        int openWindows = System.Windows.Forms.Application.OpenForms.Count;
        controller.Apply(true);
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal(0, fixture.HistoryReads);
        Assert.Empty(bridge.Panels);
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.Equal(1, fixture.HistoryReads);
        Assert.True(controller.OverlayVisible);
        Assert.Equal(openWindows, System.Windows.Forms.Application.OpenForms.Count);
        var image = Assert.Single(bridge.Panels).Value.Image!;
        Assert.Equal(new Size(1120, 740), image.Size);
        Assert.NotNull(controller.Current.Prediction);
        string? previews = Environment.GetEnvironmentVariable("JUNGLE_ROUTE_SCREENSHOTS");
        if (previews != null) { Directory.CreateDirectory(previews); image.Save(Path.Combine(previews, "Overlay.png")); }
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.False(controller.OverlayVisible);
        Assert.Empty(bridge.Panels);
        Assert.Equal(1, fixture.HistoryReads);
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.Equal(2, fixture.HistoryReads);
        foreground = false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (controller.OverlayVisible && watch.Elapsed < TimeSpan.FromSeconds(2))
        { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Assert.False(controller.OverlayVisible);
        Assert.Empty(bridge.Panels);
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.Equal(2, fixture.HistoryReads);
        foreground = true;
        controller.Apply(false);
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.Equal(2, fixture.HistoryReads);
    });

    [Fact]
    public void ClosingDuringAnalysisCancelsAndCannotReopenFromLateResults() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<JungleTimeline>();
        CancellationToken requestToken = default;
        var fixture = new Fixture { Timeline = (_, token) => { requestToken = token; return pending.Task.WaitAsync(token); } };
        using var bridge = new RecordingGameOverlay();
        using var controller = new EnemyJungleRouteController(fixture.Service, bridge, () => true);
        controller.Apply(true);
        var analysis = controller.ToggleForGameAsync("F10");
        Assert.False(analysis.IsCompleted);
        controller.ToggleForGameAsync("F10").GetAwaiter().GetResult();
        Assert.True(requestToken.IsCancellationRequested);
        pending.TrySetResult(Timeline(1));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!analysis.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(2))
        { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
        Assert.True(analysis.IsCompletedSuccessfully);
        Assert.False(controller.OverlayVisible);
        Assert.Empty(bridge.Panels);
        Assert.Null(controller.Current.Prediction);
    });

    private static IEnumerable<Control> All(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
    private static readonly IReadOnlyList<JungleObservation> Points =
    [new(120, new(3900, 7900), "历史位置采样"), new(180, new(3800, 6500), "历史位置采样"),
        new(240, new(6500, 5500), "历史位置采样"), new(300, new(7800, 4100), "历史位置采样"),
        new(360, new(8400, 2700), "历史位置采样"), new(420, new(9900, 4500), "历史位置采样")];
    private static JungleTimeline Timeline(long id) => new(id, new Dictionary<int, IReadOnlyList<JungleObservation>> { [1] = Points })
        { Events = [new(260, [1], new(6500, 5500), "历史参与击杀事件", "KILL")] };

    private sealed class Fixture
    {
        public string Phase = "InProgress", Role = "JUNGLE";
        public ActiveGameSnapshot Session;
        public int HistoryReads, TimelineReads;
        public (int, int) Range;
        public Func<long, CancellationToken, Task<JungleTimeline>> Timeline = (id, _) => Task.FromResult(EnemyJungleRouteTests.Timeline(id));
        public List<MatchHistoryGame> Games = Enumerable.Range(1, 10).Select(i => new MatchHistoryGame
        {
            GameId = i, GameCreation = 1000000 - i * 1000, GameDuration = 1200, GameMode = "CLASSIC", QueueId = 420, GameType = "MATCHED_GAME",
            Participants = [new() { participantId = 1, championId = 64, teamId = 100, Spell1Id = 11 }],
            ParticipantIdentities = [new() { ParticipantId = 1, Player = new() { Puuid = "enemy" } }]
        }).ToList();
        public EnemyJungleRouteService Service { get; }
        public Fixture()
        {
            Session = CreateSession(1);
            Service = new(ServiceProxy<ILobbyService>((method, _) => method.Name == "GetGameFlowPhaseAsync" ? Task.FromResult<string?>(Phase) : Task.FromResult<ActiveGameSnapshot?>(Session)),
                ServiceProxy<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(new("me", "我", "1", 0, 1, 0, 0))),
                ServiceProxy<IMatchHistoryService>((_, args) => { HistoryReads++; Range = ((int)args[1]!, (int)args[2]!); return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = Games } }); }),
                ServiceProxy<IMatchTimelineService>((_, args) => { TimelineReads++; return Timeline((long)args[0]!, (CancellationToken)args[1]!); }));
        }
        public ActiveGameSnapshot CreateSession(long id) => new() { GameId = id, GameMode = "CLASSIC", QueueId = 420,
            TeamOne = [new() { Puuid = "me" }], TeamTwo = [new() { Puuid = "enemy", SummonerName = "敌方打野（测试样本）", ChampionId = 64, Position = Role }] };
    }
    private static T ServiceProxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
