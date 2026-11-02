using System.Net;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class OpggSkinPopularityTests
{
    // Fields and values from OP.GG's public Lux page; the default page order is ownership,
    // whereas “best skins” sorts the same rows by play, not by count or win_rate.
    private const string Data = """
        53:["$","$L53",null,{"data":[
        {"skin_id":99029,"is_base":false,"champion_key":"lux","total_play":3736498,"play":86860,"pick_rate":2.324636598226468,"count":1099128,"chromas":[
          {"chroma_id":99032,"total_play":86860,"play":6601,"pick_rate":7.599585539949344},
          {"chroma_id":99081,"total_play":0,"play":0,"pick_rate":"$NaN"},
          {"chroma_id":103001,"play":500}]},
        {"skin_id":99017,"is_base":false,"champion_key":"lux","total_play":3736498,"play":357082,"pick_rate":9.556595507344042,"count":648770},
        {"skin_id":99007,"is_base":false,"champion_key":"lux","total_play":3736498,"play":658691,"pick_rate":17.62856557129162,"count":1175358},
        {"skin_id":99000,"is_base":true,"champion_key":"lux","total_play":3736498,"play":1500000},
        {"skin_id":103027,"is_base":false,"champion_key":"ahri","total_play":1000,"play":500},
        {"skin_id":99088,"is_base":false,"champion_key":"lux","total_play":1000,"play":1001},
        {"skin_id":99089,"is_base":false,"champion_key":"lux","total_play":0,"play":0}]}]
        """;

    private static string Page(string payload) => "<script>self.__next_f.push([1," + JsonConvert.SerializeObject(payload) + "])</script>";

    [Fact]
    public void ReadsUsageAndSortsByPlaysInsteadOfOwnershipOrPageOrder()
    {
        var statistics = OpggSkinPopularityService.ParseStatistics(Page(Data), "Lux");
        Assert.Equal(new[] { 7, 17, 29, 32 }, statistics.Select(s => s.SkinNumber));
        Assert.Equal(658691, statistics[0].Plays);
        Assert.Equal(17.62856557129162, statistics[0].PickRate, 8);
        Assert.Equal(100d * 6601 / 3736498, statistics[3].PickRate, 8);
        Assert.Empty(OpggSkinPopularityService.ParseStatistics("<html>Changed format</html>", "Lux"));
        // Optional replay of a freshly downloaded public page, with the production parser.
        if (Environment.GetEnvironmentVariable("OPGG_SKIN_PAGE_REPLAY") is string path && path.Length > 0)
        {
            var live = OpggSkinPopularityService.ParseStatistics(File.ReadAllText(path), "Lux");
            Assert.True(live.Count > 10);
            Assert.Contains(live, row => row.SkinNumber == 7 && row.Plays > 0 && row.PickRate > 0);
        }
    }

    [Fact]
    public void SupportsStreamChunksSplitInsideDataAndIgnoresMalformedArrays()
    {
        int split = Data.IndexOf("total_play", StringComparison.Ordinal) + 5;
        var splitPage = Page(Data[..split]) + Page(Data[split..]);
        Assert.Equal(OpggSkinPopularityService.ParseStatistics(Page(Data), "Lux"), OpggSkinPopularityService.ParseStatistics(splitPage, "Lux"));
        Assert.Empty(OpggSkinPopularityService.ParseStatistics(Page("1:{\"data\":[malformed}"), "Lux"));
    }

    [Fact]
    public async Task LiveTransportReadsAniviaAndLuxWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("OPGG_SKIN_LIVE") != "1") return;
        var service = new OpggSkinPopularityService();
        foreach (var model in new[] { "Anivia", "Lux" })
        {
            var statistics = await service.GetStatisticsAsync(model);
            Assert.True(statistics.Count > 10, model);
            Assert.All(statistics, row => Assert.True(row.Plays > 0 && row.PickRate > 0));
        }
    }

    [Fact]
    public async Task HttpFailureIsNotCachedAndTransportNegotiatesHttp2()
    {
        var handler = new Handler { Status = HttpStatusCode.Forbidden };
        using var http = new HttpClient(handler);
        var service = new OpggSkinPopularityService(http);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetStatisticsAsync("Lux"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        handler.Status = HttpStatusCode.OK; handler.Page = Page(Data);
        Assert.Equal(4, (await service.GetStatisticsAsync("Lux")).Count);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(HttpVersion.Version20, handler.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.Policy);
        Assert.Contains("sc-Assistant", handler.UserAgent);
    }

    [Fact]
    public async Task CachesSuccessButAllowsRetryAfterUnusableResponse()
    {
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var service = new OpggSkinPopularityService(http);
        handler.Page = "<html>No statistics</html>";
        Assert.Empty(await service.GetStatisticsAsync("Lux"));
        handler.Page = Page(Data);
        Assert.Equal(4, (await service.GetStatisticsAsync("Lux")).Count);
        Assert.Equal(4, (await service.GetStatisticsAsync("LUX")).Count);
        Assert.Equal(2, handler.Requests);
        Assert.Equal("https://op.gg/zh-cn/lol/skins/statistics/lux", handler.Url);
        Assert.Empty(await service.GetStatisticsAsync("../Lux"));
        Assert.Equal(2, handler.Requests);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Page = "";
        public int Requests;
        public string? Url;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Version? Version;
        public HttpVersionPolicy Policy;
        public string UserAgent = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++; Url = request.RequestUri?.AbsoluteUri;
            Version = request.Version; Policy = request.VersionPolicy; UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Page) });
        }
    }
}
