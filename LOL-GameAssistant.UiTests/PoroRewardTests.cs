using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using System.Net;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PoroRewardTests
{
    private static string Month => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMM");

    [Fact]
    public async Task ScanShowsOnlyEligibleUnclaimedGiftsAndKeepsTheirDetails()
    {
        var rpc = new Rpc(); var handler = new PoroHttp(); using var http = new HttpClient(handler);
        var scan = await new LcuClientFeatureService(new Sender(), http, rpc).ScanRewardsAsync();
        var gift = Assert.Single(scan.Rewards);
        Assert.Equal($"poro:{Month}:3", gift.GrantId); Assert.False(gift.RequiresSelection);
        Assert.Contains("赏银 × 10", gift.Description);
        var status = Assert.Single(scan.Sources, s => s.Source == "好运魄罗");
        Assert.Contains("已签到 3 天", status.Message);
        Assert.Contains("已领取", status.Message); Assert.Contains("尚未达成", status.Message);
        Assert.Equal(0, rpc.Signatures); Assert.Equal(0, handler.Claims);
    }

    [Fact]
    public async Task ClaimUsesFreshNativeSignatureAndVerifiesStateBeforeAllowingAnotherClaim()
    {
        var rpc = new Rpc(); var handler = new PoroHttp(); using var http = new HttpClient(handler);
        var service = new LcuClientFeatureService(new Sender(), http, rpc);
        var result = await service.ClaimRewardAsync($"poro:{Month}:3", []);
        Assert.True(result.Succeeded); Assert.Contains("领取完成", result.Message);
        Assert.Equal("3", rpc.Stage); Assert.Equal(1, handler.Claims);
        Assert.False((await service.ClaimRewardAsync($"poro:{Month}:3", [])).Succeeded);
        Assert.Equal(1, handler.Claims); Assert.Equal(1, rpc.Signatures);
    }

    [Fact]
    public async Task InitialGiftUsesItsOwnFlowAndStageZeroSignature()
    {
        var rpc = new Rpc(); var handler = new PoroHttp { InitialEligible = true }; using var http = new HttpClient(handler);
        var result = await new LcuClientFeatureService(new Sender(), http, rpc).ClaimRewardAsync($"poro:{Month}:0", []);
        Assert.True(result.Succeeded); Assert.Equal("0", rpc.Stage); Assert.Equal("358524", handler.ClaimFlow);
    }

    [Theory]
    [InlineData("199901", "3")]
    [InlineData(null, "1")]
    [InlineData(null, "7")]
    [InlineData(null, "31")]
    [InlineData(null, "03")]
    public async Task StaleClaimedLockedAndUnknownGiftsDoNotGetSigned(string? month, string stage)
    {
        var rpc = new Rpc(); var handler = new PoroHttp(); using var http = new HttpClient(handler);
        Assert.False((await new LcuClientFeatureService(new Sender(), http, rpc)
            .ClaimRewardAsync($"poro:{month ?? Month}:{stage}", [])).Succeeded);
        Assert.Equal(0, rpc.Signatures); Assert.Equal(0, handler.Claims);
    }

    [Theory]
    [InlineData("different", 257)]
    [InlineData("12345", 513)]
    public async Task NativeIdentityMustMatchCurrentAccountAndRegion(string uin, int zone)
    {
        var rpc = new Rpc { Identity = new("1", uin, zone) }; var handler = new PoroHttp(); using var http = new HttpClient(handler);
        Assert.False((await new LcuClientFeatureService(new Sender(), http, rpc).ClaimRewardAsync($"poro:{Month}:3", [])).Succeeded);
        Assert.Equal(0, handler.Requests); Assert.Equal(0, rpc.Signatures);
    }

    [Fact]
    public async Task AccountChangeAfterSigningStopsSubmission()
    {
        var sender = new Sender { ChangeCookie = true }; var handler = new PoroHttp(); var rpc = new Rpc(); using var http = new HttpClient(handler);
        Assert.False((await new LcuClientFeatureService(sender, http, rpc).ClaimRewardAsync($"poro:{Month}:3", [])).Succeeded);
        Assert.Equal(1, rpc.Signatures); Assert.Equal(0, handler.Claims);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedOrUnknownSubmissionIsNeverRetried(bool timeout)
    {
        var handler = new PoroHttp { Reject = !timeout, UnknownOutcome = timeout }; using var http = new HttpClient(handler);
        var result = await new LcuClientFeatureService(new Sender(), http, new Rpc()).ClaimRewardAsync($"poro:{Month}:3", []);
        Assert.False(result.Succeeded); Assert.Equal(1, handler.Claims);
        Assert.DoesNotContain(new string('a', 32), result.Message);
    }

    [Fact]
    public async Task ActivityConfigCannotRedirectCookiesToAnotherHost()
    {
        var handler = new PoroHttp { Endpoint = "example.com/ams/ame/amesvr" }; using var http = new HttpClient(handler);
        var rpc = new Rpc();
        Assert.False((await new LcuClientFeatureService(new Sender(), http, rpc).ClaimRewardAsync($"poro:{Month}:3", [])).Succeeded);
        Assert.Equal(0, handler.Posts); Assert.Equal(0, rpc.Signatures);
    }

    [Fact]
    public async Task FrozenRewardIsShownAsUnconfirmedAndNotClaimed()
    {
        var handler = new PoroHttp { Frozen = true }; using var http = new HttpClient(handler);
        var rpc = new Rpc();
        var service = new LcuClientFeatureService(new Sender(), http, rpc);
        Assert.False((await service.ClaimRewardAsync($"poro:{Month}:3", [])).Succeeded);
        Assert.Equal(0, rpc.Signatures); Assert.Equal(0, handler.Claims);
    }

    [Fact]
    public void NativeFramesUseCallbackIdentityRatherThanTheResponseCommandByte()
    {
        Assert.Equal(30189, TencentClientRpc.CandidatePorts(5167)[0]);
        Assert.Equal("\u0002\u00060\0payload", TencentClientRpc.CreateCallFrame(6, "0", "payload"));
        Assert.Equal("signature", TencentClientRpc.ReadCallback("\u0002\u00001\0signature", "1"));
        Assert.Null(TencentClientRpc.ReadCallback("\u0001\u0018push", "1"));
        Assert.Null(TencentClientRpc.ReadCallback("\u0002\u00002\0wrong", "1"));
        Assert.Equal(new TencentClientIdentity("1", "12345", 257), TencentClientRpc.ParseIdentity("1\012345\0257\0unused"));
        Assert.Throws<InvalidOperationException>(() => TencentClientRpc.ParseIdentity("2\0openid\0257\0unused"));
    }

    [Fact]
    public void ConfigParserReadsOnlyTheAssignedJsonAndNeverExecutesJavascript()
    {
        Assert.Equal(1, LcuClientFeatureService.ParsePoroScript("var LOLSignInReward={\"date\":1}; throw 'do not execute';", "LOLSignInReward").Value<int>("date"));
        Assert.Throws<InvalidOperationException>(() => LcuClientFeatureService.ParsePoroScript("var other={};", "LOLSignInReward"));
    }

    private sealed class Rpc : ITencentClientRpc
    {
        public TencentClientIdentity Identity = new("1", "12345", 257);
        public int Signatures; public string? Stage;
        public Task<TencentClientIdentity> GetIdentityAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Identity); }
        public Task<TencentRewardSignature> SignRewardAsync(string stage, TencentClientIdentity expected, CancellationToken token)
        { Assert.Equal(Identity, expected); Stage = stage; Signatures++; return Task.FromResult(new TencentRewardSignature("0123456789", 1792630000000, new string('a', 32))); }
    }

    private sealed class Sender : ILcuRequestSender
    {
        public bool ChangeCookie; private int _sessions;
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult<string?>(endpoint switch
        {
            "/lol-rso-auth/v1/authorization" => "{\"currentPlatformId\":\"HN1\"}",
            "/lol-tencent-cookie-relay/v1/tft-cookies" => "{\"https://apps.game.qq.com\":{\"uin\":\"o0012345\",\"skey\":\"" + (ChangeCookie && ++_sessions > 1 ? "new-session" : "dummy") + "\"}}",
            "/lol-rewards/v1/grants" or "/lol-event-hub/v1/events" => "[]",
            _ => null
        });
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PoroHttp : HttpMessageHandler
    {
        public int Claims, Requests, Posts;
        public bool InitialEligible, Reject, UnknownOutcome, Frozen;
        public string? ClaimFlow;
        public string Endpoint = "x7m3.ams.game.qq.com/ams/ame/amesvr";
        private bool _claimed, _initialClaimed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.RequestUri.Host == "apps.game.qq.com")
                body = "{\"result\":0,\"data\":{\"goods\":[{\"userLeft\":0}]}}";
            else if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("lol.qq.com", request.RequestUri.Host);
                Assert.False(request.Headers.Contains("Cookie"));
                if (path.Contains("LOL_SignInReward")) body = "var LOLSignInReward=" + new JObject
                {
                    ["date"] = Month,
                    ["stage-reward"] = new JArray(Gift("1"), Gift("3"), Gift("7")),
                    ["initial-reward"] = Gift("0")
                } + ";";
                else body = "var ams_actdesc=" + new JObject
                {
                    ["iActivityId"] = path.Contains("510196") ? "510196" : "109144", ["sServiceType"] = "lol",
                    ["sServiceDepartment"] = "group_6", ["sAmePcUrl"] = Endpoint,
                    ["flows"] = new JObject { ["f_901066"] = new JObject(), ["f_356172"] = new JObject(), ["f_358524"] = new JObject() }
                } + "; var unrelated={};";
            }
            else
            {
                Posts++;
                Assert.Equal("x7m3.ams.game.qq.com", request.RequestUri.Host);
                Assert.Contains("iActivityId=", request.RequestUri.Query);
                Assert.Contains("sServiceDepartment=group_6", request.RequestUri.Query);
                Assert.Contains("skey=dummy", request.Headers.GetValues("Cookie").Single());
                var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(s => s.Split('=', 2))
                    .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
                Assert.Equal("lol", form["sServiceType"]); Assert.Equal("1", form["xhr"]);
                if (form.GetValueOrDefault("action") == "SignIn") body = Envelope(new JObject { ["SignIn"] = new JObject { ["msg"] = "1,2,3" } });
                else if (form.GetValueOrDefault("action") == "GetClaimStatus")
                {
                    var status = new JObject { ["initial_finished"] = InitialEligible, ["1"] = Claimed() };
                    if (_claimed) status["3"] = Claimed();
                    else if (Frozen) status["3"] = new JObject { ["used"] = 0, ["total"] = 1, ["remain"] = 0, ["freeze"] = 1, ["expire"] = false };
                    if (_initialClaimed) status["initial"] = Claimed();
                    body = Envelope(new JObject { ["GetClaimStatus"] = new JObject { ["msg"] = status } });
                }
                else
                {
                    Claims++; ClaimFlow = form["iFlowId"];
                    Assert.Equal("109144", form["iActivityId"]); Assert.Equal("1", form["sArea"]);
                    Assert.Equal("0123456789", form["nonce"]); Assert.Equal(new string('a', 32), form["token"]);
                    Assert.Equal("1792630000000", form["time"]);
                    if (ClaimFlow == "358524") { Assert.False(form.ContainsKey("iStage")); _initialClaimed = true; }
                    else { Assert.Equal("356172", ClaimFlow); Assert.Equal("3", form["iStage"]); _claimed = true; }
                    if (UnknownOutcome) throw new HttpRequestException("unknown outcome");
                    body = Reject ? "{\"ret\":700,\"flowRet\":{\"iRet\":700},\"modRet\":{\"iRet\":0}}" : Envelope(new JObject());
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
        private static JObject Gift(string stage) => new() { ["stage"] = stage, ["name"] = "魄罗礼盒", ["rewards"] = new JArray(new JObject { ["name"] = "赏银", ["amount"] = 10 }) };
        private static JObject Claimed() => new() { ["used"] = "1", ["total"] = "1", ["remain"] = "0", ["expire"] = false };
        private static string Envelope(JObject module) { module["iRet"] = 0; return new JObject { ["ret"] = "0", ["flowRet"] = new JObject { ["iRet"] = "0" }, ["modRet"] = module }.ToString(); }
    }
}
