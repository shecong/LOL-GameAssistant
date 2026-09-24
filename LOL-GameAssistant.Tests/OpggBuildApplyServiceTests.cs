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
    /// <summary>
    /// 自定义符文页额度已满时（选人阶段客户端还会自建一个临时页），
    /// 只能就地改写当前页：既不能删用户的自定义页，也不能因为 pages 数量虚高而误判并中止。
    /// </summary>
    [Fact]
    public async Task ApplyBuild_WhenCustomRunePagesAreFull_RewritesCurrentPageWithoutDeletingAnything()
    {
        var lcu = new FakeLcuRequestSender(
            ownedPageCount: 2,
            customPageCount: 2,
            new JObject { ["id"] = 11, ["name"] = "101资料站-菲兹", ["current"] = false, ["isTemporary"] = false },
            new JObject { ["id"] = 22, ["name"] = "101资料站-提莫", ["current"] = false, ["isTemporary"] = false },
            new JObject { ["id"] = 33, ["name"] = "诺克萨斯之手 - 征服者", ["current"] = true, ["isTemporary"] = true, ["isEditable"] = true });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", CreateOption());

        Assert.True(result.Succeeded);
        Assert.Contains("已改写当前使用的符文页", result.Message);
        Assert.Empty(lcu.DeleteEndpoints);
        Assert.Contains("/lol-perks/v1/pages/33", lcu.PutEndpoints);
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 11 && page.Value<string>("name") == "101资料站-菲兹");
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 22 && page.Value<string>("name") == "101资料站-提莫");
        Assert.Contains(lcu.RunePages, page =>
            page.Value<long?>("id") == 33 &&
            (page.Value<string>("name") ?? "").StartsWith("LOL助手 OP.GG ·", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyBuild_WhenCustomRunePagesHaveRoom_CreatesManagedPage()
    {
        var lcu = new FakeLcuRequestSender(
            ownedPageCount: 2,
            customPageCount: 1,
            new JObject { ["id"] = 11, ["name"] = "保留的自定义页", ["current"] = true, ["isTemporary"] = false });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", CreateOption());

        Assert.True(result.Succeeded);
        Assert.Contains("符文页已设为当前", result.Message);
        Assert.Empty(lcu.DeleteEndpoints);
        Assert.Contains(lcu.RunePages, page =>
            page.Value<long?>("id") == 99 &&
            (page.Value<string>("name") ?? "").StartsWith("LOL助手 OP.GG ·", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyBuild_WhenRecommendationHasSpells_PatchesCurrentChampionSelection()
    {
        var lcu = new FakeLcuRequestSender(
            ownedPageCount: 2,
            customPageCount: 1,
            new JObject { ["id"] = 11, ["name"] = "保留的自定义页", ["current"] = true, ["isTemporary"] = false });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(
            1,
            "TOP",
            CreateOption() with { SummonerSpellIds = new[] { 4, 14 } });

        Assert.True(result.Succeeded);
        Assert.Contains("召唤师技能", result.Message);
        Assert.Contains("/lol-champ-select/v1/session/my-selection", lcu.PatchEndpoints);
    }

    private static OpggBuildOption CreateOption() => new(
        1,
        new[] { 1055 },
        new[] { 3078, 3006, 3031 },
        Array.Empty<int>(),
        8000,
        8100,
        new[] { 8005, 9111, 9104, 8014, 8139, 8105 },
        100,
        55);

    private sealed class FakeChampionCatalog : IChampionCatalog
    {
        public string GetDisplayName(int championId) => championId == 1 ? "测试英雄" : "";
        public IReadOnlyList<ChampionReference> GetAll() => Array.Empty<ChampionReference>();
        public int? FindIdByDisplayName(string? displayName) => null;
    }

    private sealed class FakeLcuRequestSender : ILcuRequestSender
    {
        private readonly int _ownedPageCount;
        private readonly int? _customPageCount;

        public List<JObject> RunePages { get; }
        public List<string> DeleteEndpoints { get; } = new();
        public List<string> PutEndpoints { get; } = new();
        public List<string> PatchEndpoints { get; } = new();

        public FakeLcuRequestSender(int ownedPageCount, int? customPageCount, params JObject[] pages)
        {
            _ownedPageCount = ownedPageCount;
            _customPageCount = customPageCount;
            RunePages = pages.Select(page => (JObject)page.DeepClone()).ToList();
        }

        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            string content = endpoint switch
            {
                "/lol-perks/v1/pages" => new JArray(RunePages).ToString(Formatting.None),
                "/lol-perks/v1/inventory" => new JObject
                {
                    ["ownedPageCount"] = _ownedPageCount,
                    ["customPageCount"] = _customPageCount
                }.ToString(Formatting.None),
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
                page["id"] = 99;
                RunePages.Add(page);
            }
            return Task.FromResult(true);
        }

        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            PutEndpoints.Add(endpoint);
            if (endpoint.StartsWith("/lol-perks/v1/pages/", StringComparison.Ordinal))
            {
                long id = long.Parse(endpoint[(endpoint.LastIndexOf('/') + 1)..]);
                JObject? page = RunePages.FirstOrDefault(item => item.Value<long?>("id") == id);
                if (page != null)
                {
                    page["name"] = JObject.Parse(jsonBody).Value<string>("name");
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            PatchEndpoints.Add(endpoint);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            DeleteEndpoints.Add(endpoint);
            long id = long.Parse(endpoint[(endpoint.LastIndexOf('/') + 1)..]);
            RunePages.RemoveAll(page => page.Value<long?>("id") == id);
            return Task.FromResult(true);
        }
    }
}
