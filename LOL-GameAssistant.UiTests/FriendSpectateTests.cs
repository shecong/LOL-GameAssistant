using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.Friends;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class FriendSpectateTests
{
    [Theory]
    [InlineData("chat", "inGame", null)]
    [InlineData("dnd", "inGame", "0")]
    [InlineData("away", " INGAME ", "42")]
    [InlineData("inGame", null, null)]
    public async Task InGameFriendCanSpectateWithoutAdvertisedGameId(string availability, string? gameStatus, string? gameId)
    {
        int posts = 0;
        var sender = Service<ILcuRequestSender>((method, args) =>
        {
            if (method.Name == "GetStringAsync")
            {
                Assert.Equal("/lol-chat/v1/friends", args[0]);
                return Task.FromResult<string?>(FriendJson(availability, gameStatus, gameId));
            }
            Assert.Equal("PostAsync", method.Name);
            Assert.Equal("/lol-spectator/v3/buddy/spectate", args[0]);
            Assert.Equal(new[] { "friend-puuid" }, JsonConvert.DeserializeObject<string[]>((string)args[1]!));
            posts++;
            return Task.FromResult(true);
        });

        var friend = Assert.Single(await new LcuFriendDirectoryService(sender).GetFriendsAsync());
        Assert.Equal(FriendPresence.InGame, friend.Presence);
        Assert.True(friend.CanSpectate);
        Assert.True((await new LcuFriendSpectateService(sender).LaunchAsync(new(friend.Puuid!))).Succeeded);
        Assert.Equal(1, posts);
    }

    [Theory]
    [InlineData("chat", "outOfGame", FriendPresence.Online)]
    [InlineData("chat", "inQueue", FriendPresence.Online)]
    [InlineData("dnd", "championSelect", FriendPresence.DoNotDisturb)]
    [InlineData("chat", "spectating", FriendPresence.Spectating)]
    [InlineData("inGame", "outOfGame", FriendPresence.Online)]
    [InlineData("inGameOther", null, FriendPresence.Online)]
    [InlineData("offline", "inGame", FriendPresence.Offline)]
    [InlineData("invisible", "inGame", FriendPresence.Offline)]
    public async Task OtherStatesCannotSpectateEvenWithStaleGameId(string availability, string? gameStatus, FriendPresence expected)
    {
        var sender = Service<ILcuRequestSender>((_, _) =>
            Task.FromResult<string?>(FriendJson(availability, gameStatus, "42")));
        var friend = Assert.Single(await new LcuFriendDirectoryService(sender).GetFriendsAsync());
        Assert.Equal(expected, friend.Presence);
        Assert.False(friend.CanSpectate);
    }

    [Fact]
    public async Task MissingIdentityAndCancellationNeverSendRequests()
    {
        var sender = Service<ILcuRequestSender>((_, _) => throw new InvalidOperationException("Should not send"));
        var service = new LcuFriendSpectateService(sender);
        Assert.False(new FriendProfile(" ", "好友", FriendPresence.InGame, null, 0, null, 42).CanSpectate);
        Assert.False((await service.LaunchAsync(new(" ", 42))).Succeeded);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync(new("target"), cancellation.Token));
    }

    [Fact]
    public async Task ClientRejectionDoesNotReportSuccess()
    {
        var sender = Service<ILcuRequestSender>((method, _) =>
        {
            Assert.Equal("PostAsync", method.Name);
            return Task.FromResult(false);
        });
        Assert.False((await new LcuFriendSpectateService(sender).LaunchAsync(new("friend-puuid"))).Succeeded);
    }

    private static string FriendJson(string availability, string? gameStatus, string? gameId) => new JArray(new JObject
    {
        ["puuid"] = "friend-puuid", ["gameName"] = "好友", ["availability"] = availability,
        ["lol"] = new JObject { ["gameStatus"] = gameStatus, ["gameId"] = gameId }
    }).ToString();
}
