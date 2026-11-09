using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Entity;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class AssetDetailTests
{
    [Fact]
    public void GameMarkupBecomesReadableDescription()
    {
        Assert.Equal("获得100装备急速\n\n装备技能冷却缩减 & 提速。",
            AssetDetailToolTip.PlainText("获得<scaleBonus>100装备急速</scaleBonus><br><br><rules>装备技能冷却缩减%i:cooldown% &amp; 提速。</rules>"));
    }

    [Fact]
    public void MayhemDescriptionIsAvailableOffline()
    {
        Assert.Contains("100装备急速", AugmentCatalog.GetDescription(1002));
    }

    [Fact]
    public void OverlayDescriptionsKeepMarkupOutOfTheVisibleRow()
    {
        string row = AugmentOverlayText.Format("强化 50.0% · 123 场", "获得<scaleBonus>100装备急速</scaleBonus><br>冷却缩减 &amp; 提速。%i:cooldown%");
        Assert.Equal("强化 50.0% · 123 场\t获得100装备急速 冷却缩减 & 提速。", row);
        Assert.DoesNotContain('\n', row);
        Assert.DoesNotContain('<', row);
        Assert.Contains("暂未获取", AugmentOverlayText.Format("未知强化", null));
    }

    [Fact]
    public void MatchMappingPreservesSelectedRunesAndSkipsEmptySlots()
    {
        var detail = LegacyMatchReadModelMapper.ToDomain(new GameDetailModel.GameInfo
        {
            participants = [new GameDetailModel.ParticipantsItem
            {
                stats = new GameDetailModel.Stats { perk0 = 8005, perk2 = 9104, perk5 = 8014 }
            }]
        });
        Assert.Equal(new[] { 8005, 9104, 8014 }, detail!.participants[0].stats!.RuneIds);
    }
}
