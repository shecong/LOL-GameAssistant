using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class MatchHonorsUiTests
{
    private static IGameAssetService Assets => Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null));
    private sealed class Champions : IChampionCatalog
    {
        public string GetDisplayName(int id) => id == 1 ? "九尾妖狐" : "寒冰射手";
        public IReadOnlyList<ChampionReference> GetAll() => [];
        public int? FindIdByDisplayName(string? name) => null;
    }
    private static MatchHistoryGame Game() => new()
    {
        HasFullParticipants = true, GameId = 1200123, GameDuration = 1800, QueueId = 420, GameMode = "CLASSIC",
        GameCreation = 1792857600000,
        Participants = Enumerable.Range(1, 10).Select(id => new MatchParticipant { participantId = id, championId = id,
            teamId = id <= 5 ? 100 : 200, stats = new() { Win = id <= 5, kills = id % 5 == 1 ? 15 : 2,
                deaths = 3, assists = 7, largestKillingSpree = id == 1 ? 8 : 0, pentaKills = id == 1 ? 1 : 0,
                quadraKills = id == 6 ? 1 : 0, HasDamageResult = true, totalDamageDealtToChampions = id % 5 == 1 ? 40000 : 12000 } }).ToList(),
        ParticipantIdentities = Enumerable.Range(1, 10).Select(id => new MatchIdentity { ParticipantId = id,
            Player = new() { Puuid = "honor-player-" + id, GameName = "玩家" + id } }).ToList()
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryAndLiveHistoryShowSameHonorsWithoutDetailRequests(bool dark) => MatchListScrollingTests.OnUiThread(() =>
    {
        string mode = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(dark ? "Dark" : "Light");
            int calls = 0;
            var service = Service<IMatchHistoryService>((_, _) => { calls++; throw new InvalidOperationException("No details during binding"); });
            var game = Game();
            var queryRow = new MatchHistoryListRow(game, "honor-player-1", Assets, new Champions()) { Width = 1100 };
            var liveRow = new RecentMatchRow(Assets, service) { Width = 600 };
            liveRow.UseWorkspaceLayout();
            Pump(liveRow.SetSummaryAsync(game, "honor-player-1"));
            Assert.Equal("胜方 MVP · 超神 · 五杀", liveRow.Honors.Text);
            Assert.Equal(queryRow.Honors.Text, liveRow.Honors.Text);
            Assert.Contains("五杀", queryRow.AccessibleName);
            Assert.Equal(0, calls);
            using var host = new Form { ClientSize = new Size(1120, 240), ShowInTaskbar = false };
            queryRow.Location = new Point(10, 15); liveRow.Location = new Point(10, 90);
            host.Controls.Add(queryRow); host.Controls.Add(liveRow); host.Show(); UiTheme.Apply(host);
            var badges = liveRow.Controls.OfType<MatchHonorBadges>().Single();
            Assert.True(badges.Bottom <= liveRow.ClientSize.Height);
            Assert.All(liveRow.Controls.Cast<Control>().Where(c => c.Visible && c != badges), c => Assert.True(c.Bottom <= badges.Top));
            Save(host, dark ? "honors-dark" : "honors-light");
        }
        finally { UiTheme.SetMode(mode); }
    });

    [Fact]
    public void IncompleteSummaryGainsMvpOnlyAfterItsDetailIsOpened() => MatchListScrollingTests.OnUiThread(() =>
    {
        var full = Game(); var detail = full.ToSummaryDetail();
        var summary = Game(); summary.HasFullParticipants = false;
        summary.Participants = [summary.Participants[0]]; summary.ParticipantIdentities = [summary.ParticipantIdentities[0]];
        int calls = 0;
        var service = Service<IMatchHistoryService>((_, _) => { calls++; return Task.FromResult<MatchDetail?>(detail); });
        using var liveRow = new RecentMatchRow(Assets, service);
        Pump(liveRow.SetSummaryAsync(summary, "honor-player-1"));
        Assert.Null(liveRow.Honors.Mvp); Assert.Contains("超神", liveRow.Honors.Feats); Assert.Equal(0, calls);
        Pump(liveRow.LoadDetailAsync());
        Assert.Equal("胜方 MVP", liveRow.Honors.Mvp); Assert.Equal(1, calls);
        using var queryRow = new MatchHistoryListRow(summary, "honor-player-1", Assets, new Champions());
        Assert.Null(queryRow.Honors.Mvp);
        queryRow.ApplyDetailHonors(detail, "honor-player-1");
        Assert.Equal("胜方 MVP", queryRow.Honors.Mvp);
    });

    [Fact]
    public void DetailScoreboardLabelsBothMvpsAndIndividualFeats() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var pane = new MatchHistoryDetailPane(Assets, new Champions()) { Dock = DockStyle.Fill };
        using var host = new Form { ClientSize = new Size(700, 760), ShowInTaskbar = false };
        host.Controls.Add(pane); host.Show();
        pane.SetDetail(Game().ToSummaryDetail(), "honor-player-1");
        var grids = Children(pane).OfType<DataGridView>().ToArray();
        Assert.Equal(2, grids.Length);
        var awards = grids.SelectMany(grid => grid.Rows.Cast<DataGridViewRow>()).Select(row => row.Cells[4].Value?.ToString()).ToArray();
        Assert.Single(awards, text => text?.Contains("胜方 MVP") == true);
        Assert.Single(awards, text => text?.Contains("败方 MVP") == true);
        Assert.Contains(awards, text => text?.Contains("四杀") == true);
        Assert.Contains(awards, text => text?.Contains("超神") == true && text.Contains("五杀"));
        Save(host, "honors-detail");
    });

    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));

    [Fact]
    public void ClassicCardRowsLoadHonorsAutomaticallyAndShareSameMatchRequest() => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode("Dark");
            var full = Game();
            var pending = new TaskCompletionSource<MatchDetail?>();
            int calls = 0;
            var source = Service<IMatchHistoryService>((_, _) => { calls++; return pending.Task; });
            using var host = new Form { ClientSize = new Size(370, 320), ShowInTaskbar = false };
            RecentMatchRow Create(int id, int y)
            {
                var partial = Game(); partial.HasFullParticipants = false;
                partial.Participants = [partial.Participants[id - 1]];
                partial.ParticipantIdentities = [partial.ParticipantIdentities[id - 1]];
                var row = new RecentMatchRow(Assets, source) { Width = 350, Location = new Point(10, y) };
                row.UseReferenceLayout();
                Pump(row.SetSummaryAsync(partial, "honor-player-" + id));
                row.EnableAutomaticHonors(); host.Controls.Add(row);
                return row;
            }
            var winner = Create(1, 10); var loser = Create(6, 90);
            Assert.Equal(0, calls);
            host.Show();
            PumpUntil(() => calls == 1);
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(1, calls);
            pending.SetResult(full.ToSummaryDetail());
            PumpUntil(() => winner.Honors.Mvp != null && loser.Honors.Mvp != null);
            Assert.Equal("胜方 MVP · 超神 · 五杀", winner.Honors.Text);
            Assert.Equal("败方 MVP · 四杀", loser.Honors.Text);
            Assert.Equal(1, calls);
            var ordinary = new RecentMatchRow(Assets, source) { Width = 350, Location = new Point(10, 170) };
            ordinary.UseReferenceLayout();
            Pump(ordinary.SetSummaryAsync(full, "honor-player-2")); host.Controls.Add(ordinary);
            Assert.Equal("本局暂无荣誉", ordinary.Controls.OfType<MatchHonorBadges>().Single().StatusText);
            Save(host, "honors-classic-every-row");
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void ClosingCardsCancelsQueuedHonorWorkAndLimitsBackgroundConcurrency() => MatchListScrollingTests.OnUiThread(() =>
    {
        int calls = 0;
        var pending = new TaskCompletionSource<MatchDetail?>();
        var source = Service<IMatchHistoryService>((_, _) => { calls++; return pending.Task; });
        using var host = new Form { ClientSize = new Size(400, 700), ShowInTaskbar = false };
        for (int i = 0; i < 8; i++)
        {
            var game = Game(); game.GameId += i; game.HasFullParticipants = false;
            game.Participants = [game.Participants[0]]; game.ParticipantIdentities = [game.ParticipantIdentities[0]];
            var row = new RecentMatchRow(Assets, source) { Width = 350, Top = i * 75 };
            row.UseReferenceLayout(); Pump(row.SetSummaryAsync(game, "honor-player-1"));
            row.EnableAutomaticHonors(); host.Controls.Add(row);
        }
        host.Show(); PumpUntil(() => calls == 2);
        System.Windows.Forms.Application.DoEvents(); Assert.Equal(2, calls);
        host.Close(); System.Windows.Forms.Application.DoEvents();
        pending.SetResult(null); System.Windows.Forms.Application.DoEvents();
        Assert.Equal(2, calls);
    });

    [Fact]
    public void FullDetailCardsShowBothMvps() => MatchListScrollingTests.OnUiThread(() =>
    {
        var history = Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() }));
        var premades = Service<IPremadeDetectionService>((_, _) => Task.FromResult(new PremadeDetectionResult([])));
        using var form = new MatchDetailForm(Game().ToSummaryDetail(), "honor-player-1", Assets, history, premades);
        form.Show();
        PumpUntil(() => Children(form).OfType<MatchHonorBadges>().Count() == 2);
        Assert.Contains(Children(form).OfType<MatchHonorBadges>(), badge => badge.Honors.Text == "胜方 MVP · 超神 · 五杀");
        Assert.Contains(Children(form).OfType<MatchHonorBadges>(), badge => badge.Honors.Text == "败方 MVP · 四杀");
        Save(form, "honors-full-detail");
    });

    [Fact]
    public async Task OldDetailCacheIsRefreshedForHonorFields()
    {
        string root = Path.Combine(Path.GetTempPath(), "honors-cache-" + Guid.NewGuid());
        try
        {
            var old = Game().ToSummaryDetail(); old.HasFullParticipants = false; old.participants[0].stats!.largestKillingSpree = 0;
            var oldCache = new LocalDataCache<MatchDetail>(Path.Combine(root, "player-data", "details"), TimeSpan.FromDays(30), _ => true);
            await oldCache.GetAsync("v3:honors-test:1200123", () => Task.FromResult<MatchDetail?>(old));
            int calls = 0;
            var inner = Service<IMatchHistoryService>((_, _) => { calls++; return Task.FromResult<MatchDetail?>(Game().ToSummaryDetail()); });
            var cache = new CachedMatchHistoryService(inner, root, _ => Task.FromResult("honors-test"));
            var detail = await cache.GetDetailAsync(1200123);
            Assert.True(detail!.HasFullParticipants);
            Assert.Equal("胜方 MVP · 超神 · 五杀", MatchHonorCalculator.ForPlayer(detail, detail.participants[0]).Text);
            await cache.GetDetailAsync(1200123);
            Assert.Equal(1, calls);
        }
        finally
        {
            string full = Path.GetFullPath(root);
            if (Directory.Exists(full) && full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(full, recursive: true);
        }
    }

    private static void Save(Form form, string name)
    {
        if (Environment.GetEnvironmentVariable("HONOR_PREVIEW_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(folder, name + ".png"));
    }
}
