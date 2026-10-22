using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ChampionSkinResourceTests
{
    [Theory]
    [InlineData("kNoRarity", SkinQuality.Standard)]
    [InlineData("kEpic", SkinQuality.Epic)]
    [InlineData("kLegendary", SkinQuality.Legendary)]
    [InlineData("kUltimate", SkinQuality.Ultimate)]
    [InlineData("kMythic", SkinQuality.Mythic)]
    [InlineData("kExalted", SkinQuality.Exalted)]
    [InlineData("kTranscendent", SkinQuality.Transcendent)]
    [InlineData("futureQuality", SkinQuality.Unknown)]
    [InlineData(null, SkinQuality.Unknown)]
    public void ReadsClientQualityWithoutGuessing(string? rarity, SkinQuality expected)
    {
        Assert.Equal(expected, LcuChampionSkinResourceService.ReadQuality(rarity, false));
        Assert.Equal(SkinQuality.Base, LcuChampionSkinResourceService.ReadQuality(rarity, true));
    }

    [Fact]
    public async Task ChromasInheritQualityAndMissingMetadataStaysUnknown()
    {
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((_, args) => Task.FromResult<string?>(((string)args[0]!).EndsWith("champion-summary.json")
            ? "[{\"id\":99,\"alias\":\"Lux\"}]"
            : """{"skins":[{"id":99000,"name":"拉克丝"},{"id":99007,"name":"大元素使","rarity":"kUltimate","chromas":[{"id":99008,"name":"炫彩","rarity":"kNoRarity"}]},{"id":99009,"name":"传说名字不能代表品质"}]}"""));
        var catalog = await new LcuChampionSkinResourceService(sender).GetCatalogAsync("Lux");
        Assert.Equal(SkinQuality.Base, catalog.Single(s => s.SkinNumber == 0).Quality);
        Assert.Equal(SkinQuality.Ultimate, catalog.Single(s => s.SkinNumber == 7).Quality);
        var chroma = catalog.Single(s => s.SkinNumber == 8);
        Assert.True(chroma.IsChroma); Assert.Equal(SkinQuality.Ultimate, chroma.Quality);
        Assert.Equal(SkinQuality.Unknown, catalog.Single(s => s.SkinNumber == 9).Quality);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10, 0, 0, 0, 0];

    [Fact]
    public async Task UsesIndependentChromaPreviewBeforeParentSplash()
    {
        var images = new List<string>();
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((method, args) =>
        {
            string path = (string)args[0]!;
            if (method.Name == "GetBytesAsync")
            {
                images.Add(path);
                return Task.FromResult<byte[]?>(Png);
            }
            return Task.FromResult<string?>(path.EndsWith("champion-summary.json")
                ? "[{\"id\":99,\"alias\":\"Lux\"}]"
                : """
                  {"skins":[{"id":99008,"name":"春晖神女 拉克丝",
                  "splashPath":"/lol-game-data/assets/ASSETS/Characters/Lux/Skins/Skin08/Images/lux_splash_centered_8.jpg",
                  "chromas":[{"id":99009,"name":"春晖神女 拉克丝 玉露金风",
                  "chromaPath":"/lol-game-data/assets/v1/champion-chroma-images/99/99009.png",
                  "tilePath":"/lol-game-data/assets/v1/champion-chroma-images/99/99009.png"}]}]}
                  """);
        });
        var service = new LcuChampionSkinResourceService(sender);
        var chroma = (await service.GetCatalogAsync("Lux")).Single(skin => skin.SkinNumber == 9);
        Assert.Equal("春晖神女 拉克丝 玉露金风", chroma.Name);
        Assert.NotNull(await service.GetPosterAsync(chroma));
        Assert.Equal("/lol-game-data/assets/v1/champion-chroma-images/99/99009.png", Assert.Single(images));
    }

    [Fact]
    public async Task UsesClientNamesAndExactSelectionPosterPathsIncludingChromaFallback()
    {
        var reads = new List<string>();
        var images = new List<string>();
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((method, args) =>
        {
            string path = (string)args[0]!;
            if (method.Name == "GetBytesAsync")
            {
                images.Add(path);
                return Task.FromResult<byte[]?>(path.EndsWith("official.jpg") ? Png : "{\"errorCode\":404}"u8.ToArray());
            }
            Assert.Equal("GetStringAsync", method.Name);
            reads.Add(path);
            return Task.FromResult<string?>(path.EndsWith("champion-summary.json")
                ? "[{\"id\":99,\"alias\":\"Lux\",\"name\":\"光辉女郎\"}]"
                : """
                  {"skins":[{"id":99007,"name":"大元素使 拉克丝","splashPath":"/ASSETS/Characters/Lux/Skins/Skin07/missing.jpg",
                  "uncenteredSplashPath":"/lol-game-data/assets/ASSETS/Characters/Lux/Skins/Skin07/official.jpg",
                  "chromas":[{"id":99008,"name":"大元素使 炫彩"}]}]}
                  """);
        });
        var service = new LcuChampionSkinResourceService(sender);
        var catalog = await service.GetCatalogAsync("lUx");
        Assert.Equal("大元素使 拉克丝", catalog.Single(s => s.SkinNumber == 7).Name);
        var chroma = catalog.Single(s => s.SkinNumber == 8);
        Assert.Equal("大元素使 炫彩", chroma.Name);
        var poster = await service.GetPosterAsync(chroma);
        Assert.Equal(Png, poster!.Content);
        Assert.Equal(2, images.Count);
        Assert.All(images, path => Assert.StartsWith("/lol-game-data/assets/ASSETS/Characters/Lux/Skins/Skin07/", path));
        await service.GetPosterAsync(catalog.Single(s => s.SkinNumber == 7));
        Assert.Equal(3, images.Count); // Missing path is retried; successful parent image is cached.
        await service.GetCatalogAsync("Lux");
        Assert.Equal(2, reads.Count);
    }

    [Fact]
    public async Task EmptyCatalogIsRetriedAndVersionChangeInvalidatesNames()
    {
        int summaries = 0;
        string version = "1";
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((method, args) =>
        {
            Assert.Equal("GetStringAsync", method.Name);
            bool summary = ((string)args[0]!).EndsWith("champion-summary.json");
            if (summary && ++summaries == 1) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(summary ? "[{\"id\":99,\"alias\":\"Lux\"}]" : "{\"skins\":[{\"id\":99000,\"name\":\"拉克丝\"}]}");
        });
        var service = new LcuChampionSkinResourceService(sender, () => version);
        Assert.Empty(await service.GetCatalogAsync("Lux"));
        Assert.Single(await service.GetCatalogAsync("Lux"));
        Assert.Single(await service.GetCatalogAsync("Lux"));
        Assert.Equal(2, summaries);
        version = "2";
        Assert.Single(await service.GetCatalogAsync("Lux"));
        Assert.Equal(3, summaries);
    }

    [Theory]
    [InlineData("https://example.com/image.jpg")]
    [InlineData("/ASSETS/../secret")]
    [InlineData("/other-resource/image.jpg")]
    public async Task NeverRequestsNonGameAssetPaths(string path)
    {
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((_, _) => throw new InvalidOperationException("Unexpected request"));
        Assert.Null(await new LcuChampionSkinResourceService(sender).GetPosterAsync(new ChampionSkinResource(0, "基础", [path])));
    }

    [Fact]
    public async Task FallsBackToClientSkinMapWhenChampionDetailUnavailable()
    {
        var sender = SecondPhaseRegressionTests.Service<ILcuRequestSender>((method, args) => Task.FromResult<string?>((string)args[0]! switch
        {
            "/lol-game-data/assets/v1/champion-summary.json" => "[{\"id\":99,\"alias\":\"Lux\"}]",
            "/lol-game-data/assets/v1/skins.json" => "{\"99007\":{\"id\":99007,\"name\":\"大元素使 拉克丝\"},\"1000\":{\"id\":1000,\"name\":\"其他英雄\"}}",
            _ => null
        }));
        var catalog = await new LcuChampionSkinResourceService(sender).GetCatalogAsync("Lux");
        Assert.Equal(7, Assert.Single(catalog).SkinNumber);
    }
}
