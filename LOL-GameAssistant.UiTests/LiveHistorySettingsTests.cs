using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class LiveHistorySettingsTests
{
    private static MatchHistoryGame[] History() => Enumerable.Range(1, 30).Select(id => new MatchHistoryGame
    {
        GameId = id, GameCreation = DateTimeOffset.UtcNow.AddHours(-id).ToUnixTimeMilliseconds(),
        QueueId = id % 2 == 0 ? 420 : 450, GameMode = id % 2 == 0 ? "CLASSIC" : "ARAM",
        Participants = [new() { stats = new() { Win = id % 2 == 0, kills = 3, deaths = 2, assists = 5 } }]
    }).ToArray();

    [Fact]
    public void FiltersBeforeTakingTenAndDoesNotMixUnknownOrSparseModes()
    {
        var games = History().Reverse().ToArray();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i),
            LiveHistorySelection.Select(games.Concat(games), LiveHistoryFilter.Latest, 420, "CLASSIC").Select(g => g.GameId));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i * 2),
            LiveHistorySelection.Select(games, LiveHistoryFilter.CurrentMode, 420, "CLASSIC").Select(g => g.GameId));
        Assert.Empty(LiveHistorySelection.Select(games, LiveHistoryFilter.CurrentMode, 0, null));
        Assert.Single(LiveHistorySelection.Select(games.Take(2), LiveHistoryFilter.CurrentMode, 420, "CLASSIC"));
    }

    [Fact]
    public void SettingsSurviveLegacyMappingAndInvalidValuesNormalize()
    {
        var settings = new AssistantSettings { LiveHistoryFilter = LiveHistoryFilter.CurrentMode };
        Assert.Equal(LiveHistoryFilter.CurrentMode, LegacySettingsMapper.ToDomain(LegacySettingsMapper.ToLegacy(settings)).LiveHistoryFilter);
        settings.LiveHistoryFilter = (LiveHistoryFilter)99;
        settings.Normalize();
        Assert.Equal(LiveHistoryFilter.Latest, settings.LiveHistoryFilter);
    }

    [Fact]
    public void ChangingFilterUpdatesLoadedRowsAndWinRateWithoutFetchingAgain() => MatchListScrollingTests.OnUiThread(() =>
    {
        int requests = 0;
        var matches = Service<IMatchHistoryService>((method, _) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            requests++;
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = History().ToList() } });
        });
        using var card = new LivePlayerForm("history-filter-" + Guid.NewGuid(), "玩家", 0, "top", false, true, true, 420, "CLASSIC",
            Service<IPlayerProfileService>((_, _) => Task.FromResult<LOL_GameAssistant.Domain.Players.PlayerProfile?>(null)),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)), matches,
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));
        card.EnsureDataLoadedAsync().GetAwaiter().GetResult();
        int initialRequests = requests;
        Assert.Equal(10, card.RecentSampleSize);
        Assert.Equal(5, card.RecentWins);
        card.SetHistoryFilter(LiveHistoryFilter.CurrentMode);
        Assert.Equal(10, card.RecentWins);
        Assert.All(card.LayoutHistory, game => Assert.Equal(420, game.QueueId));
        Assert.Equal(10, card.Controls.Find("panelMatches", true).Single().Controls.OfType<RecentMatchRow>().Count());
        card.SetHistoryFilter(LiveHistoryFilter.Latest);
        Assert.Equal(5, card.RecentWins);
        Assert.Equal(initialRequests, requests);
    });

    [Fact]
    public void SettingsDialogSavesImmediatelyAndUsesBothThemes() => MatchListScrollingTests.OnUiThread(() =>
    {
        var saved = LiveHistoryFilter.Latest;
        using var dialog = new LiveGameSettingsForm(saved, filter => saved = filter);
        var selector = (AntdUI.Select)dialog.Controls.Find("liveHistoryFilter", true).Single();
        selector.SelectedIndex = 1;
        Assert.Equal(LiveHistoryFilter.CurrentMode, saved);
        string previous = UiTheme.Mode;
        try
        {
            dialog.ShowInTaskbar = false;
            dialog.Show();
            foreach (string theme in new[] { "Light", "Dark" })
            {
                UiTheme.SetMode(theme); UiTheme.Apply(dialog);
                System.Windows.Forms.Application.DoEvents();
                Assert.True(selector.Width > 400);
                string? folder = Environment.GetEnvironmentVariable("LIVE_HISTORY_SCREENSHOTS");
                if (folder == null) continue;
                Directory.CreateDirectory(folder);
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                bitmap.Save(Path.Combine(folder, "HistorySettings-" + theme + ".png"));
            }
        }
        finally { UiTheme.SetMode(previous); }
    });

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
