using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class BuildConfigurationRegressionTests
{
    private static OpggBuildOption Option => new(1, [], [3006, 3031, 3094], [], 8000, 8200,
        [8005, 8009, 9104, 8014, 8234, 8236], 100, 50);

    [Theory]
    [InlineData("None", true)]
    [InlineData("Lobby", true)]
    [InlineData("Matchmaking", true)]
    [InlineData("ReadyCheck", true)]
    [InlineData("ChampSelect", true)]
    [InlineData("InProgress", false)]
    [InlineData("GameStart", false)]
    public async Task VerifiedApplyUsesTheSameEditablePhasesAsManualConfiguration(string phase, bool expected)
    {
        using var folder = new TestFolder();
        var lcu = new Client { Phase = phase };
        int applications = 0;
        var inner = Service<IOpggBuildApplyService>((method, args) =>
        {
            Assert.Equal("ApplyBuildAsync", method.Name);
            applications++;
            lcu.Page["selectedPerkIds"] = new JArray(Option.RunePerkIds);
            return Task.FromResult(OpggBuildApplyResult.Success("已应用符文"));
        });
        var service = new VerifiedBuildApplyService(inner, lcu, folder.Path);
        var result = await service.ApplyBuildAsync(1, "TOP", Option, applySummonerSpells: false, applyItemSet: false);
        Assert.Equal(expected, result.Succeeded);
        Assert.Equal(expected ? 1 : 0, applications);
    }

    [Fact]
    public async Task MissingItemSetsDoNotBlockRunesOrOverwriteUncapturedItemsDuringRestore()
    {
        using var folder = new TestFolder();
        var lcu = new Client { ItemsReadable = false };
        var originalPage = lcu.Page.DeepClone();
        int applications = 0;
        var inner = Service<IOpggBuildApplyService>((method, args) =>
        {
            Assert.Equal("ApplyBuildAsync", method.Name);
            Assert.False((bool)args[6]!); // No item write without an item baseline.
            applications++;
            lcu.Page["selectedPerkIds"] = new JArray(Option.RunePerkIds);
            return Task.FromResult(OpggBuildApplyResult.Success("已应用符文"));
        });
        var service = new VerifiedBuildApplyService(inner, lcu, folder.Path);
        var result = await service.ApplyBuildAsync(1, "TOP", Option, applySummonerSpells: false);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, applications);
        Assert.Contains("保留原出装", result.Message);
        Assert.Empty(lcu.ItemWrites);
        lcu.ItemsReadable = true;
        var originalItems = lcu.Items.DeepClone();
        var restore = await service.RestoreLastConfigurationAsync();
        Assert.True(restore.Succeeded, restore.Message);
        Assert.True(JToken.DeepEquals(originalPage, lcu.Page));
        Assert.True(JToken.DeepEquals(originalItems, lcu.Items));
        Assert.Empty(lcu.ItemWrites);
    }

    [Fact]
    public async Task MissingItemsAndNoWritableRunesDoNotReportAnApplicationThatDidNothing()
    {
        using var folder = new TestFolder();
        var lcu = new Client { ItemsReadable = false };
        var inner = Service<IOpggBuildApplyService>((_, _) => throw new InvalidOperationException("Must not write"));
        var service = new VerifiedBuildApplyService(inner, lcu, folder.Path);
        var result = await service.ApplyBuildAsync(1, "TOP", Option with { RunePerkIds = [] }, applySummonerSpells: false);
        Assert.False(result.Succeeded);
        Assert.Empty(lcu.ItemWrites);
    }

    private sealed class Client : ILcuRequestSender
    {
        public string Phase = "Lobby";
        public bool ItemsReadable = true;
        public JObject Page = JObject.Parse("""{"id":1,"name":"manual","primaryStyleId":8000,"subStyleId":8200,"selectedPerkIds":[1,2,3,4,5,6]}""");
        public JObject Items = JObject.Parse("""{"itemSets":[{"uid":"manual","title":"自己的出装"}]}""");
        public List<string> ItemWrites = [];
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default) =>
            Task.FromResult(endpoint switch
            {
                "/lol-summoner/v1/current-summoner" => "{\"puuid\":\"a\",\"summonerId\":42}",
                "/lol-gameflow/v1/gameflow-phase" => "\n\"" + Phase + "\"\n",
                "/lol-perks/v1/pages" => new JArray(Page.DeepClone()).ToString(),
                "/lol-perks/v1/currentpage" => Page.ToString(),
                "/lol-item-sets/v1/item-sets/42/sets" => ItemsReadable ? Items.ToString() : null,
                _ => "{}"
            });
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            if (endpoint == "/lol-perks/v1/pages/1") Page = JObject.Parse(jsonBody);
            if (endpoint.Contains("item-sets")) ItemWrites.Add(jsonBody);
            return Task.FromResult(true);
        }
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lol-build-regression-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
