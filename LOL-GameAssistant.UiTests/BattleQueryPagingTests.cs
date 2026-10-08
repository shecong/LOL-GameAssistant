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
            Assert.Equal(pageSize, detailRequests);
            Assert.All(list.Controls.Cast<Control>(), control => Assert.IsType<MatchHistoryPreviewCard>(control));
        });

    private static T Proxy<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, BattleOverlayTests.ServiceProxy>();
        ((BattleOverlayTests.ServiceProxy)(object)proxy).Handler = handler ?? ((_, _) => null);
        return proxy;
    }
}
