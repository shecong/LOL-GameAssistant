using System.Globalization;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ChampSelectKdaFormatTests
{
    private static RecentModePerformanceAssessment Ready { get; } = new(
        MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);

    [Fact]
    public void MessageUsesFloorOrderAndCompactColumns()
    {
        string message = ChampSelectKdaAnnouncementBuilder.Build([
            new(3, Ready), new(1, Ready), new(2, Ready with { Label = RecentPerformanceLabel.Medium })]);
        string[] lines = message.Split('\n');
        Assert.Equal("【近期KDA】", lines[0]);
        Assert.Equal("1楼：上等马88分｜KDA4.90｜胜率55%", lines[1]);
        Assert.StartsWith("2楼：中等马", lines[2]);
        Assert.StartsWith("3楼：", lines[3]);
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("我方：", message);
    }

    [Fact]
    public void MissingResultsAndSmallSamplesNeverReceiveAConfidentRating()
    {
        string missing = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, null));
        Assert.Contains("战绩暂未获取", missing);
        string noKda = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(2,
            Ready with { SampleSize = 0, WinSampleSize = 12, HasEnoughSample = false }));
        Assert.Contains("无KDA样本", noKda);
        Assert.Contains("胜率55%", noKda);
        Assert.DoesNotContain("88分", noKda);
        string small = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(3,
            Ready with { SampleSize = 3, HasEnoughSample = false, WinSampleSize = 0 }));
        Assert.Contains("数据不足", small);
        Assert.Contains("胜率--", small);
        Assert.Contains("KDA4.90", small);
        Assert.Contains("数据不足(3场)", small);
        Assert.DoesNotContain("上等马", small);
    }

    [Theory]
    [InlineData(4.499, "4.49")]
    [InlineData(4.1, "4.10")]
    public void ReferenceScopeAndNumbersStayClearAcrossCultures(double kda, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string line = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, Ready with { Kda = kda, UsesAllModes = true }));
            Assert.Contains("KDA" + expected, line);
            Assert.Contains("胜率55%", line);
            Assert.Contains("全模式参考", line);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LongPlayerNamesStayCompactWithoutBreakingUnicodeOrAddingLines()
    {
        string line = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, Ready, "很长很长很长的昵称\n额外一行"));
        Assert.StartsWith("很长很长很…：", line);
        Assert.DoesNotContain('\n', line);
        Assert.True(line.Length <= 35);
        Assert.Equal("玩家甲：上等马88分｜KDA4.90｜胜率55%",
            ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, Ready, "玩家甲")));
    }

    [Fact]
    public void OldDefaultMigratesButCustomTemplatesKeepTheirWording()
    {
        var settings = new AssistantSettings { ChampSelectKdaAnnouncementTemplate = "【选人近期 KDA 评估】\r\n{allies}\r\n{enemies}" };
        settings.Normalize();
        Assert.Equal(ChampSelectKdaAnnouncementBuilder.DefaultTemplate, settings.ChampSelectKdaAnnouncementTemplate);
        string custom = "自定义标题\r\n{ALLIES}\r\n{enemies}\r\n结束";
        settings.ChampSelectKdaAnnouncementTemplate = custom;
        settings.Normalize();
        Assert.StartsWith("自定义标题", settings.ChampSelectKdaAnnouncementTemplate);
        string message = ChampSelectKdaAnnouncementBuilder.Build([new(1, Ready)], custom);
        Assert.StartsWith("自定义标题\n1楼：", message);
        Assert.EndsWith("\n结束", message);
        Assert.DoesNotContain("{", message);
        Assert.DoesNotContain("\n\n", message);
    }
}
