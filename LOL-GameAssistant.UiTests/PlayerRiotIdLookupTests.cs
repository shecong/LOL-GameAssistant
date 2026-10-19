using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class PlayerRiotIdLookupTests
{
    [Fact]
    public async Task AliasLookupResolvesChineseRiotIdBeforeLoadingProfile()
    {
        var requests = new List<string>();
        var service = new LcuPlayerProfileService(Service<ILcuRequestSender>((_, args) =>
        {
            string path = (string)args[0]!;
            requests.Add(path);
            return Task.FromResult<string?>(path.Contains("alias/lookup")
                ? "{\"alias\":{\"gameName\":\"悲伤诉说我爱你\",\"tagLine\":\"22301\"},\"puuid\":\"target\"}"
                : "{\"puuid\":\"target\",\"gameName\":\"悲伤诉说我爱你\",\"tagLine\":\"22301\",\"summonerLevel\":123}");
        }));
        var player = await service.FindByRiotIdAsync(" 悲伤诉说我爱你 ", "#22301");
        Assert.Equal("target", player!.Puuid);
        Assert.Equal(123, player.SummonerLevel);
        Assert.Equal(new[]
        {
            "/lol-summoner/v1/alias/lookup?gameName=" + Uri.EscapeDataString("悲伤诉说我爱你") + "&tagLine=22301",
            "/lol-summoner/v2/summoners/puuid/target"
        }, requests);
    }

    [Fact]
    public async Task KnownAliasWithUnavailableLocalProfileReportsAvailabilityInsteadOfSearchingAnotherName()
    {
        var requests = new List<string>();
        var service = new LcuPlayerProfileService(Service<ILcuRequestSender>((_, args) =>
        {
            string path = (string)args[0]!;
            requests.Add(path);
            return Task.FromResult<string?>(path.Contains("alias/lookup") ? "{\"puuid\":\"target\"}" : null);
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FindByRiotIdAsync("悲伤诉说我爱你", "22301"));
        Assert.Contains("已找到该 Riot ID", error.Message);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task UnsupportedAliasEndpointRetainsLegacyClientFallback()
    {
        var service = new LcuPlayerProfileService(Service<ILcuRequestSender>((_, args) =>
            Task.FromResult<string?>(((string)args[0]!).Contains("summoners?name=")
                ? "{\"puuid\":\"legacy\",\"gameName\":\"玩家\",\"tagLine\":\"123\"}" : null)));
        Assert.Equal("legacy", (await service.FindByRiotIdAsync("玩家", "123"))!.Puuid);
    }
}
