using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using System.Net;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PoroShopTests
{
    [Fact]
    public async Task CatalogUsesTightestLimitAndDoesNotIncludeDraws()
    {
        using var fixture = new Fixture(); fixture.Handler.DailyLeft = 0;
        var shop = await fixture.Service.GetPoroShopAsync();
        Assert.Equal(100, shop.Silver); Assert.Equal(50, shop.Gold); Assert.Equal(2, shop.Items.Count);
        var silver = shop.Items.Single(i => i.Id == "1");
        Assert.Equal(0, silver.Remaining); Assert.False(silver.CanExchange);
        Assert.Equal(PoroShopCurrency.Gold, shop.Items.Single(i => i.Id == "2").Currency);
        Assert.Equal(0, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task MultipleItemsAndQuantitiesAreExchangedAndVerifiedIndividually()
    {
        using var fixture = new Fixture();
        var preview = await fixture.Service.GetPoroShopAsync();
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 2), new("2", 1)]);
        Assert.All(result.Items, r => Assert.Equal(PoroShopExchangeState.Succeeded, r.State));
        Assert.Equal(3, result.Items.Sum(r => r.Succeeded)); Assert.Equal(3, fixture.Handler.Exchanges);
        Assert.Equal(80, fixture.Handler.Silver); Assert.Equal(30, fixture.Handler.Gold);
        Assert.Equal(["1", "1", "2"], fixture.Handler.Purchased);
    }

    [Fact]
    public async Task CombinedCostIsCheckedBeforeAnyExchange()
    {
        using var fixture = new Fixture(); fixture.Handler.Silver = 15;
        var preview = await fixture.Service.GetPoroShopAsync();
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 2)]);
        Assert.Contains("总价超过", result.Message); Assert.Equal(0, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task ChangedPriceRejectsWholeBatchBeforeSpending()
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        fixture.Handler.Price = 11;
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 1), new("2", 1)]);
        Assert.All(result.Items, r => Assert.Equal(PoroShopExchangeState.Rejected, r.State));
        Assert.Equal(0, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task ChangedAccountRejectsBeforeSpending()
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        fixture.Sender.Uin = "999"; fixture.Rpc.Uin = "999";
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 1)]);
        Assert.Contains("账号", result.Message); Assert.Equal(0, fixture.Handler.Exchanges);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("missing-code")]
    [InlineData("stale-status")]
    public async Task UnknownOutcomeStopsAndNeverRetries(string outcome)
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        fixture.Handler.Outcome = outcome;
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 2), new("2", 1)]);
        Assert.Equal(PoroShopExchangeState.Unconfirmed, result.Items[0].State);
        Assert.Equal(PoroShopExchangeState.Skipped, result.Items[1].State);
        Assert.Equal(1, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task BusinessRejectionPreservesPartialSuccessAndStopsRest()
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        fixture.Handler.RejectAt = 2;
        var result = await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 2), new("2", 1)]);
        Assert.Equal(1, result.Items[0].Succeeded); Assert.Equal(PoroShopExchangeState.Rejected, result.Items[0].State);
        Assert.Equal(PoroShopExchangeState.Skipped, result.Items[1].State); Assert.Equal(2, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task DuplicateSelectionsCannotCauseDuplicateSpending()
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 1), new("1", 1)]);
        Assert.Equal(0, fixture.Handler.Exchanges);
    }

    [Fact]
    public async Task QuantitiesAboveLimitAndDrawItemsAreRejected()
    {
        using var fixture = new Fixture(); var preview = await fixture.Service.GetPoroShopAsync();
        await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("1", 4)]);
        await fixture.Service.ExchangePoroShopItemsAsync(preview, [new("draw", 1)]);
        Assert.Equal(0, fixture.Handler.Exchanges);
    }

    private sealed class Fixture : IDisposable
    {
        public Sender Sender = new(); public Rpc Rpc = new(); public ShopHttp Handler = new();
        private readonly HttpClient _http;
        public LcuClientFeatureService Service;
        public Fixture() { _http = new HttpClient(Handler); Service = new(Sender, _http, Rpc); }
        public void Dispose() => _http.Dispose();
    }
    private sealed class Rpc : ITencentClientRpc
    {
        public string Uin = "12345";
        public Task<TencentClientIdentity> GetIdentityAsync(CancellationToken token) => Task.FromResult(new TencentClientIdentity("1", Uin, 257));
        public Task<TencentRewardSignature> SignRewardAsync(string stage, TencentClientIdentity expected, CancellationToken token) => throw new Exception("Shop does not need reward signing");
    }
    private sealed class Sender : ILcuRequestSender
    {
        public string Uin = "12345";
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(endpoint switch
            {
                "/lol-rso-auth/v1/authorization" => "{\"currentPlatformId\":\"HN1\"}",
                "/lol-tencent-cookie-relay/v1/tft-cookies" => new JObject { ["https://apps.game.qq.com"] = new JObject { ["uin"] = "o00" + Uin, ["skey"] = "dummy" } }.ToString(),
                _ => null
            });
        }
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class ShopHttp : HttpMessageHandler
    {
        public long Silver = 100, Gold = 50, Price = 10;
        public int DailyLeft = 3, GoldLeft = 2, Exchanges, RejectAt = -1;
        public string Outcome = "";
        public List<string> Purchased = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("apps.game.qq.com", request.RequestUri!.Host);
            Assert.Contains("skey=dummy", request.Headers.GetValues("Cookie").Single());
            var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
            JObject body;
            switch (query["_service"])
            {
                case "welink.usertask.QueryLolCloudTicket":
                    Assert.Equal("1,1", query["area"]);
                    body = new() { ["ret_code"] = 0, ["data"] = new JObject { ["1"] = Silver, ["3"] = Gold, ["summonerLevel"] = 412 } }; break;
                case "goods.lol.judou.limit":
                    body = new() { ["result"] = 0, ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["dh_data"] = new JObject { ["goods"] = new JArray(Item("1", "38", Price, DailyLeft), Item("2", "42", 20, GoldLeft), Item("draw", "39", 10, 10)) } }; break;
                case "buy.plug.swoole.loljf":
                    Exchanges++;
                    Assert.Equal("1", query["iZone"]); Assert.Equal("9805", query["_plug_id"]); Assert.Equal("1003", query["_app_id"]);
                    Assert.True(uint.TryParse(query["p_tk"], out _));
                    if (Exchanges == RejectAt) { body = new() { ["ret"] = -1 }; break; }
                    string id = query["iGoodsSeqId"]; Purchased.Add(id);
                    if (Outcome != "stale-status")
                    {
                        if (id == "1") { Assert.Equal("38", query["iActionType"]); Silver -= Price; DailyLeft--; }
                        else { Assert.Equal("42", query["iActionType"]); Gold -= 20; GoldLeft--; }
                    }
                    if (Outcome == "timeout") throw new HttpRequestException("unknown result");
                    body = Outcome == "missing-code" ? new() : new() { ["ret"] = 0 }; break;
                default: throw new Exception("Unexpected shop operation");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToString()) });
        }
        private static JObject Item(string id, string type, long price, int left) => new()
        {
            ["iGoodsId"] = id, ["sGoodsName"] = "商品" + id, ["sGoodsDesc"] = "兑换物品", ["iType"] = type,
            ["iPlugId"] = "9805", ["sAppIds"] = "1003", ["iPrice"] = price,
            ["dtBeginTime"] = "2020-01-01 00:00:00", ["dtEndTime"] = "2099-01-01 00:00:00",
            ["limit"] = new JArray(new JObject { ["left"] = left, ["type"] = "2" }, new JObject { ["left"] = 20, ["type"] = "4" })
        };
    }
}
