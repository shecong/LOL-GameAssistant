using System.Reflection;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class PlayerNameSearchTests
{
    private static PlayerProfile Profile(string id, string tag = "111") => new(id, "同名玩家", tag, 1, 88, 0, 0);
    private static MatchHistoryGame Game(string platform, params (string Id, string Tag)[] players) => new()
    {
        GameId = 42, PlatformId = platform,
        ParticipantIdentities = players.Select(p => new MatchIdentity { Player = new() { Puuid = p.Id, GameName = "同名玩家", TagLine = p.Tag } }).ToList()
    };
    private static IPlayerProfileService Profiles(PlayerProfile? found = null) => Service<IPlayerProfileService>((method, _) =>
        Task.FromResult(method.Name is "FindByNameAsync" or "FindByRiotIdAsync" ? found : new PlayerProfile("self", "当前玩家", "CN", 0, 1, 0, 0)));
    private static IMatchHistoryService History(params MatchHistoryGame[] games) => Service<IMatchHistoryService>((_, _) =>
        Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = games.ToList() } }));

    [Fact]
    public async Task AllVisibleHomonymsAreMergedAndDeduplicatedWithoutStoppingAtApiFirstResult()
    {
        var result = await new PlayerNameSearchService(Profiles(Profile("api")), History(Game("HN1", ("api", "111"), ("recent", "222"))))
            .SearchAsync("同名玩家", "", [new() { Puuid = "favorite", GameName = "同名玩家", TagLine = "333" }],
                [Game("HN1", ("loaded", "444"), ("api", "111"))]);
        Assert.Equal(4, result.Candidates.Count); Assert.False(result.IsComplete);
        Assert.Equal(new[] { "111", "222", "333", "444" }, result.Candidates.Select(c => c.TagLine));
        Assert.Contains("已加载战绩", result.Candidates[0].Source); Assert.Contains("名称接口", result.Candidates[0].Source);
        Assert.Contains("不是全区同名名单", result.ScopeMessage);
    }

    [Fact]
    public async Task SingleNameResultDoesNotClaimUniqueAndExactRiotIdDoes()
    {
        var service = new PlayerNameSearchService(Profiles(Profile("api")), History());
        var bare = await service.SearchAsync("同名玩家", "", [], []);
        Assert.Single(bare.Candidates); Assert.False(bare.IsComplete);
        var exact = await service.SearchAsync("同名玩家#111", "", [], []);
        Assert.Single(exact.Candidates); Assert.True(exact.IsComplete);
    }

    [Fact]
    public async Task WrongTagFromLegacyNameFallbackCannotBeAcceptedAsExactRiotId()
    {
        var result = await new PlayerNameSearchService(Profiles(Profile("wrong", "other")), History())
            .SearchAsync("同名玩家#111", "", [], []);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task CachedExactIdentityStillRequiresConfirmation()
    {
        var result = await new PlayerNameSearchService(Profiles(Profile("old") with { IsCached = true }), History())
            .SearchAsync("同名玩家#111", "", [], []);
        Assert.Single(result.Candidates); Assert.False(result.IsComplete); Assert.Contains("名称缓存", result.Candidates[0].Source);
    }

    [Fact]
    public async Task CrossRegionCandidatesNeverIncludeAnotherRealmsFavoritesOrGames()
    {
        var profiles = Service<IPlayerProfileService>((method, _) =>
        {
            Assert.Equal("FindByNameAsync", method.Name);
            return Task.FromException<PlayerProfile?>(new NotSupportedException());
        });
        var history = Service<IMatchHistoryService>((_, _) => throw new InvalidOperationException("跨区不扫描本区账号"));
        var result = await new PlayerNameSearchService(profiles, history).SearchAsync("同名玩家", "HN10",
            [new() { Puuid = "wrong", GameName = "同名玩家", TagLine = "111", Platform = "HN1" },
             new() { Puuid = "right", GameName = "同名玩家", TagLine = "222", Platform = "HN10" }],
            [Game("HN1", ("other-region", "333")), Game("HN10", ("loaded", "444"))]);
        Assert.Equal(new[] { "right", "loaded" }, result.Candidates.Select(c => c.Puuid));
        Assert.All(result.Candidates, candidate => Assert.Equal("HN10", candidate.Platform));
        Assert.Contains("暂不支持跨区名称接口", result.ScopeMessage);
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoAnEmptySuccessfulSearch()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var profiles = Service<IPlayerProfileService>((_, _) => Task.FromCanceled<PlayerProfile?>(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlayerNameSearchService(profiles, History())
            .SearchAsync("同名玩家", "", [], [], canceled.Token));
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Click(Control button) => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
    private static PlayerNameSearchResult Choices() => new([new("one", "同名玩家", "111", "HN1", 100, "名称接口"),
        new("two", "同名玩家", "222", "HN1", null, "近期200场")], false, "候选不代表全区名单，请核对后选择。");

    [Theory]
    [InlineData(false, 950)]
    [InlineData(true, 950)]
    [InlineData(true, 660)]
    public void PickerShowsEveryCandidateAndRequiresExplicitSelection(bool dark, int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(dark ? "Dark" : "Light");
            using var picker = new PlayerCandidatePickerForm(Choices()) { Width = width };
            picker.Show(); System.Windows.Forms.Application.DoEvents();
            var grid = Field<DataGridView>(picker, "_players"); var confirm = Field<AntdUI.Button>(picker, "_confirm");
            Assert.Equal(2, grid.Rows.Count); Assert.Empty(grid.SelectedRows.Cast<DataGridViewRow>()); Assert.False(confirm.Enabled);
            Assert.Null(picker.SelectedCandidate);
            Assert.Equal("同名玩家#222", grid.Rows[1].Cells[0].Value); Assert.Equal("未提供", grid.Rows[1].Cells[2].Value);
            using var bitmap = new Bitmap(picker.Width, picker.Height); picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, picker.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"player-candidates-{dark}-{width}.png"));
            grid.Rows[1].Selected = true; Assert.True(confirm.Enabled); Click(confirm);
            Assert.Equal("two", picker.SelectedCandidate?.Puuid); Assert.Equal(DialogResult.OK, picker.DialogResult);
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Fact]
    public void PickerCancelNeverChoosesTheFirstPlayer() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var picker = new PlayerCandidatePickerForm(Choices()); picker.Show(); System.Windows.Forms.Application.DoEvents();
        Click(Field<AntdUI.Button>(picker, "_cancel")); Assert.Null(picker.SelectedCandidate); Assert.Equal(DialogResult.Cancel, picker.DialogResult);
    });

    [Fact]
    public void BattleQueryLoadsOnlyTheChosenPlayerAndCancelDoesNotLoadAnyone() => MatchListScrollingTests.OnUiThread(() =>
    {
        var loaded = new List<string>(); bool cancel = false;
        var profiles = Service<IPlayerProfileService>((method, args) =>
        {
            if (method.Name == "GetByPuuidAsync") { loaded.Add((string)args[0]!); return Task.FromResult<PlayerProfile?>(Profile((string)args[0]!, "222")); }
            return Task.FromResult<PlayerProfile?>(method.Name == "GetCurrentAsync" ? null : Profile("one"));
        });
        using var query = new BattleQueryForm(profiles, Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
            History(), Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)),
            Service<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer> { new() { Puuid = "two", GameName = "同名玩家", TagLine = "222" } }),
            Service<ITextExportService>((_, _) => Task.CompletedTask), pickCandidate: (result, _) =>
            {
                Assert.Equal(2, result.Candidates.Count);
                return Task.FromResult<PlayerSearchCandidate?>(cancel ? null : result.Candidates.Single(c => c.Puuid == "two"));
            });
        Pump(query.PerformSearchAsync("同名玩家")); Assert.Equal(new[] { "two" }, loaded);
        cancel = true; Pump(query.PerformSearchAsync("同名玩家")); Assert.Single(loaded);
        Assert.Equal("已取消玩家选择", Field<AntdUI.Label>(query, "lblStatus").Text);
    });

    [Fact]
    public void LateChoiceFromPreviousSearchCannotOverwriteNewPlayer() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<PlayerSearchCandidate?>(); PlayerSearchCandidate? oldChoice = null;
        var loaded = new List<string>();
        var profiles = Service<IPlayerProfileService>((method, args) =>
        {
            if (method.Name == "GetByPuuidAsync") { loaded.Add((string)args[0]!); return Task.FromResult<PlayerProfile?>(Profile((string)args[0]!)); }
            return Task.FromResult<PlayerProfile?>(method.Name == "GetCurrentAsync" ? null : Profile("old"));
        });
        using var query = new BattleQueryForm(profiles, Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
            History(), Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)),
            Service<IFavoritePlayerStore>((_, _) => new List<FavoritePlayer>()), Service<ITextExportService>((_, _) => Task.CompletedTask),
            pickCandidate: (result, _) => { oldChoice = result.Candidates[0]; return pending.Task; });
        var first = query.PerformSearchAsync("同名玩家"); Assert.False(first.IsCompleted);
        const string newId = "11111111-1111-1111-1111-111111111111";
        Pump(query.PerformSearchAsync(newId)); pending.SetResult(oldChoice); Pump(first);
        Assert.Equal(new[] { newId }, loaded); Assert.Equal(newId, Field<PlayerProfile>(query, "_currentPlayer").Puuid);
    });
}
