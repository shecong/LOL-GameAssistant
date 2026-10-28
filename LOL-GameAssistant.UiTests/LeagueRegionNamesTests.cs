using System.Reflection;
using LOL.Lounge;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class LeagueRegionNamesTests
{
    [Theory]
    [InlineData("NJ100", "联盟一区")]
    [InlineData("GZ100", "联盟二区")]
    [InlineData("CQ100", "联盟三区")]
    [InlineData("TJ100", "联盟四区")]
    [InlineData("TJ101", "联盟五区")]
    [InlineData("LA1", "拉丁美洲北部")]
    [InlineData("LAS", "拉丁美洲南部")]
    [InlineData("ME1", "中东")]
    [InlineData("PBE1", "测试服（PBE）")]
    [InlineData("PBE", "测试服（PBE）")]
    [InlineData("TENCENT_PBE", "国服体验服")]
    [InlineData(" hn14_new ", "均衡教派")]
    [InlineData("tencent_wt3_new", "弗雷尔卓德")]
    [InlineData(" tencent_nj100 ", "联盟一区")]
    public void MergedPlatformsAndLegacyAliasesResolveWithoutGuessing(string code, string expected) =>
        Assert.Equal(expected, LoungeLcuReader.RegionName(code));

    [Fact]
    public void RiotPlatformInventoryAndPublishedLegacyCodesAreRecognized()
    {
        // Riot 的 Platform Routing Values，以及 Akari 的历史 LCU region 编号。
        string[] codes = ["BR1", "EUN1", "EUW1", "JP1", "KR", "LA1", "LA2", "NA1", "OC1", "TR1", "RU",
            "PH2", "SG2", "TH2", "TW2", "VN2", "PH1", "SG1", "TH1", "TW1", "VN1", "RU1", "OCE", "EUNE"];
        foreach (string code in codes) Assert.True(LeagueRegionNames.TryGetName(code, out _), code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("TENCENT")]
    [InlineData("AMERICAS")]
    [InlineData("ASIA")]
    [InlineData("TENCENT_NA1")]
    [InlineData("HN999_NEW")]
    [InlineData("NJ101")]
    public void UnspecifiedOrUnpublishedPlatformsStayUnknown(string? code)
    {
        Assert.False(LeagueRegionNames.TryGetName(code, out _));
        Assert.StartsWith("未知大区", LoungeLcuReader.RegionName(code));
    }

    [Theory]
    [InlineData("未知大区（NJ100）", "联盟一区")]
    [InlineData("未知大区(tencent_cq100)", "联盟三区")]
    [InlineData("NJ100", "联盟一区")]
    [InlineData("已连接 · 玩家 · 未知大区（TJ101） · 在线", "已连接 · 玩家 · 联盟五区 · 在线")]
    [InlineData("未知大区（TENCENT）", "地区待确认")]
    [InlineData("未知大区（FUTURE）", "地区待确认")]
    public void LegacyRegionLabelsUseTheExpandedCatalog(string text, string expected) =>
        Assert.Equal(expected, LoungeForm.NormalizeRegion(text));

    [Fact]
    public async Task LiveMergedPlatformWinsOverChatAndRefreshesWhenAccountChanges()
    {
        string account = "alice", platform = "NJ100";
        var lcu = Service<ILcuRequestSender>((_, args) => Task.FromResult<string?>((string)args[0]! switch
        {
            "/lol-summoner/v1/current-summoner" => account.Length == 0 ? "{}" : "{\"puuid\":\"" + account + "\",\"gameName\":\"Player\"}",
            "/lol-platform-config/v1/namespaces/LoginDataPacket/platformId" => "\"" + platform + "\"",
            "/lol-chat/v1/me" => "{\"platformId\":\"HN1\",\"availability\":\"chat\"}",
            _ => null
        }));
        var reader = new LoungeLcuReader(lcu);
        Assert.Equal("联盟一区", (await reader.ReadAsync(true, default)).Presence.Region);
        account = "bob"; platform = "TJ101";
        Assert.Equal("联盟五区", (await reader.ReadAsync(true, default)).Presence.Region);
        account = "";
        var loggedOut = (await reader.ReadAsync(true, default)).Presence;
        Assert.Empty(loggedOut.Platform);
        Assert.Empty(loggedOut.Puuid);
    }

    [Fact]
    public void LegacyVictoryAndMembersAreCorrectedWithoutRewritingChat() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
        var receive = typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var members = typeof(LoungeClient).GetMethod("UpdateMembers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        members.Invoke(client, [new LoungeMember[] { new("member", "Player", "未知大区（NJ100）", "在线") }]);
        receive.Invoke(client, [new LoungeMessage(1, "", "member", "Player", "未知大区（NJ100）", "victory",
            "（未知大区（NJ100））拿下了一场胜利！", DateTimeOffset.Now), false]);
        receive.Invoke(client, [new LoungeMessage(2, "", "member", "Player", "未知大区（NJ100）", "chat",
            "这段原文：未知大区（NJ100）", DateTimeOffset.Now), false]);
        using var page = new LoungeForm(client);
        using var host = new Form { ClientSize = new(1100, 700) };
        host.Controls.Add(page); host.Show();
        System.Windows.Forms.Application.DoEvents();
        var history = (AntdUI.Chat.ChatList)typeof(LoungeForm).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        string text = ChatViewAssertions.Text(history);
        Assert.Contains("（联盟一区）拿下了一场胜利", text);
        Assert.Contains("这段原文：未知大区（NJ100）", text);
        var table = (AntdUI.Table)typeof(LoungeForm).GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Assert.Contains("联盟一区", Assert.Single(((Array)table.DataSource!).Cast<object>()).ToString());
        using var recruitment = page.CreateRecruitmentDialog();
        var region = Assert.IsType<AntdUI.Select>(Assert.Single(recruitment.Controls.Find("Region", true)));
        Assert.Contains("联盟一区", region.Items.Cast<object>());
        Assert.Contains("联盟五区", region.Items.Cast<object>());
        Assert.Contains("教育网专区", region.Items.Cast<object>());
        Assert.Contains("中东", region.Items.Cast<object>());
        Assert.Equal(region.Items.Count, region.Items.Cast<object>().Distinct().Count());
    });
}
