using LOL_GameAssistant.Infrastructure.Ai;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CloudRecommendationParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlainChineseAdviceKeepsTheFullReadableText()
    {
        string response = "【装备】先补核心输出\n建议：" + new string('稳', 700) + "。\n依据：游戏时间 19:29。";

        var card = Assert.Single(CloudRecommendationParser.Parse(response, Now));

        Assert.Equal(response, card.Body);
        Assert.DoesNotContain("{\"recommendations\"", card.Body);
    }

    [Fact]
    public void LegacyJsonAdviceKeepsTheFullBody()
    {
        string body = new string('稳', 400);
        string response = System.Text.Json.JsonSerializer.Serialize(new
        {
            recommendations = new[] { new { category = "装备", title = "先补核心输出", body, evidence = "19:29" } }
        });

        var card = Assert.Single(CloudRecommendationParser.Parse(response, Now));

        Assert.Equal(body, card.Body);
    }

    [Fact]
    public void TruncatedLegacyJsonRecoversCompletedAdviceWithoutShowingJson()
    {
        string response = """
            {"recommendations":[{"category":"装备","priority":"important","title":"先补核心输出","body":"可考虑先合成无尽之刃。","evidence":"19:29 暂无成装"},{"category":"对线","title":"补
            """;

        var card = Assert.Single(CloudRecommendationParser.Parse(response, Now));

        Assert.Equal("先补核心输出", card.Title);
        Assert.Equal("可考虑先合成无尽之刃。", card.Body);
        Assert.DoesNotContain("recommendations", card.Body);
    }

    [Fact]
    public void BrokenJsonWithoutCompleteAdviceGetsReadableError()
    {
        var card = Assert.Single(CloudRecommendationParser.Parse("{\"recommendations\":[{\"title\":\"补", Now));

        Assert.Contains("格式不完整", card.Body);
        Assert.DoesNotContain("{", card.Body);
    }
}
