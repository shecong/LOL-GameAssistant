using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaAnnouncementBuilderTests
{
    [Theory]
    [InlineData(RecentPerformanceLabel.Upper)]
    [InlineData(RecentPerformanceLabel.Medium)]
    [InlineData(RecentPerformanceLabel.Lower)]
    [InlineData(RecentPerformanceLabel.Human)]
    public void BuiltInRemarksVaryWithoutConsecutiveDuplicates(RecentPerformanceLabel label)
    {
        string? previous = null;
        var observed = new HashSet<string>();
        for (int index = 0; index < 40; index++)
        {
            string current = GameKdaRemarkLibrary.Select(label);
            Assert.NotEqual(previous, current);
            Assert.False(string.IsNullOrWhiteSpace(current));
            observed.Add(current);
            previous = current;
        }
        Assert.True(observed.Count > 1);
    }

    [Fact]
    public void CustomLinesReplaceBuiltInsAndExcludeDuplicates()
    {
        var custom = new Dictionary<string, string> { ["Lower"] = "一号句\n二号句\n一号句\n " };
        string first = GameKdaRemarkLibrary.Select(RecentPerformanceLabel.Lower, custom);
        string next = GameKdaRemarkLibrary.Select(RecentPerformanceLabel.Lower, custom);
        Assert.Contains(first, new[] { "一号句", "二号句" });
        Assert.Contains(next, new[] { "一号句", "二号句" });
        Assert.NotEqual(first, next);
        custom["Lower"] = "唯一句";
        Assert.Equal("唯一句", GameKdaRemarkLibrary.Select(RecentPerformanceLabel.Lower, custom));
        custom["Lower"] = "\n ";
        Assert.NotEqual("唯一句", GameKdaRemarkLibrary.Select(RecentPerformanceLabel.Lower, custom));
    }

    [Fact]
    public void CustomTemplateSubstitutesActualPlayerAndStats()
    {
        var assessment = new RecentModePerformanceAssessment(MatchPerformanceTier.Lower, 55, 19, 42, "", 2.19, true, RecentPerformanceLabel.Lower);
        var custom = new Dictionary<string, string> { ["Lower"] = "{name}|{team}|{position}|{kda}|{samples}|{score}|{label}|{winrate}%" };
        string message = GameKdaAnnouncementBuilder.BuildSharpPlayer(new("红方", "玩家甲", assessment), 5, custom);
        Assert.Contains("玩家甲|红方|5|2.19|19|55|下等马|42.0%", message);
        Assert.DoesNotContain("{", message);
    }

    [Theory]
    [InlineData(RecentPerformanceLabel.Upper, "收尾")]
    [InlineData(RecentPerformanceLabel.Medium, "到场")]
    [InlineData(RecentPerformanceLabel.Lower, "加戏")]
    [InlineData(RecentPerformanceLabel.Human, "复活倒计时")]
    public void IndividualSharpAssessmentUsesActualStatsAndLabel(RecentPerformanceLabel label, string expected)
    {
        var assessment = new RecentModePerformanceAssessment(MatchPerformanceTier.Lower, 55, 19, 42, "", 2.19, true, label);
        string message = GameKdaAnnouncementBuilder.BuildSharpPlayer(new("红方", "测试玩家", assessment), 5, new Dictionary<string, string> { [label.ToString()] = expected });
        Assert.StartsWith("测试玩家", message);
        Assert.DoesNotContain("【红5】", message);
        Assert.Contains("19场 KDA2.19", message);
        Assert.Contains(expected, message);
        Assert.DoesNotContain('\n', message);
        Assert.True(message.Length < 120);
    }

    [Fact]
    public void SharpAssessmentDoesNotInventEvaluationWithoutEnoughData()
    {
        string pending = GameKdaAnnouncementBuilder.BuildSharpPlayer(new("蓝方", "玩家", null), 1);
        Assert.Contains("战绩暂未获取", pending);
        var small = new RecentModePerformanceAssessment(MatchPerformanceTier.Lower, 50, 2, 0, "", 1.2, false, RecentPerformanceLabel.Human);
        string insufficient = GameKdaAnnouncementBuilder.BuildSharpPlayer(new("蓝方", "玩家", small), 1);
        Assert.Contains("2场 KDA1.20", insufficient);
        Assert.Contains("样本不足", insufficient);
        Assert.DoesNotContain("复活倒计时", insufficient);
    }

}
