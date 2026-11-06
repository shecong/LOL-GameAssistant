using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CslolSkinNamesTests
{
    private readonly CslolSkinNames _names = new();
    [Fact]
    public void MapsChampionAndSkinUsingIdsWithoutChangingPackageIdentity()
    {
        var skin = new CslolSkinEntry { ChampionKey = "Vayne", SkinId = 67011, SkinName = "PROJECT Vayne", FileName = "skins/Vayne/PROJECT Vayne.fantome" };
        Assert.Equal("薇恩", _names.Champion("Vayne"));
        Assert.Equal("薇恩", _names.Champion("vayne"));
        Assert.Equal("源计划：净化 薇恩", _names.Display(skin));
        Assert.Equal("PROJECT Vayne", skin.SkinName);
        Assert.Equal("skins/Vayne/PROJECT Vayne.fantome", skin.FileName);
        Assert.True(_names.Matches(skin, "净化")); Assert.True(_names.Matches(skin, "project"));
    }
    [Fact]
    public void ChromaUsesItsFullIdInsteadOfParentSkinOrEnglishColor()
    {
        var skin = new CslolSkinEntry { ChampionKey = "Annie", SkinId = 1013, SkinName = "Lunar Beast Annie", ChromaId = "1014", ChromaName = "Ruby" };
        Assert.Equal("福牛守护者 安妮 贺岁", _names.Display(skin));
        Assert.True(_names.Matches(skin, "贺岁")); Assert.True(_names.Matches(skin, "Ruby"));
    }
    [Theory]
    [InlineData(103086, "联盟不朽 阿狸")]
    [InlineData(145071, "联盟不朽 卡莎")]
    [InlineData(18080, "联盟不朽 崔丝塔娜")]
    public void IncludesTieredSkinNames(int id, string name) => Assert.Equal(name, _names.Display(new() { SkinId = id }));
    [Fact]
    public void PackageOnlyWeaponFormsHaveDistinctChineseLabels()
    {
        Assert.Equal("王 佛耶戈 · 战士武器", _names.Display(new() { SkinId = 234043, ChromaId = "234045" }));
        Assert.Equal("王 佛耶戈 · 法师武器", _names.Display(new() { SkinId = 234043, ChromaId = "234049" }));
    }
    [Fact]
    public void UnmappedFutureEntriesKeepTheirOriginalNamesAndDistinctChroma()
    {
        Assert.Equal("My New Skin", _names.Display(new() { SkinId = 999999, SkinName = "My New Skin" }));
        Assert.Equal("Custom", _names.Champion("Custom"));
        Assert.Equal("福牛守护者 安妮 · New Color", _names.Display(new() { SkinId = 1013, ChromaId = "999999", ChromaName = "New Color" }));
    }
    [Fact]
    public void ProgressMessagesUseTheSameDisplayNames()
    {
        var skin = new CslolSkinEntry { SkinId = 1001, SkinName = "Goth Annie" };
        Assert.Equal("正在导入皮肤资源 1/1：哥特萝莉 安妮", _names.TranslateStatus("正在导入皮肤资源 1/1：Goth Annie", [skin]));
    }
}
