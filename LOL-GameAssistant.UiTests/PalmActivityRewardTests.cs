using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class PalmActivityRewardTests
{
    [Theory]
    [InlineData("Dark")][InlineData("Light")]
    public void RewardWindowShowsVerifiedSourcesAndDeliveryRegion(string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(theme);
            using var fixture = new Fixture();
            using var form = new RewardSelectionForm(fixture.Service);
            form.Show();
            SecondPhaseRegressionTests.PumpUntil(() => Descendants(form).OfType<AntdUI.Button>().Count(b => b.Text == "领取奖励") == 3);
            Assert.Contains(Descendants(form).OfType<AntdUI.Label>(), l => l.Text?.Contains("领奖大区：艾欧尼亚") == true);
            Assert.Equal(3, Descendants(form).OfType<AntdUI.Button>().Count(b => b.Text == "打开官方活动页核对"));
            Assert.Equal(0, fixture.Http.Claims);
            if (Environment.GetEnvironmentVariable("PALM_REWARDS_PREVIEWS") is string path)
            {
                Directory.CreateDirectory(path);
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(path, "rewards-" + theme + ".png"));
            }
        }
        finally { UiTheme.SetMode(previous); }
    });

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    [Fact]
    public async Task ScanQueriesRealTaskAndClaimStatesWithoutSubmittingOrCompletingTasks()
    {
        using var fixture = new Fixture();
        var scan = await fixture.Service.ScanRewardsAsync();
        Assert.Equal(3, scan.Rewards.Count);
        Assert.All(scan.Rewards, r => { Assert.StartsWith("palm:", r.GrantId); Assert.False(r.RequiresSelection); });
        Assert.Contains(scan.Rewards, r => r.Title.Contains("查看赛事速递"));
        Assert.Contains(scan.Rewards, r => r.Title.Contains("表情奖励"));
        Assert.Contains(scan.Rewards, r => r.Title.Contains("暂存皮肤"));
        Assert.Contains(scan.Sources, s => s.Source == "全球总决赛暂存奖励" && s.Message.Contains("已拥有"));
        Assert.Equal(0, fixture.Http.Claims);
        Assert.DoesNotContain(fixture.Http.Flows, f => f is "1266808" or "1265257" or "1265297");
    }

    [Theory]
    [InlineData("1064772")][InlineData("1065770")][InlineData("1058041")]
    public async Task ClaimRechecksCurrentAccountEligibilityAndConfirmsStateWithoutRepeating(string activity)
    {
        using var fixture = new Fixture();
        var reward = Assert.Single((await fixture.Service.ScanRewardsAsync()).Rewards, r => r.GrantId.Split(':')[1] == activity);
        var result = await fixture.Service.ClaimRewardAsync(reward.GrantId, []);
        Assert.True(result.Succeeded); Assert.Contains("领取完成", result.Message);
        Assert.Equal(1, fixture.Http.Claims);
        Assert.False((await fixture.Service.ClaimRewardAsync(reward.GrantId, [])).Succeeded);
        Assert.Equal(1, fixture.Http.Claims);
    }

    [Fact]
    public async Task ExistingClaimAllIncludesOnlyVerifiedPalmRewards()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Service.ClaimAllPendingRewardsAsync()).Succeeded);
        Assert.Equal(3, fixture.Http.Claims);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task AccountOrCookieChangePreventsSubmission(bool changeAccount)
    {
        using var fixture = new Fixture();
        var reward = (await fixture.Service.ScanRewardsAsync()).Rewards.Single(r => r.GrantId.Contains(":1065770:"));
        fixture.Sender.Reads = 0;
        fixture.Sender.ChangeAt = changeAccount ? 1 : 3;
        fixture.Sender.ChangeAccount = changeAccount;
        Assert.False((await fixture.Service.ClaimRewardAsync(reward.GrantId, [])).Succeeded);
        Assert.Equal(0, fixture.Http.Claims);
    }

    [Theory]
    [InlineData("binding")][InlineData("region")][InlineData("account")][InlineData("claimed")]
    [InlineData("game")][InlineData("malformed")][InlineData("protocol")][InlineData("endpoint")]
    [InlineData("expired")][InlineData("module")]
    public async Task MusicUnknownIneligibleOrChangedStateNeverEntersClaimableRewards(string condition)
    {
        using var fixture = new Fixture(); fixture.Http.Condition = condition;
        var scan = await fixture.Service.ScanRewardsAsync();
        Assert.DoesNotContain(scan.Rewards, r => r.GrantId.Contains(":1065770:"));
        Assert.Equal(0, fixture.Http.Claims);
        Assert.Contains(scan.Sources, s => s.Source == "海斗音乐季表情奖励");
    }

    [Theory]
    [InlineData("watch-unknown")][InlineData("watch-claimed")][InlineData("watch-undone")][InlineData("watch-duplicate")]
    public async Task WatchRequiresBothOfficialTaskCompletionAndUnclaimedState(string condition)
    {
        using var fixture = new Fixture(); fixture.Http.Condition = condition;
        Assert.DoesNotContain((await fixture.Service.ScanRewardsAsync()).Rewards, r => r.GrantId.Contains(":1064772:"));
        Assert.Equal(0, fixture.Http.Claims);
    }

    [Fact]
    public async Task NewWatchTasksAreReportedAndNeverTurnedIntoGuessedClaimRequests()
    {
        using var fixture = new Fixture(); fixture.Http.Condition = "watch-new-task";
        var scan = await fixture.Service.ScanRewardsAsync();
        Assert.Single(scan.Rewards, r => r.GrantId.Contains(":1064772:"));
        Assert.Contains(scan.Sources, s => s.Source == "掌盟 S16 观赛任务" && s.Message.Contains("1 项新任务尚未适配"));
        Assert.Equal(0, fixture.Http.Claims);
    }

    [Theory]
    [InlineData("worlds-closed")][InlineData("worlds-owned")][InlineData("worlds-unknown")][InlineData("worlds-duplicate")]
    public async Task WorldsNeverExchangesPointsOrClaimsOwnedUnknownOrClosedRewards(string condition)
    {
        using var fixture = new Fixture(); fixture.Http.Condition = condition;
        Assert.DoesNotContain((await fixture.Service.ScanRewardsAsync()).Rewards, r => r.GrantId.Contains(":1058041:"));
        Assert.Equal(0, fixture.Http.Claims);
    }

    [Theory]
    [InlineData("reject")][InlineData("timeout")][InlineData("after-binding")][InlineData("after-malformed")]
    public async Task SubmissionFailuresAndUnknownOutcomesAreNotRetriedOrReportedComplete(string outcome)
    {
        using var fixture = new Fixture();
        var reward = (await fixture.Service.ScanRewardsAsync()).Rewards.Single(r => r.GrantId.Contains(":1065770:"));
        fixture.Http.Outcome = outcome;
        var result = await fixture.Service.ClaimRewardAsync(reward.GrantId, []);
        Assert.False(result.Succeeded); Assert.Equal(1, fixture.Http.Claims);
        Assert.DoesNotContain("dummy", result.Message); Assert.DoesNotContain("领取完成", result.Message);
    }

    [Fact]
    public async Task AcceptedButUnchangedStateIsExplicitlyUnconfirmed()
    {
        using var fixture = new Fixture();
        var reward = (await fixture.Service.ScanRewardsAsync()).Rewards.Single(r => r.GrantId.Contains(":1065770:"));
        fixture.Http.Outcome = "unchanged";
        var result = await fixture.Service.ClaimRewardAsync(reward.GrantId, []);
        Assert.True(result.Succeeded); Assert.Contains("尚未更新", result.Message); Assert.Equal(1, fixture.Http.Claims);
    }

    private sealed class Fixture : IDisposable
    {
        public Sender Sender = new(); public EventHttp Http = new();
        private readonly HttpClient _http;
        public LcuClientFeatureService Service;
        public Fixture() { _http = new(Http); Service = new(Sender, _http, new NoPoro()); }
        public void Dispose() => _http.Dispose();
    }
    private sealed class NoPoro : ITencentClientRpc
    {
        public Task<TencentClientIdentity> GetIdentityAsync(CancellationToken token) => throw new InvalidOperationException();
        public Task<TencentRewardSignature> SignRewardAsync(string stage, TencentClientIdentity expected, CancellationToken token) => throw new InvalidOperationException();
    }
    private sealed class Sender : ILcuRequestSender
    {
        public int Reads, ChangeAt = int.MaxValue; public bool ChangeAccount;
        public Task<string?> GetStringAsync(string endpoint, CancellationToken token = default)
        {
            string? result = endpoint switch
            {
                "/lol-rewards/v1/grants" or "/lol-event-hub/v1/events" => "[]",
                "/lol-rso-auth/v1/authorization" => "{\"currentPlatformId\":\"HN1\"}",
                "/lol-tencent-cookie-relay/v1/tft-cookies" => new JObject { ["https://apps.game.qq.com"] = new JObject
                    { ["uin"] = ++Reads >= ChangeAt && ChangeAccount ? "o0098765" : "o0012345", ["skey"] = Reads >= ChangeAt && !ChangeAccount ? "changed" : "dummy" } }.ToString(),
                _ => null
            };
            return Task.FromResult(result);
        }
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> PostAsync(string endpoint, string body, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> PutAsync(string endpoint, string body, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string body, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class EventHttp : HttpMessageHandler
    {
        public string? Condition, Outcome;
        public int Claims; public List<string> Flows = [];
        private readonly HashSet<string> _claimed = [];
        private static readonly Dictionary<string, string[]> Mappings = new()
        {
            ["1064772"] = ["er7gB9", "1265256", "pbYAoN", "1265296", "YpL796", "1265299"],
            ["1065770"] = ["KAvQLc", "1266807", "4b9da9", "1266785", "ONhKHV", "1266809"],
            ["1058041"] = ["98Vc2D", "1258159", "DcTqBD", "1262751", "nt32P2", "1264506", "Uep2mX", "1266321"]
        };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            JObject data;
            var uri = request.RequestUri!;
            if (uri.Host == "apps.game.qq.com") data = JObject.Parse("{\"result\":0,\"data\":{\"goods\":[{\"userLeft\":0}]}}");
            else if (uri.Host == "mlol.qt.qq.com")
            {
                Assert.Equal(HttpMethod.Get, request.Method); Assert.False(request.Headers.Contains("Cookie"));
                string[] paths = ["a20261015matchs16", "a20261022musicseason", "a20261014lplevent"];
                var rows = paths.Select((p, i) => new JObject { ["taskId"] = i + 1, ["title"] = p, ["gameid"] = "lol", ["intent"] = $"https://lol.qq.com/act/{p}/index.html",
                    ["activityStartTime"] = "2020-01-01 00:00:00", ["activityEndTime"] = "2100-01-01 00:00:00" });
                data = new() { ["code"] = 0, ["data"] = new JObject { ["result"] = 0, ["next"] = "0", ["feedsInfo"] = new JArray(new JObject { ["feedNews"] = new JObject { ["body"] = new JArray(rows) } }) } };
            }
            else if (uri.Host == "lol.qq.com")
            {
                Assert.Equal(HttpMethod.Get, request.Method); Assert.False(request.Headers.Contains("Cookie"));
                string id = uri.Segments[^2].Trim('/'); var mapping = Mappings[id]; var aliases = new JObject(); var flows = new JObject();
                for (int i = 0; i < mapping.Length; i += 2) { aliases[mapping[i]] = mapping[i + 1]; flows["f_" + mapping[i + 1]] = new JObject(); }
                if (Condition == "protocol" && id == "1065770") aliases["ONhKHV"] = "123";
                data = new() { ["iActivityId"] = id, ["sServiceType"] = "lol", ["sServiceDepartment"] = "k6_lol", ["iShutdown"] = "0",
                    ["dtBeginTime"] = "2020-01-01 00:00:00", ["dtEndTime"] = Condition == "expired" && id == "1065770" ? "2000-01-01 00:00:00" : "2100-01-01 00:00:00",
                    ["sAmePcUrl"] = Condition == "endpoint" && id == "1065770" ? "evil.example/ams/ame/amesvr" : "x7m3.ams.game.qq.com/ams/ame/amesvr",
                    ["sSDID"] = new string('a', 32), ["alias"] = aliases, ["flows"] = flows };
                return Reply("var ams_actdesc=" + data.ToString(Newtonsoft.Json.Formatting.None) + ";");
            }
            else
            {
                Assert.Equal("x7m3.ams.game.qq.com", uri.Host); Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("skey=dummy", request.Headers.GetValues("Cookie").Single());
                var fields = (await request.Content!.ReadAsStringAsync(token)).Split('&').Select(x => x.Split('=', 2))
                    .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
                string id = fields["iActivityId"], flow = fields["iFlowId"]; var mapping = Mappings[id]; Flows.Add(flow);
                Assert.Equal("k6_lol", fields["sServiceDepartment"]);
                if (flow == mapping[1])
                {
                    Assert.Equal("true", fields["query"]);
                    bool missing = id == "1065770" && (Condition == "binding" || Claims > 0 && Outcome == "after-binding");
                    data = new() { ["bindarea"] = missing ? JValue.CreateNull() : new JObject
                        { ["Farea"] = id == "1065770" && Condition == "region" ? 3 : 1, ["Fuin"] = id == "1065770" && Condition == "account" ? "98765" : "12345" } };
                }
                else if (flow == mapping[3])
                {
                    if (id == "1065770")
                    {
                        data = new() { ["iClaimed"] = _claimed.Contains(id) || Condition == "claimed" ? 1 : 0, ["iGameCount"] = Condition == "game" ? 0 : 2, ["bindArea"] = 1 };
                        if (Condition == "malformed" || Claims > 0 && Outcome == "after-malformed") data.Remove("iClaimed");
                    }
                    else if (id == "1064772")
                    {
                        data = new() { ["statuList"] = new JArray(new JObject { ["taskId"] = 1, ["done"] = Condition == "watch-undone" ? 0 : 1 },
                            new JObject { ["taskId"] = 2, ["done"] = 0 }, new JObject { ["taskId"] = 3, ["done"] = 1 }),
                            ["holdList"] = new JArray(new JObject { ["key"] = "daily_login_reward", ["iUsedNum"] = _claimed.Contains(id) || Condition == "watch-claimed" ? 1 : 0 },
                                new JObject { ["key"] = "once_task6023", ["iUsedNum"] = 1 }) };
                        if (Condition == "watch-unknown") data.Remove("holdList");
                        if (Condition == "watch-duplicate") ((JArray)data["statuList"]!).Add(new JObject { ["taskId"] = 1, ["done"] = 1 });
                        if (Condition == "watch-new-task") ((JArray)data["statuList"]!).Add(new JObject { ["taskId"] = 7, ["done"] = 1 });
                    }
                    else
                    {
                        var pending = new JArray();
                        if (!_claimed.Contains(id)) pending.Add(new JObject { ["id"] = "101", ["sExtend3"] = "1234" });
                        pending.Add(new JObject { ["id"] = "102", ["sExtend3"] = "5678" });
                        if (Condition == "worlds-duplicate") pending.Add(new JObject { ["id"] = "102", ["sExtend3"] = "5678" });
                        data = new() { ["zcx"] = pending, ["skinRec"] = 1, ["openShop"] = Condition == "worlds-closed" ? 0 : 1 };
                        if (Condition == "worlds-unknown") data.Remove("zcx");
                    }
                }
                else if (id == "1058041" && flow == "1266321")
                {
                    Assert.Equal("1", fields["iChangeArea"]);
                    data = new() { ["res"] = fields["iItemCode"] == "5678" || Condition == "worlds-owned" ? 1 : 0 };
                }
                else
                {
                    Assert.Equal(mapping[5], flow); Claims++;
                    if (id == "1064772") Assert.Equal("1", fields["taskId"]);
                    if (id == "1058041") { Assert.Equal("101", fields["id"]); Assert.Equal("1", fields["iChangeArea"]); }
                    if (Outcome == "timeout") throw new HttpRequestException("timeout");
                    if (Outcome == "reject") return Reply("{\"ret\":-1}");
                    if (Outcome != "unchanged") _claimed.Add(id);
                    data = new();
                }
                data = new() { ["ret"] = 0, ["flowRet"] = new JObject { ["iRet"] = 0 }, ["modRet"] = new JObject { ["ret"] = 0,
                    ["iRet"] = Condition == "module" && id == "1065770" ? -1 : 0, ["jData"] = data } };
            }
            return Reply(data.ToString());
        }
        private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
