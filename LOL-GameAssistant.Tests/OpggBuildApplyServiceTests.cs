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
    public void MayhemBuilds_ParseSinglePanelPageWithoutPanelMarker()
    {
        const string html = "Summoner Spells summoner-spell-icons/4.png summoner-spell-icons/32.png " +
            "Starting Items item-icons/1054.png Core Items</h3> " +
            "#1 </span> item-icons/3084.png item-icons/3111.png item-icons/3065.png " +
            "Win Rate: <span class='rate'>55.5%</span> Pick Rate: <span class='rate'>15.7%</span> " +
            "Situational Items item-icons/3001.png";

        var options = OpggBuildApplyService.ParseMayhemBuildOptions(html);

        Assert.Single(options);
        Assert.Equal([3084, 3111, 3065], options[0].CoreItemIds);
        Assert.Equal([4, 32], options[0].SummonerSpellIds);
        Assert.Equal(55.5, options[0].WinRate);
    }

    /// <summary>
    /// 额度已满且没有助手创建的页面时，用户页和选人阶段的临时页都不能改写。
    /// </summary>
    [Fact]
    public async Task ApplyBuild_WhenCustomRunePagesAreFull_LeavesUserPagesUnchanged()
    {
        var lcu = new FakeLcuRequestSender(
            ownedPageCount: 2,
            customPageCount: 2,
            new JObject { ["id"] = 11, ["name"] = "101资料站-菲兹", ["current"] = false, ["isTemporary"] = false },
            new JObject { ["id"] = 22, ["name"] = "101资料站-提莫", ["current"] = false, ["isTemporary"] = false },
            new JObject { ["id"] = 33, ["name"] = "诺克萨斯之手 - 征服者", ["current"] = true, ["isTemporary"] = true, ["isEditable"] = true });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", CreateOption());

        Assert.False(result.Succeeded);
        Assert.Contains("没有可替换的助手符文页", result.Message);
        Assert.Empty(lcu.DeleteEndpoints);
        Assert.DoesNotContain("/lol-perks/v1/pages/33", lcu.PutEndpoints);
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 11 && page.Value<string>("name") == "101资料站-菲兹");
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 22 && page.Value<string>("name") == "101资料站-提莫");
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 33 && page.Value<string>("name") == "诺克萨斯之手 - 征服者");
    }

    [Fact]
    public async Task ApplyBuild_WhenFullAndReplacementAuthorized_ReplacesOnlyCurrentEditablePage()
    {
        var lcu = new FakeLcuRequestSender(2, 2,
            new JObject { ["id"] = 11, ["name"] = "保留页", ["current"] = false, ["isEditable"] = true },
            new JObject { ["id"] = 22, ["name"] = "原当前页", ["current"] = true, ["isEditable"] = true });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        var result = await service.ApplyBuildAsync(1, "TOP", CreateOption(), allowReplaceCurrentRunePage: true);

        Assert.True(result.Succeeded);
        Assert.Contains(lcu.RunePages, page => page.Value<long>("id") == 11 && page.Value<string>("name") == "保留页");
        Assert.Contains(lcu.RunePages, page => page.Value<long>("id") == 22 && page.Value<bool>("current") &&
            page["selectedPerkIds"]!.Values<int>().SequenceEqual(CreateOption().RunePerkIds));
        Assert.Contains("/lol-item-sets/v1/item-sets/1/sets", lcu.PutEndpoints);
        Assert.Empty(lcu.DeleteEndpoints);
    }

    [Fact]
    public async Task ApplyBuild_WhenAuthorizedReplacementSwitchFails_RestoresOriginalRunes()
    {
        var original = new JObject
        {
            ["id"] = 22, ["name"] = "原当前页", ["current"] = true, ["isEditable"] = true,
            ["primaryStyleId"] = 8100, ["subStyleId"] = 8000,
            ["order"] = 0,
            ["selectedPerkIds"] = new JArray(8112, 8139, 8120, 8105, 9111, 9104)
        };
        var lcu = new FakeLcuRequestSender(1, 1, original) { FailNextCurrentSwitch = true };
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        var result = await service.ApplyBuildAsync(1, "TOP", CreateOption(), allowReplaceCurrentRunePage: true);

        Assert.False(result.Succeeded);
        Assert.True(JToken.DeepEquals(original, Assert.Single(lcu.RunePages)));
        Assert.DoesNotContain("/lol-item-sets/v1/item-sets/1/sets", lcu.PutEndpoints);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ApplyBuild_WhenCurrentPageCannotBeReplaced_LeavesItUnchanged(bool editable, bool temporary)
    {
        var original = new JObject
        {
            ["id"] = 22, ["name"] = "原当前页", ["current"] = true,
            ["isEditable"] = editable, ["isTemporary"] = temporary
        };
        var lcu = new FakeLcuRequestSender(1, 1, original);
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        var result = await service.ApplyBuildAsync(1, "TOP", CreateOption(), allowReplaceCurrentRunePage: true);

        Assert.False(result.Succeeded);
        Assert.Empty(lcu.PutEndpoints);
        Assert.True(JToken.DeepEquals(original, Assert.Single(lcu.RunePages)));
    }

    [Fact]
    public async Task ApplyBuild_WhenManagedPageExists_ReplacesOnlyManagedPage()
    {
        var lcu = new FakeLcuRequestSender(2, 2,
            new JObject { ["id"] = 11, ["name"] = "用户符文页", ["current"] = true },
            new JObject { ["id"] = 22, ["name"] = "LOL助手 OP.GG · 旧方案", ["current"] = false });
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", CreateOption());

        Assert.True(result.Succeeded);
        Assert.Contains("已替换助手创建的旧页", result.Message);
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 11 && page.Value<string>("name") == "用户符文页");
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 22 && page.Value<string>("name") == "LOL助手 OP.GG · 测试英雄 上路 · 方案 1");
        Assert.Empty(lcu.DeleteEndpoints);
    }

    [Fact]
    public async Task ApplyBuild_WhenCurrentSwitchFails_RestoresManagedPage()
    {
        var lcu = new FakeLcuRequestSender(2, 2,
            new JObject { ["id"] = 11, ["name"] = "用户符文页", ["current"] = true },
            new JObject { ["id"] = 22, ["name"] = "LOL助手 OP.GG · 旧方案", ["current"] = false })
        { FailNextCurrentSwitch = true };
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP", CreateOption());

        Assert.False(result.Succeeded);
        Assert.Contains(lcu.RunePages, page => page.Value<long?>("id") == 22 && page.Value<string>("name") == "LOL助手 OP.GG · 旧方案");
        Assert.Contains("/lol-perks/v1/pages/22", lcu.PutEndpoints);
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

    [Fact]
    public async Task ApplyBuild_WhenSpellWriteFails_ReportsPartialApply()
    {
        var lcu = new FakeLcuRequestSender(2, 1,
            new JObject { ["id"] = 11, ["name"] = "用户符文页", ["current"] = true })
        { FailSpellWrite = true };
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        OpggBuildApplyResult result = await service.ApplyBuildAsync(1, "TOP",
            CreateOption() with { SummonerSpellIds = new[] { 4, 14 } });

        Assert.False(result.Succeeded);
        Assert.Contains("部分已写入", result.Message);
        Assert.Contains("召唤师技能写入失败", result.Message);
    }

    [Fact]
    public async Task CapturePersonalPreset_ReadsCurrentRunesAndFallsBackToSessionForSpells()
    {
        var lcu = new FakeLcuRequestSender(2, 1)
        {
            CurrentRunePage = new JObject
            {
                ["name"] = "我的致命节奏", ["primaryStyleId"] = 8000, ["subStyleId"] = 8100,
                ["selectedPerkIds"] = new JArray(8005, 9111, 9104, 8014, 8139, 8105)
            },
            SelectionSession = new JObject
            {
                ["localPlayerCellId"] = 3,
                ["myTeam"] = new JArray(new JObject
                {
                    ["cellId"] = 3, ["spell1Id"] = 4, ["spell2Id"] = 7
                })
            }
        };
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());

        var preset = await service.CaptureCurrentRunePresetAsync(22, "ranked", "BOTTOM");

        Assert.NotNull(preset);
        Assert.Equal(22, preset.ChampionId);
        Assert.Equal("adc", preset.Position);
        Assert.Equal([4, 7], preset.SummonerSpellIds);
        Assert.Equal(6, preset.RunePerkIds.Count);
    }

    [Fact]
    public async Task ApplyPersonalPreset_WritesManagedPageAndSummonerSpells()
    {
        var lcu = new FakeLcuRequestSender(2, 1);
        var service = new OpggBuildApplyService(lcu, new FakeChampionCatalog());
        var preset = new LOL_GameAssistant.Domain.Builds.PersonalRunePreset
        {
            Name = "常用射手", ChampionId = 22, Mode = "ranked", Position = "adc",
            PrimaryStyleId = 8000, SubStyleId = 8100,
            RunePerkIds = [8005, 9111, 9104, 8014, 8139, 8105],
            SummonerSpellIds = [4, 7]
        };

        OpggBuildApplyResult result = await service.ApplyPersonalRunePresetAsync(preset);

        Assert.True(result.Succeeded);
        Assert.Contains(lcu.RunePages, page =>
            (page.Value<string>("name") ?? "").StartsWith("LOL助手 个人 · 常用射手", StringComparison.Ordinal));
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
        public bool FailNextCurrentSwitch { get; set; }
        public bool FailSpellWrite { get; set; }
        public JObject? CurrentRunePage { get; set; }
        public JObject? MySelection { get; set; }
        public JObject? SelectionSession { get; set; }

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
                "/lol-perks/v1/currentpage" => (CurrentRunePage ?? new JObject()).ToString(Formatting.None),
                "/lol-champ-select/v1/session/my-selection" => (MySelection ?? new JObject()).ToString(Formatting.None),
                "/lol-champ-select/v1/session" => (SelectionSession ?? new JObject()).ToString(Formatting.None),
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
            if (endpoint == "/lol-perks/v1/currentpage")
            {
                // LCU requires a scalar page ID, not an object containing an ID.
                JToken token = JToken.Parse(jsonBody);
                Assert.Equal(JTokenType.Integer, token.Type);
                if (!FailNextCurrentSwitch)
                    foreach (var page in RunePages) page["current"] = page.Value<long>("id") == token.Value<long>();
            }
            if (endpoint == "/lol-perks/v1/currentpage" && FailNextCurrentSwitch)
            {
                FailNextCurrentSwitch = false;
                return Task.FromResult(false);
            }
            if (endpoint.StartsWith("/lol-perks/v1/pages/", StringComparison.Ordinal))
            {
                long id = long.Parse(endpoint[(endpoint.LastIndexOf('/') + 1)..]);
                JObject? page = RunePages.FirstOrDefault(item => item.Value<long?>("id") == id);
                if (page != null)
                {
                    foreach (var property in JObject.Parse(jsonBody).Properties())
                        page[property.Name] = property.Value.DeepClone();
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            PatchEndpoints.Add(endpoint);
            return Task.FromResult(!FailSpellWrite);
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
