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
    [Theory]
    [InlineData(450, "")]
    [InlineData(0, "ARAM")]
    [InlineData(2400, "KIWI")]
    public void AramBenchRemainsAvailableAfterModeResolvesAndSupportsSwap(int queueId, string mode) => MatchListScrollingTests.OnUiThread(() =>
    {
        int swappedChampion = 0;
        using var fixture = new Fixture(featureHandler: (_, args) =>
        {
            swappedChampion = (int)args[0]!;
            return Task.FromResult(new ClientFeatureResult(true, "已交换"));
        });
        fixture.Lobby.SetResult(new LobbySnapshot { QueueId = queueId, GameMode = mode });
        fixture.Game.SetResult(null);
        fixture.Selection.SetResult(Snapshot(22, 99));
        fixture.Form.StartTracking();
        System.Windows.Forms.Application.DoEvents();
        Assert.Equal([22, 99], BenchIds(fixture.Form));
        var button = Children(fixture.Form).OfType<Button>().Single(button => button.Tag is 22);
        // PerformClick requires a visible form; invoke the same click event without needing the game client.
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);
        Assert.Equal(22, swappedChampion);
        fixture.Form.ObserveChampionSelection(Snapshot(99));
        Assert.Equal([99], BenchIds(fixture.Form));
    });

    [Fact]
    public void ClassicModeDoesNotShowBenchChoices() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture();
        fixture.Lobby.SetResult(new LobbySnapshot { QueueId = 430, GameMode = "CLASSIC" });
        fixture.Game.SetResult(null);
        fixture.Form.StartTracking();
        fixture.Form.ObserveChampionSelection(Snapshot(22, 99));
        System.Windows.Forms.Application.DoEvents();
        Assert.Empty(BenchIds(fixture.Form));
    });

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
    public void CompanionHidesOnClientMinimizeAndReturnsOnRestoreWhileDataPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new Form { Size = new Size(900, 600), Opacity = 0, ShowInTaskbar = false };
        client.Show();
        using var fixture = new Fixture(() => ChampSelectCompanionForm.TryGetVisibleClientBounds(client.Handle, out var bounds)
            ? bounds : null);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.SyncClientVisibility());
        Assert.True(fixture.Form.Visible);
        client.WindowState = FormWindowState.Minimized;
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
        client.WindowState = FormWindowState.Normal;
        Assert.True(fixture.Form.SyncClientVisibility());
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Lobby.Task.IsCompleted);
        Assert.False(fixture.Player.Task.IsCompleted);
    });

    [Fact]
    public void HiddenClientHidesCompanionAndStoppedTrackingDoesNotReshowIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new Form { Size = new Size(900, 600), Opacity = 0, ShowInTaskbar = false };
        client.Show();
        using var fixture = new Fixture(() => ChampSelectCompanionForm.TryGetVisibleClientBounds(client.Handle, out var bounds)
            ? bounds : null);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.Visible);
        client.Hide();
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
        fixture.Form.StopTracking();
        client.Show();
        Assert.False(fixture.Form.SyncClientVisibility());
        Assert.False(fixture.Form.Visible);
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

    [Fact]
    public void CompanionIsTopMostOnlyWhileClientIsForegroundEvenWhenDataPending() => MatchListScrollingTests.OnUiThread(() =>
    {
        bool focused = false;
        using var fixture = new Fixture(() => new Rectangle(100, 100, 900, 600), clientForeground: () => focused);
        Assert.False(fixture.Form.TopMost);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Form.TopMost);
        focused = true;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.TopMost);
        focused = false;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.Visible);
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Player.Task.IsCompleted);
        focused = true;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.TopMost);
        fixture.Form.StopTracking();
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Form.Visible);
    });

    [Fact]
    public void HiddenClientAndOpenBuildPickerSuppressTopMost() => MatchListScrollingTests.OnUiThread(() =>
    {
        Rectangle? bounds = new Rectangle(100, 100, 900, 600);
        using var fixture = new Fixture(() => bounds, clientForeground: () => true);
        fixture.Form.StartTracking();
        Assert.True(fixture.Form.TopMost);
        var chooseBuild = (Button)typeof(ChampSelectCompanionForm).GetField("_chooseBuild", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Form)!;
        chooseBuild.Enabled = false;
        fixture.Form.SyncClientVisibility();
        Assert.False(fixture.Form.TopMost);
        chooseBuild.Enabled = true;
        fixture.Form.SyncClientVisibility();
        Assert.True(fixture.Form.TopMost);
        bounds = null;
        fixture.Form.SyncClientVisibility();
        Assert.False(fixture.Form.TopMost);
        Assert.False(fixture.Form.Visible);
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

        public Fixture(Func<Rectangle?>? clientBounds = null, Func<MethodInfo, object?[], object?>? featureHandler = null,
            Func<bool>? clientForeground = null)
        {
            Form = new ChampSelectCompanionForm(
                Service<IChampionSelectService>((_, args) => Selection.Task.WaitAsync(Token(args))),
                Service<ILobbyService>((method, args) => method.Name == "GetLobbyAsync"
                    ? Lobby.Task.WaitAsync(Token(args)) : Game.Task.WaitAsync(Token(args))),
                Service<IClientFeatureService>(featureHandler ?? ((_, _) => null)),
                Service<IChampionCatalog>((_, args) => $"英雄 {args[0]}"),
                Service<IPlayerProfileService>((_, _) => Player.Task),
                Service<IRankedStatsService>((_, _) => null),
                Service<IMatchHistoryService>((_, _) => null),
                Service<IApplicationSettingsStore>((_, _) => new AssistantSettings()), clientBounds, clientForeground);
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
