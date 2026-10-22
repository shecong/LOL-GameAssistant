using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using System.Net;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class RewardCollectionTests
{
    [Fact]
    public async Task ScanIncludesDailyBoxAndEveryBattlePassLevelWithoutWriting()
    {
        var lcu = new Sender();
        lcu.Data["/lol-event-hub/v1/events"] = "[{\"eventId\":\"pass\",\"eventInfo\":{\"eventName\":\"测试战令\"}}]";
        lcu.Data[Root + "/progress"] = "{\"level\":2,\"totalLevels\":3}";
        lcu.Data[Root + "/items"] = "[{\"threshold\":1,\"rewardOptions\":[{\"rewardName\":\"精粹\",\"state\":\"Selected\"}]},{\"threshold\":2,\"rewardOptions\":[{\"rewardName\":\"箱子\",\"state\":\"Unlocked\"}]},{\"threshold\":3,\"rewardOptions\":[{\"rewardName\":\"法球\",\"state\":\"Locked\"}]}]";
        lcu.Data[Root + "/unclaimed-rewards"] = "{\"rewardsCount\":1}";
        using var http = new HttpClient(new MallHandler());
        var scan = await new LcuClientFeatureService(lcu, rewardHttpClient: http).ScanRewardsAsync();
        Assert.Equal(2, scan.Rewards.Count);
        Assert.All(scan.Rewards, reward => Assert.False(reward.RequiresSelection));
        string detail = Assert.Single(scan.Sources, s => s.Source == "测试战令").Message;
        Assert.Contains("第 1 级", detail); Assert.Contains("已领取", detail);
        Assert.Contains("第 2 级", detail); Assert.Contains("第 3 级", detail);
        Assert.Contains(scan.Sources, s => s.Source == "好运魄罗");
        Assert.Empty(lcu.Writes);
    }

    [Fact]
    public async Task DailyClaimUsesOnlyFreePaymentAndChecksBusinessResponseAndRemaining()
    {
        var handler = new MallHandler();
        using var http = new HttpClient(handler);
        var service = new LcuClientFeatureService(new Sender(), rewardHttpClient: http);
        var result = await service.ClaimRewardAsync("mall:daily-essence", []);
        Assert.True(result.Succeeded); Assert.Contains("领取完成", result.Message);
        Assert.Equal(1, handler.Claims);
        Assert.False((await service.ClaimRewardAsync("mall:daily-essence", [])).Succeeded);
        Assert.Equal(1, handler.Claims);
    }

    [Fact]
    public async Task MallBusinessFailureIsNotTreatedAsSuccessOrRetried()
    {
        var handler = new MallHandler { RejectClaim = true };
        using var http = new HttpClient(handler);
        var service = new LcuClientFeatureService(new Sender(), rewardHttpClient: http);
        Assert.False((await service.ClaimRewardAsync("mall:daily-essence", [])).Succeeded);
        Assert.Equal(1, handler.Claims);
    }

    [Fact]
    public async Task UnavailableSourceDoesNotHideOtherRewards()
    {
        var lcu = new Sender(); lcu.Data["/lol-event-hub/v1/events"] = "invalid";
        using var http = new HttpClient(new MallHandler());
        var scan = await new LcuClientFeatureService(lcu, rewardHttpClient: http).ScanRewardsAsync();
        Assert.Single(scan.Rewards);
        Assert.Contains(scan.Sources, s => s.Source == "战令" && s.Message.Contains("未完成"));
    }

    [Fact]
    public async Task StaleOrLockedBattlePassCannotBeClaimed()
    {
        var lcu = new Sender();
        lcu.Data["/lol-event-hub/v1/events"] = "[{\"eventId\":\"pass\"}]";
        lcu.Data[Root + "/unclaimed-rewards"] = "{\"rewardsCount\":0}";
        var service = new LcuClientFeatureService(lcu);
        Assert.False((await service.ClaimRewardAsync("event:pass", [])).Succeeded);
        Assert.False((await service.ClaimRewardAsync("event:foreign", [])).Succeeded);
        Assert.Empty(lcu.Writes);
        lcu.Data[Root + "/unclaimed-rewards"] = "{\"rewardsCount\":1}";
        Assert.True((await service.ClaimRewardAsync("event:pass", [])).Succeeded);
        Assert.Equal(Root + "/claim-all", Assert.Single(lcu.Writes));
    }

    [Fact]
    public async Task UnsupportedRegionNeverSendsTencentRequest()
    {
        var lcu = new Sender(); lcu.Data["/lol-rso-auth/v1/authorization"] = "{\"currentPlatformId\":\"UNKNOWN\"}";
        var handler = new MallHandler(); using var http = new HttpClient(handler);
        var result = await new LcuClientFeatureService(lcu, rewardHttpClient: http).ClaimRewardAsync("mall:daily-essence", []);
        Assert.False(result.Succeeded); Assert.Equal(0, handler.Requests);
    }

    private const string Root = "/lol-event-hub/v1/events/pass/reward-track";
    private sealed class Sender : ILcuRequestSender
    {
        public Dictionary<string, string> Data { get; } = new()
        {
            ["/lol-rewards/v1/grants"] = "[]",
            ["/lol-event-hub/v1/events"] = "[]",
            ["/lol-rso-auth/v1/authorization"] = "{\"currentPlatformId\":\"HN1\"}",
            ["/lol-tencent-cookie-relay/v1/tft-cookies"] = "{\"https://apps.game.qq.com\":{\"uin\":\"o0012345\",\"skey\":\"dummy\"}}"
        };
        public List<string> Writes { get; } = [];
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(Data.GetValueOrDefault(endpoint));
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        { Writes.Add(endpoint); Data[Root + "/unclaimed-rewards"] = "{\"rewardsCount\":0}"; return Task.FromResult(true); }
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MallHandler : HttpMessageHandler
    {
        public int Claims, Requests;
        public bool RejectClaim;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("apps.game.qq.com", request.RequestUri!.Host);
            Assert.Contains("skey=dummy", request.Headers.GetValues("Cookie").Single());
            var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(s => s.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
            Assert.Equal("1", query["areaid"]); Assert.Equal("12345", query["roleid"]);
            string body;
            if (query["_service"] == "goods.hs.items") body = $"{{\"result\":0,\"data\":{{\"goods\":[{{\"userLeft\":{(Claims == 0 || RejectClaim ? 1 : 0)}}}]}}}}";
            else if (query["_service"] == "goods.hs.res") body = "{\"data\":{\"sfileMD5\":\"version\"}}";
            else
            {
                Assert.Equal("buy.plug.svr.sysc_ext", query["_service"]);
                Assert.Equal("8", query["paytype"]); Assert.Equal("338943", query["propid"]);
                Assert.Equal("1", query["buyNum"]);
                var extra = JObject.Parse(query["appext"]);
                Assert.Equal(0, extra.Value<int>("user_price")); Assert.Equal("version", extra.Value<string>("sfileMD5"));
                Claims++; body = RejectClaim ? "{\"ret\":-1}" : "{\"ret\":0}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
