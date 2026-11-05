using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PalmRewardDirectoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(8));
    private static JObject Activity(int id, string game = "lol", string end = "2026-11-30 23:59:00", string url = "https://lol.qq.com/act/example/") => new()
    {
        ["taskId"] = id, ["title"] = "活动 " + id, ["gameid"] = game, ["intent"] = url,
        ["activityStartTime"] = "2026-11-01 00:00:00", ["activityEndTime"] = end
    };
    private static JObject Directory(params JObject[] rows) => new()
    {
        ["code"] = 0, ["data"] = new JObject { ["result"] = 0, ["next"] = "0",
            ["feedsInfo"] = new JArray(new JObject { ["feedNews"] = new JObject { ["body"] = new JArray(rows) } }) }
    };

    [Fact]
    public void DirectoryFiltersExpiredAndOtherGamesAndDoesNotImplyClaimEligibility()
    {
        var result = LcuClientFeatureService.ReadPalmDirectory(Directory(Activity(1), Activity(1), Activity(2, "lgame"), Activity(3, end: "2026-11-05 11:59:59")), Now);
        Assert.Contains("发现 1 项", result.Message); Assert.Contains("目录本身不包含账户领取状态", result.Message);
        Assert.Contains("https://lol.qq.com/act/example/", result.Message);
        Assert.DoesNotContain("活动 2", result.Message); Assert.DoesNotContain("活动 3", result.Message);
    }

    [Theory]
    [InlineData("https://lol.qq.com.evil.example/claim")]
    [InlineData("javascript:alert(1)")]
    [InlineData("qtpage://community_post?id=1")]
    [InlineData("https://account@lol.qq.com/claim")]
    public void UnsupportedIntentsAreNotPresentedAsOpenableOfficialLinks(string url)
    {
        var result = LcuClientFeatureService.ReadPalmDirectory(Directory(Activity(1, url: url)), Now);
        Assert.Contains("需要在掌盟内打开", result.Message); Assert.DoesNotContain(url, result.Message);
    }

    [Fact]
    public void IncompletePaginationAndInvalidRowsRemainUnconfirmed()
    {
        var root = Directory(Activity(1, end: "invalid")); root["data"]!["next"] = "opaque";
        var result = LcuClientFeatureService.ReadPalmDirectory(root, Now);
        Assert.Contains("不能判定已扫描全部", result.Message); Assert.Contains("1 项活动资料未确认", result.Message);
        Assert.Contains("未判定掌盟奖励已领完", result.Message);
        Assert.Throws<InvalidOperationException>(() => LcuClientFeatureService.ReadPalmDirectory(new JObject { ["code"] = -1 }, Now));
    }
}
