using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class FriendActivityTests
{
    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("\"invalid\"")]
    [InlineData("-1")]
    [InlineData("9223372036854775807")]
    [InlineData("99999999999999999999999999999")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1.5")]
    public async Task InvalidOptionalNumbersDoNotDiscardAnyFriends(string value)
    {
        var first = new JObject
        {
            ["puuid"] = "empty", ["gameName"] = "空活动好友", ["availability"] = "away",
            ["lol"] = new JObject { ["queueId"] = JToken.Parse(value), ["timeStamp"] = JToken.Parse(value) }
        };
        var result = await Create(new JArray(first, ActiveFriend(), new JObject
        { ["puuid"] = "offline", ["name"] = "离线好友", ["availability"] = "offline" })).GetFriendActivitiesAsync();
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].QueueId); Assert.Null(result[0].StartedAt); Assert.Null(result[0].Elapsed);
        Assert.Equal("away", result[0].Availability);
        Assert.Equal(420, result[1].QueueId); Assert.Equal("inGame", result[1].GameStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), result[1].StartedAt);
        Assert.Equal("离线好友", result[2].DisplayName); Assert.Null(result[2].StartedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupportsNumericAndStringValues(bool asString)
    {
        var friend = ActiveFriend();
        if (asString) { friend["lol"]!["queueId"] = "420"; friend["lol"]!["timeStamp"] = "1700000000000"; }
        var result = Assert.Single(await Create(new JArray(friend)).GetFriendActivitiesAsync());
        Assert.Equal(420, result.QueueId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), result.StartedAt);
    }

    [Fact]
    public async Task MissingAndNullActivityAreAllowed()
    {
        var result = await Create(new JArray(new JObject { ["puuid"] = "one" },
            new JObject { ["puuid"] = "two", ["lol"] = JValue.CreateNull() })).GetFriendActivitiesAsync();
        Assert.Equal(2, result.Count);
        Assert.All(result, friend => { Assert.Equal(0, friend.QueueId); Assert.Null(friend.StartedAt); });
    }

    private static JObject ActiveFriend() => new()
    {
        ["puuid"] = "active", ["gameName"] = "正常好友", ["availability"] = "chat",
        ["lol"] = new JObject { ["queueId"] = 420, ["timeStamp"] = 1700000000000L,
            ["gameStatus"] = "inGame", ["gameQueueType"] = "RANKED_SOLO_5x5" }
    };

    private static LcuClientFeatureService Create(JArray friends) => new(Service<ILcuRequestSender>((method, args) =>
    {
        Assert.Equal("GetStringAsync", method.Name); Assert.Equal("/lol-chat/v1/friends", args[0]);
        return Task.FromResult<string?>(friends.ToString());
    }));
}
