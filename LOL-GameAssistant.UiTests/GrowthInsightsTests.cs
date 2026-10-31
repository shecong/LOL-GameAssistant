using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GrowthInsightsTests
{
    [Fact]
    public void MissingResultsAndMetricsAreNotCountedAsZero()
    {
        var games = new[] { new GrowthMatch(1, 100, 1, "峡谷", 1200, true, 3, 6, null, null),
            new GrowthMatch(2, 200, 1, "峡谷", 1800, null, null, null, null, null) };
        string text = GrowthInsights.Overview(new(games, [], 0, "测试"));
        Assert.Contains("胜率 100%", text);
        Assert.Contains("伤害 0/2", text);
        Assert.Contains("伤害 —/分", text);
        Assert.Contains("最近 2 场", text);
    }

    [Fact]
    public void ChampionEvaluationRequiresFiveKnownResults()
    {
        Assert.Contains("观察期", GrowthInsights.ChampionStatus(new(1, 20, 1, 1, 100, 3, 5, 500, 1)));
        Assert.Contains("主练候选", GrowthInsights.ChampionStatus(new(1, 10, 10, 6, 60, 3, 5, 500, 1)));
        Assert.Contains("重点复盘", GrowthInsights.ChampionStatus(new(1, 10, 10, 3, 30, 3, 5, 500, 1)));
    }

    [Fact]
    public void ReviewBaselineExcludesOtherChampionsModesAndFutureGames()
    {
        var match = new GrowthMatch(10, 10000, 1, "峡谷", 1200, false, 2, 4, 400, 1);
        var history = new[] { match, match with { GameId = 1, CreatedAt = 1000 },
            match with { GameId = 2, CreatedAt = 2000, ChampionId = 2 },
            match with { GameId = 3, CreatedAt = 3000, Mode = "乱斗" },
            match with { GameId = 11, CreatedAt = 11000 } };
        string text = GrowthInsights.Review(match, history);
        Assert.Contains("同英雄同模式 1 场", text);
        Assert.Contains("下一局练习", text);
    }
}
