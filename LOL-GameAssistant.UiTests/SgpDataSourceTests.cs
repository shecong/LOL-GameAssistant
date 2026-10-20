using System.Net;
using System.Text;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.Lounge;
using LOL_GameAssistant.Infrastructure.Sgp;
using Newtonsoft.Json.Linq;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SgpDataSourceTests
{
    [Fact]
    public async Task CrossRegionUsesTargetServerAndNeverUsesCurrentRealmAddress()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("hn10-k8s-sgp.lol.qq.com", request.RequestUri!.Host);
            Assert.Equal("entitlements-test", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Json(new JObject { ["games"] = new JArray(Game(42, platform: "HN10")) }));
        }));
        var client = new SgpClient(Lcu(address: "https://hn1-k8s-sgp.lol.qq.com:21019"), http, targetPlatform: "HN10");
        var context = await client.GetContextAsync(default);
        Assert.True(context.IsCrossRegion); Assert.Equal("HN1", context.LoginPlatform);
        var result = (await new SgpMatchHistoryService(client).GetPageAsync("player", 0, 9))!.Games!.Games[0];
        Assert.Equal("HN10", result.PlatformId); Assert.True(result.IsCrossRegion);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ReadAsync(context, "/ranked", true, default));
    }

    [Fact]
    public async Task CrossRegionProfilesComeFromTargetHistoryWithoutLocalIdentityFallback()
    {
        var identities = Service<IPlayerProfileService>((_, _) => throw new InvalidOperationException("不得查询本区玩家资料"));
        var summary = SgpMatchMapper.Map(Game(42, platform: "HN10"), "HN10").Summary;
        var history = Service<IMatchHistoryService>((method, _) =>
        {
            Assert.Equal("GetPageAsync", method.Name);
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = [summary] } });
        });
        var service = new SgpPlayerProfileService(new(Lcu(), targetPlatform: "HN10"), identities, history);
        var result = (await service.GetByPuuidAsync("player"))!;
        Assert.Equal("测试#123", result.RiotId); Assert.Equal(0, result.SummonerLevel);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.FindByRiotIdAsync("测试", "123"));
    }

    [Fact]
    public async Task ChangingLoginRealmCannotReuseUnsafeReplayMetadataFromSeedCache()
    {
        string login = "HN10"; int calls = 0;
        var local = Service<ILcuRequestSender>((method, args) =>
        {
            Assert.Equal("GetStringAsync", method.Name);
            return Task.FromResult<string?>((string)args[0]! switch
            {
                "/lol-chat/v1/me" => new JObject { ["platformId"] = login }.ToString(),
                "/entitlements/v1/token" => "{\"accessToken\":\"test\"}", _ => null
            });
        });
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            return Task.FromResult(Json(request.RequestUri!.Query.Length > 0
                ? new JObject { ["games"] = new JArray(Game(42, platform: "HN10")) } : Game(42, platform: "HN10")));
        }));
        var service = new SgpMatchHistoryService(new(local, http, targetPlatform: "HN10"));
        await service.GetPageAsync("player", 0, 9); Assert.False((await service.GetDetailAsync(42))!.IsCrossRegion);
        login = "HN1";
        Assert.True((await service.GetDetailAsync(42))!.IsCrossRegion); Assert.Equal(2, calls);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Json(JToken json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json") };
    private static ILcuRequestSender Lcu(string platform = "HN1", string? address = null, List<string>? calls = null) => Service<ILcuRequestSender>((method, args) =>
    {
        Assert.Equal("GetStringAsync", method.Name);
        string route = (string)args[0]!; calls?.Add(route);
        return Task.FromResult<string?>(route switch
        {
            "/lol-chat/v1/me" => new JObject { ["platformId"] = platform }.ToString(),
            "/lol-platform-config/v1/namespaces/PlayerPreferences/ServiceEndpoint" => address == null ? null : new JValue(address).ToString(Newtonsoft.Json.Formatting.None),
            "/entitlements/v1/token" => "{\"accessToken\":\"entitlements-test\"}",
            "/lol-league-session/v1/league-session-token" => "\"league-session-test\"",
            _ => null
        });
    });
    private static JObject Game(long id, string puuid = "player", string platform = "HN1") => new()
    {
        ["json"] = new JObject
        {
            ["gameId"] = id, ["platformId"] = platform, ["gameCreation"] = 1700000000000,
            ["gameDuration"] = 1800, ["gameMode"] = "CLASSIC", ["queueId"] = 420, ["gameType"] = "MATCHED_GAME", ["endOfGameResult"] = "GameComplete",
            ["participants"] = new JArray(new JObject
            {
                ["puuid"] = puuid, ["participantId"] = 1, ["championId"] = 22, ["teamId"] = 100,
                ["riotIdGameName"] = "测试", ["riotIdTagline"] = "123", ["win"] = true,
                ["kills"] = 8, ["deaths"] = 2, ["assists"] = 9, ["summoner1Id"] = 4, ["summoner2Id"] = 7,
                ["totalMinionsKilled"] = 120, ["neutralMinionsKilled"] = 10, ["totalDamageDealtToChampions"] = 20000,
                ["visionScore"] = 20, ["item0"] = 3006, ["playerAugment1"] = 1234, ["largestKillingSpree"] = 8,
                ["perks"] = JObject.Parse("{\"styles\":[{\"selections\":[{\"perk\":8005}]}]}")
            }),
            ["teams"] = new JArray(new JObject { ["teamId"] = 100, ["bans"] = new JArray(new JObject { ["pickTurn"] = 1, ["championId"] = 11 }) })
        }
    };

    [Fact]
    public async Task PagesUseStartAndCountAndNeverInventTotal()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("entitlements-test", request.Headers.Authorization!.Parameter);
            int start = request.RequestUri.Query.Contains("startIndex=10&") ? 10 : 0;
            return Task.FromResult(Json(new JObject { ["games"] = new JArray(Enumerable.Range(start, 10).Select(i => Game(100 - i))) }));
        }));
        var service = new SgpMatchHistoryService(new(Lcu(), http));
        var first = (await service.GetPageAsync("player", 0, 9))!.Games!;
        var second = (await service.GetPageAsync("player", 10, 19))!.Games!;
        Assert.Contains("startIndex=10&count=10", paths[1]);
        Assert.Empty(first.Games.Select(p => p.GameId).Intersect(second.Games.Select(p => p.GameId)));
        Assert.False(first.IsTotalKnown); Assert.True(first.HasMore); Assert.Equal(11, first.GameCount);
        Assert.Equal(21, second.GameCount); Assert.Equal(10, second.GameIndexBegin);
    }

    [Fact]
    public async Task LargeRequestsSplitAndShortPageTerminates()
    {
        var offsets = new List<int>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            int start = int.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri!.Query, "startIndex=(\\d+)").Groups[1].Value);
            offsets.Add(start);
            return Task.FromResult(Json(new JObject { ["games"] = new JArray(Enumerable.Range(start, start == 0 ? 100 : 23).Select(i => Game(1000 - i))) }));
        }));
        var result = (await new SgpMatchHistoryService(new(Lcu(), http)).GetAllAsync("player", 500))!.Games!;
        Assert.Equal(new[] { 0, 100 }, offsets); Assert.Equal(123, result.Games.Count);
        Assert.True(result.IsTotalKnown); Assert.False(result.IsTruncated); Assert.Equal(123, result.GameCount);
    }

    [Fact]
    public async Task RepeatedWindowIsRejectedInsteadOfLooping()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Json(new JObject { ["games"] = new JArray(Enumerable.Range(1, 100).Select(i => Game(i))) }));
        }));
        await Assert.ThrowsAsync<IOException>(() => new SgpMatchHistoryService(new(Lcu(), http)).GetAllAsync("player", 500));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task EmptyPageAndBoundsAreHandled()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new JObject { ["games"] = new JArray() }))));
        var service = new SgpMatchHistoryService(new(Lcu(), http));
        var page = (await service.GetPageAsync("player", 0, 9))!.Games!;
        Assert.Empty(page.Games); Assert.Equal(-1, page.GameIndexEnd); Assert.True(page.IsTotalKnown);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPageAsync("player", -1, 9));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPageAsync("player", 0, 5000));
    }

    [Fact]
    public async Task SummaryProvidesFullDetailsWithoutTimelineDownloadAndClearInvalidatesSeed()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests++; Assert.EndsWith("SUMMARY", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(request.RequestUri.Query.Length > 0 ? new JObject { ["games"] = new JArray(Game(42)) } : Game(42)));
        }));
        var service = new SgpMatchHistoryService(new(Lcu(), http));
        await service.GetPageAsync("player", 0, 9);
        var detail = (await service.GetDetailAsync(42))!;
        Assert.Equal(1, requests); Assert.Equal("123", detail.GetPlayerIdentity("player")!.TagLine);
        var participant = detail.GetParticipant("player")!;
        Assert.Equal(4, participant.Spell1Id); Assert.Equal(8, participant.stats!.kills); Assert.True(participant.stats.Win);
        Assert.Equal(new[] { 1234 }, participant.stats.AugmentIds); Assert.Equal(new[] { 8005 }, participant.stats.RuneIds);
        Assert.Equal(new[] { 11 }, detail.GetBannedChampionIds(100));
        Assert.Equal(1, ((ILocalCacheStore)service).MeasureMemory().Entries);
        ((ILocalCacheStore)service).Clear(); await service.GetDetailAsync(42); Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("HN10", "player", 42)]
    [InlineData("HN1", "someone-else", 42)]
    [InlineData("HN1", "player", 0)]
    public async Task WrongRealmPlayerOrIdCannotEnterCache(string platform, string puuid, long id)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new JObject { ["games"] = new JArray(Game(id, puuid, platform)) }))));
        await Assert.ThrowsAsync<IOException>(() => new SgpMatchHistoryService(new(Lcu(), http)).GetPageAsync("player", 0, 9));
    }

    [Fact]
    public async Task RankedUsesLeagueSessionAndMapsRankAndTrueLosses()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("league-session-test", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Json(JObject.Parse("{\"queues\":[{\"queueType\":\"RANKED_SOLO_5x5\",\"tier\":\"GOLD\",\"rank\":\"I\",\"highestRank\":\"II\",\"wins\":43,\"losses\":0,\"leaguePoints\":93}]}")));
        }));
        var result = (await new SgpRankedStatsService(new(Lcu(), http)).GetAsync("player"))!.Queues["RANKED_SOLO_5x5"];
        Assert.Equal("I", result.Division); Assert.Equal("II", result.HighestDivision); Assert.True(result.HasWinLossRecord); Assert.Equal(0, result.Losses);
    }

    [Fact]
    public async Task CurrentClientConfigurationSupportsOtherRealmsWithoutGuessingHost()
    {
        var calls = new List<string>();
        var client = new SgpClient(Lcu("EUW1", "https://euw1-sgp.riotgames.com", calls));
        var context = await client.GetContextAsync(default);
        Assert.Equal("EUW1", context.Platform); Assert.Equal("euw1-sgp.riotgames.com", context.Server.Host);
        Assert.DoesNotContain(calls, p => p.Contains("match-history"));
        await Assert.ThrowsAsync<NotSupportedException>(() => new SgpClient(Lcu("UNKNOWN")).GetContextAsync(default));
    }

    [Theory]
    [InlineData("http://hn1-k8s-sgp.lol.qq.com:21019")]
    [InlineData("https://sgp.lol.qq.com.attacker.example")]
    [InlineData("https://sgp.lol.qq.com/path")]
    [InlineData("https://user@sgp.lol.qq.com")]
    public async Task UntrustedConfigurationCannotReceiveCredentials(string address)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SgpClient(Lcu(address: address)).GetContextAsync(default));
    }

    [Fact]
    public async Task UnauthorizedRefreshesOnceButRedirectNeverAccepted()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new JObject(), ++requests == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.Redirect))));
        var client = new SgpClient(Lcu(), http); var context = await client.GetContextAsync(default);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.ReadAsync(context, "/test", false, default));
        Assert.Equal(HttpStatusCode.Redirect, error.StatusCode); Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ProfileUsesReadOnlyBatchAndPreservesRiotIdentityWhenSgpNameEmpty()
    {
        var identity = new PlayerProfile("player", "名字", "标签", 1, 1, 0, 0);
        var identities = Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(identity));
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("[\"player\"]", await request.Content!.ReadAsStringAsync(token));
            return Json(JObject.Parse("{\"puuid\":\"player\",\"name\":\"\",\"level\":100,\"profileIconId\":88}"));
        }));
        var result = (await new SgpPlayerProfileService(new(Lcu(), http), identities).GetByPuuidAsync("player"))!;
        Assert.Equal("名字#标签", result.RiotId); Assert.Equal(100, result.SummonerLevel); Assert.Equal(88, result.ProfileIconId);
    }

    [Fact]
    public async Task LoungeUsesUnifiedMatchSourceAndPreservesVictoryAndHighlights()
    {
        var detail = SgpMatchMapper.Map(Game(42), "HN1").Detail;
        var histories = Service<IMatchHistoryService>((_, _) => Task.FromResult<LOL_GameAssistant.Domain.Matches.MatchDetail?>(detail));
        var reader = new LoungeLcuReader(Lcu(), histories);
        var json = await reader.MatchAsync(42, default);
        Assert.True(LoungeLcuReader.ReadVictory(json, 42, "player"));
        Assert.Contains("legendary", LoungeLcuReader.ReadHighlights(json, 42, "player"));
    }

    [Fact]
    public async Task OldSessionCannotSendCredentialsAndCancellationIsPreserved()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((_, token) =>
        {
            requests++;
            return Task.FromCanceled<HttpResponseMessage>(token);
        }));
        var client = new SgpClient(Lcu(), http); var context = await client.GetContextAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync(context with { Session = context.Session + "-old" }, "/test", false, default));
        Assert.Equal(0, requests);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync(context, "/test", false, canceled.Token));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task TransportTimeoutBecomesCacheEligibleFailureAndReleasesSlot()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((_, _) => ++requests == 1
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")) : Task.FromResult(Json(new JObject()))));
        var client = new SgpClient(Lcu(), http); var context = await client.GetContextAsync(default);
        await Assert.ThrowsAsync<IOException>(() => client.ReadAsync(context, "/test", false, default));
        Assert.NotNull(await client.ReadAsync(context, "/test", false, default));
    }

    [Fact]
    public async Task UnboundedExportRequestIsCappedAndMarkedAsPartial()
    {
        int requests = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests++;
            int start = int.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri!.Query, "startIndex=(\\d+)").Groups[1].Value);
            return Task.FromResult(Json(new JObject { ["games"] = new JArray(Enumerable.Range(start, 100).Select(i => Game(10000 - i))) }));
        }));
        var result = (await new SgpMatchHistoryService(new(Lcu(), http)).GetAllAsync("player", int.MaxValue))!.Games!;
        Assert.Equal(50, requests); Assert.Equal(5000, result.Games.Count); Assert.True(result.IsTruncated); Assert.False(result.IsTotalKnown);
    }

    [Fact]
    public void LegacyUnknownIndexesNeverRelabelFirstWindowAsAnotherPage()
    {
        var legacy = new LOL_GameAssistant.Entity.GameHeadModel.MatchHistoryResponse { Games = new()
        {
            GameCount = 1, GameIndexBegin = 0, GameIndexEnd = 0,
            Games = [new() { GameId = 42 }]
        } };
        Assert.Empty(LegacyMatchReadModelMapper.ToDomainPage(legacy, 10, 19)!.Games!.Games);
    }

    [Fact]
    public async Task PremadeDetectionUsesInjectedSourceAndOnlyClaimsHistoricalInference()
    {
        int reads = 0;
        var summaries = new List<MatchHistoryGame>();
        foreach (int id in new[] { 41, 42 })
        {
            var envelope = Game(id);
            ((JArray)envelope["json"]!["participants"]!).Add(new JObject
                { ["participantId"] = 2, ["puuid"] = "friend", ["teamId"] = 100 });
            summaries.Add(SgpMatchMapper.Map(envelope, "HN1").Summary);
        }
        var history = Service<IMatchHistoryService>((method, args) =>
        {
            Assert.Equal("GetPageAsync", method.Name); Assert.Equal(0, args[1]); Assert.Equal(19, args[2]); reads++;
            return Task.FromResult<MatchHistoryResponse?>(new() { Games = new() { Games = summaries } });
        });
        var result = await new LegacyPremadeDetectionService(history).DetectAsync(
            [new("player", "玩家"), new("friend", "队友")], []);
        Assert.Equal(2, reads); Assert.Single(result.Groups); Assert.True(result.Groups[0].IsInferred);
        Assert.Equal(new[] { "friend", "player" }, result.Groups[0].Puuids.Order());
    }
}
