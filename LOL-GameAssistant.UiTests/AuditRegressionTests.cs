using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Coaching;
using LOL_GameAssistant.LoLApi;
using System.Reflection;
using System.Net;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class AuditRegressionTests
{
    [Fact]
    public async Task ConnectionMonitorRetriesDiscoveryAfterFailureAndDisconnect()
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<bool>? changed = null;
        int attempts = 0, errors = 0;
        var stream = Service<ILeagueClientEventStream>((method, args) =>
        {
            if (method.Name == "add_ConnectionChanged") { changed += (Action<bool>)args[0]!; return null; }
            if (method.Name == "remove_ConnectionChanged") { changed -= (Action<bool>)args[0]!; return null; }
            if (method.Name != "ConnectAsync") return null;
            Assert.True((bool)args[0]!);
            int attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1) return Task.FromException<bool>(new HttpRequestException("discovery failed"));
            changed?.Invoke(true);
            if (attempt == 2) connected.TrySetResult();
            else reconnected.TrySetResult();
            return Task.FromResult(true);
        });
        await using var monitor = new LeagueClientConnectionMonitor(stream, _ => Interlocked.Increment(ref errors), TimeSpan.FromMilliseconds(10));
        monitor.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        changed!.Invoke(false);
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync();
        Assert.Equal(3, attempts);
        Assert.Equal(1, errors);
        Assert.Null(changed);
    }

    [Theory]
    [InlineData(LcuHttpFailure.NoCredentials, null)]
    [InlineData(LcuHttpFailure.Timeout, null)]
    [InlineData(LcuHttpFailure.Network, null)]
    [InlineData(LcuHttpFailure.HttpStatus, HttpStatusCode.BadRequest)]
    [InlineData(LcuHttpFailure.None, HttpStatusCode.Unauthorized)]
    public void FailedLobbyCommandCannotBeReportedAsAccepted(LcuHttpFailure failure, HttpStatusCode? status)
    {
        using var content = new MemoryStream([1]);
        Assert.Throws<HttpRequestException>(() => Game_Api.EnsureLobbyCommandSucceeded(new(content, status, failure)));
        Assert.False(content.CanRead);
    }

    [Fact]
    public void EmptySuccessfulLobbyResponseIsAccepted() =>
        Game_Api.EnsureLobbyCommandSucceeded(new(null, HttpStatusCode.NoContent, LcuHttpFailure.None));

    [Fact]
    public async Task CancelledLobbyOperationsStopBeforeCredentialDiscovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lobby = new LegacyLobbyService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lobby.StartMatchmakingAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lobby.AcceptReadyCheckAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lobby.GetLobbyAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lobby.GetGameFlowPhaseAsync(cancellation.Token));
    }

    [Fact]
    public async Task AiContextFollowsAccountSwitchAndRejectsMissingIdentity()
    {
        string? account = "first";
        var snapshot = new ActiveGameSnapshot
        {
            TeamOne = [new GameTeamMember { Puuid = "first", ChampionId = 1 }],
            TeamTwo = [new GameTeamMember { Puuid = "second", ChampionId = 2 }]
        };
        var service = new AiGameContextService(
            Service<ILobbyService>((method, _) => method.Name == "GetGameFlowPhaseAsync"
                ? Task.FromResult<string?>("InProgress") : Task.FromResult<ActiveGameSnapshot?>(snapshot)),
            Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(account == null ? null : new(account, "", "", 0, 0, 0, 0))),
            Service<IChampionSelectService>((_, _) => null),
            Service<ILiveClientGameStateService>((_, _) => Task.FromResult<LiveClientGameSnapshot?>(new(100, [], 100, "CLASSIC"))),
            Service<ILaneKnowledgeService>((_, _) => ""),
            Service<IChampionCatalog>((_, args) => args[0]!.ToString()));
        var first = await service.CollectAsync();
        Assert.Equal(1, first.MyChampionId);
        Assert.Equal(["2"], first.EnemyChampions);
        account = "second";
        var second = await service.CollectAsync();
        Assert.Equal(2, second.MyChampionId);
        Assert.Equal(["1"], second.EnemyChampions);
        account = null;
        var missing = await service.CollectAsync();
        Assert.False(missing.HasLiveClientData);
        Assert.Empty(missing.AlliedChampions);
        Assert.Empty(missing.EnemyChampions);
    }

    [Fact]
    public async Task UnrelatedSettingsDoNotRequestAiAndManualRefreshBypassesInterval()
    {
        int requests = 0;
        var context = new AiGameContext { Phase = "ChampSelect", MyChampionId = 1 };
        var coaching = Service<IAiCoachingService>((_, _) => Task.FromResult(context));
        var provider = Service<IAiRecommendationProvider>((_, _) =>
        {
            requests++;
            return Task.FromResult(new AiRecommendationResult(true, "", "建议", context, null));
        });
        using var coordinator = new RecommendationCoordinator(coaching, provider);
        int publications = 0;
        coordinator.StateChanged += _ => publications++;
        var settings = new AssistantSettings { Ai = new CloudAiSettings
            { Model = "test", EncryptedApiKey = "test", DynamicRefreshEnabled = false } };
        coordinator.Start(settings);
        Assert.Equal(1, requests);
        int initialPublications = publications;
        settings.ThemeMode = "Dark";
        settings.AutoAccept = true;
        settings.Ai.RecommendationOverlayEnabled = true;
        coordinator.UpdateSettings(settings);
        Assert.Equal(1, requests);
        Assert.Equal(initialPublications + 1, publications);
        settings.Ai.DynamicRefreshSeconds = 60;
        coordinator.UpdateSettings(settings);
        Assert.Equal(1, requests);
        await coordinator.RefreshAsync();
        Assert.Equal(1, requests);
        await coordinator.RefreshAsync(force: true);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ConnectionMonitorStartsOnceCancelsPendingConnectionAndUnsubscribes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int connections = 0, subscriptions = 0;
        var stream = Service<ILeagueClientEventStream>(asyncHandler);
        object? asyncHandler(MethodInfo method, object?[] args)
        {
            if (method.Name == "add_ConnectionChanged") { subscriptions++; return null; }
            if (method.Name == "remove_ConnectionChanged") { subscriptions--; return null; }
            if (method.Name == "ConnectAsync")
            {
                Assert.True((bool)args[0]!);
                return Connect((CancellationToken)args[1]!);
            }
            return null;
        }
        async Task<bool> Connect(CancellationToken token)
        {
            Interlocked.Increment(ref connections);
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return false;
        }
        var monitor = new LeagueClientConnectionMonitor(stream, _ => { });
        for (int i = 0; i < 20; i++) monitor.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync();
        monitor.Start();
        Assert.Equal(1, connections);
        Assert.Equal(0, subscriptions);
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    [Fact]
    public void LockfileUsesPortAndPasswordRatherThanPid()
    {
        Assert.True(LcuLockfileParser.TryParse("LeagueClient:12345:54321:test-password:https\n", out var port, out var token));
        Assert.Equal("54321", port);
        Assert.Equal("test-password", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("LeagueClient:12345:54321:https")]
    [InlineData("LeagueClient:0:54321:test:https")]
    [InlineData("LeagueClient:123:65536:test:https")]
    [InlineData("LeagueClient:123:0:test:https")]
    [InlineData("LeagueClient:123:54321: :https")]
    [InlineData("LeagueClient:123:54321:test:http")]
    public void InvalidLockfileCannotSupplyCredentials(string? content)
    {
        Assert.False(LcuLockfileParser.TryParse(content, out var port, out var token));
        Assert.Null(port);
        Assert.Null(token);
    }

    [Fact]
    public void DisposingOnePreviewDoesNotInvalidateAnotherOrCachedBytes()
    {
        using var original = new Bitmap(2, 2);
        original.SetPixel(0, 0, Color.Red);
        using var buffer = new MemoryStream();
        original.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
        byte[] bytes = buffer.ToArray();
        var first = SettingForm.DecodePreviewImage(bytes)!;
        using var second = (Bitmap)SettingForm.DecodePreviewImage(bytes)!;
        first.Dispose();
        Assert.Equal(Color.Red.ToArgb(), second.GetPixel(0, 0).ToArgb());
        using var third = (Bitmap)SettingForm.DecodePreviewImage(bytes)!;
        Assert.Equal(Color.Red.ToArgb(), third.GetPixel(0, 0).ToArgb());
    }
}
