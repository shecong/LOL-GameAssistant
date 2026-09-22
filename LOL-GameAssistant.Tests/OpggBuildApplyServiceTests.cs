using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class OpggBuildApplyServiceTests
{
    [Fact]
    public async Task ApplyBuild_WhenRunePagesAreFull_DeletesOnlyTheCurrentPageThenCreatesManagedPage()
    {
        var lcu = new FakeLcuRequestSender(
            new JObject { ["id"] = 11, ["name"] = "保留的自定义页", ["current"] = false, ["isDeletable"] = true },
            new JObject { ["id"] = 22, ["name"] = "当前自定义页", ["current"] = true, ["isDeletable"] = true });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());
        var option = new OpggBuildOption(
            1,
            new[] { 1055 },
            new[] { 3078, 3006, 3031 },
            Array.Empty<int>(),
            8000,
            8100,
            new[] { 8005, 9111, 9104, 8014, 8139, 8105 },
            100,
            55);

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", option);

        Assert.True(result.Succeeded);
        Assert.Contains("替换原当前符文页", result.Message);
        Assert.Equal(new[] { "/lol-perks/v1/pages/22" }, lcu.DeleteEndpoints);
        Assert.Contains(lcu.RunePages, page => (page.Value<string>("name") ?? "").StartsWith("LOL助手 OP.GG ·", StringComparison.Ordinal));
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 11);
    }

    private sealed class FakeChampionCatalog : IChampionCatalog
    {
        public string GetDisplayName(int championId) => championId == 1 ? "测试英雄" : "";
        public IReadOnlyList<ChampionReference> GetAll() => Array.Empty<ChampionReference>();
        public int? FindIdByDisplayName(string? displayName) => null;
    }

    private sealed class FakeLcuRequestSender : ILcuRequestSender
    {
        public List<JObject> RunePages { get; }
        public List<string> DeleteEndpoints { get; } = new();

        public FakeLcuRequestSender(params JObject[] pages) => RunePages = pages.Select(page => (JObject)page.DeepClone()).ToList();

        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            string content = endpoint switch
            {
                "/lol-perks/v1/pages" => new JArray(RunePages).ToString(Formatting.None),
                "/lol-perks/v1/inventory" => new JObject { ["ownedPageCount"] = 2 }.ToString(Formatting.None),
                "/lol-summoner/v1/current-summoner" => new JObject { ["summonerId"] = 1, ["accountId"] = 1 }.ToString(Formatting.None),
                _ when endpoint.StartsWith("/lol-item-sets/v1/item-sets/", StringComparison.Ordinal) =>
                    new JObject { ["accountId"] = 1, ["itemSets"] = new JArray() }.ToString(Formatting.None),
                _ => "{}"
            };
            return Task.FromResult<string?>(content);
        }

        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            if (endpoint == "/lol-perks/v1/pages")
            {
                var page = JObject.Parse(jsonBody);
                page["id"] = 33;
                RunePages.Add(page);
            }
            return Task.FromResult(true);
        }

        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            DeleteEndpoints.Add(endpoint);
            long id = long.Parse(endpoint[(endpoint.LastIndexOf('/') + 1)..]);
            RunePages.RemoveAll(page => page.Value<long?>("id") == id);
            return Task.FromResult(true);
        }
    }
}
