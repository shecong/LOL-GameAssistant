using System.Reflection;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ChampSelectCompanionTests
{
    [Fact]
    public void BenchEventsRenderWhileModeAndPlayerRequestsRemainPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(777));
        Assert.Equal([777], BenchIds(fixture.Form));
        Assert.False(fixture.Lobby.Task.IsCompleted);
        Assert.False(fixture.Player.Task.IsCompleted);

        fixture.Form.ObserveChampionSelection(Snapshot(22, 99));
        Assert.Equal([22, 99], BenchIds(fixture.Form));
        fixture.Form.ObserveChampionSelection(Snapshot());
        Assert.Empty(BenchIds(fixture.Form));
    });

    [Fact]
    public void OlderHttpResponseDoesNotOverwriteNewerBenchEvent() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(777));
        fixture.Selection.SetResult(Snapshot(22));
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal([777], BenchIds(fixture.Form));
        fixture.Form.StopTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(99));
        Assert.Empty(BenchIds(fixture.Form));
    });

    private static ChampionSelectionSnapshot Snapshot(params int[] ids) => new()
    {
        BenchChampionIds = ids,
        LocalPlayerCellId = 0,
        MyTeam = [new ChampionSelectionMember { CellId = 1, Puuid = "ally" }]
    };

    private static int[] BenchIds(Control control) => Children(control)
        .OfType<Button>().Where(button => button.Tag is int).Select(button => (int)button.Tag!).ToArray();

    private static IEnumerable<Control> Children(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));

    private sealed class Fixture : IDisposable
    {
        public TaskCompletionSource<ChampionSelectionSnapshot?> Selection { get; } = new();
        public TaskCompletionSource<LobbySnapshot?> Lobby { get; } = new();
        public TaskCompletionSource<ActiveGameSnapshot?> Game { get; } = new();
        public TaskCompletionSource<PlayerProfile?> Player { get; } = new();
        public ChampSelectCompanionForm Form { get; }

        public Fixture()
        {
            Form = new ChampSelectCompanionForm(
                Service<IChampionSelectService>((_, args) => Selection.Task.WaitAsync(Token(args))),
                Service<ILobbyService>((method, args) => method.Name == "GetLobbyAsync"
                    ? Lobby.Task.WaitAsync(Token(args)) : Game.Task.WaitAsync(Token(args))),
                Service<IClientFeatureService>((_, _) => null),
                Service<IChampionCatalog>((_, args) => $"英雄 {args[0]}"),
                Service<IPlayerProfileService>((_, _) => Player.Task),
                Service<IRankedStatsService>((_, _) => null),
                Service<IMatchHistoryService>((_, _) => null),
                Service<IApplicationSettingsStore>((_, _) => new AssistantSettings()));
        }

        public void Dispose()
        {
            Form.Dispose();
            Player.TrySetResult(null);
            System.Windows.Forms.Application.DoEvents();
        }

        private static CancellationToken Token(object?[] args) => args.OfType<CancellationToken>().FirstOrDefault();
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ServiceProxy>();
        ((ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
