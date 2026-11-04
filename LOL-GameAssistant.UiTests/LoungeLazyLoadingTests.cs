using System.Reflection;
using LOL.Lounge;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.Lounge;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LoungeLazyLoadingTests
{
    private static LoungeClient Client() => new(new LoungeLcuReader(
        SecondPhaseRegressionTests.Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
    private static LoungeMessage Message(long id, DateTimeOffset at) => new(id, "", "sender", "player", "region", "chat", "message", at);

    [Fact]
    public async Task StartupDoesNotSynchronizeOrAcceptLiveMessages()
    {
        using var client = Client();
        Assert.False(client.MessagesActive);
        await using var disconnected = new HubConnectionBuilder().WithUrl("http://127.0.0.1:1/hubs/lounge").Build();
        var sync = (Task)typeof(LoungeClient).GetMethod("SyncAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [disconnected, CancellationToken.None])!;
        await sync; // Must not invoke any hub method while the lounge is unopened.
        typeof(LoungeClient).GetMethod("ReceiveLiveMessage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [Message(1, DateTimeOffset.Now)]);
        Assert.Empty(client.Messages); Assert.Equal(0, client.UnreadCount);
    }

    [Fact]
    public void FirstOpenShowsOnlyTodayAndClosingRejectsLateInitialResponse()
    {
        using var client = Client();
        var day = LoungeRules.StartOfDay(DateTimeOffset.Now);
        var snapshot = new LoungeSnapshot([Message(1, day.AddTicks(-1)), Message(2, day), Message(3, day.AddDays(1))], [],
            ProtocolVersion: LoungeRules.ProtocolVersion, OlderBefore: 2, HasOlder: true);
        client.SetMessagesActive(true); long epoch = client.MessageEpoch;
        client.ApplyTodaySnapshot(snapshot, day, epoch);
        Assert.Equal(2, Assert.Single(client.Messages).Id);
        Assert.Equal(2, client.HistoryBefore); Assert.True(client.InitialHasOlder);
        client.SetMessagesActive(false);
        client.ApplyTodaySnapshot(snapshot, day, epoch);
        Assert.Empty(client.Messages); Assert.Null(client.HistoryBefore);
        client.SetMessagesActive(true);
        Assert.True(client.MessageEpoch > epoch);
        client.ApplyTodaySnapshot(snapshot, day, client.MessageEpoch);
        Assert.Equal(2, Assert.Single(client.Messages).Id);
    }

    [Fact]
    public async Task LateCatchupCannotFillMessagesAfterLeavingAndReopening()
    {
        using var client = Client();
        var day = LoungeRules.StartOfDay(DateTimeOffset.Now);
        client.SetMessagesActive(true); long epoch = client.MessageEpoch;
        var response = new TaskCompletionSource<LoungeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = client.SynchronizeAsync((_, _) => response.Task, default, epoch);
        client.SetMessagesActive(false); client.SetMessagesActive(true);
        response.SetResult(new([Message(1, day.AddHours(1))], []));
        await read;
        Assert.Empty(client.Messages);
    }

    [Fact]
    public void EmptyTodayStillProvidesCursorForExplicitOlderHistory()
    {
        using var client = Client(); client.SetMessagesActive(true);
        client.ApplyTodaySnapshot(new([], [], ProtocolVersion: LoungeRules.ProtocolVersion,
            OlderBefore: long.MaxValue, HasOlder: true), LoungeRules.StartOfDay(DateTimeOffset.Now), client.MessageEpoch);
        Assert.Empty(client.Messages);
        Assert.Equal(long.MaxValue, client.HistoryBefore); Assert.True(client.InitialHasOlder);
    }
}
