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
