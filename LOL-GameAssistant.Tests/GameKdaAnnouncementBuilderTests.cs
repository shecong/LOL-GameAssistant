using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaAnnouncementBuilderTests
{
    [Fact]
    public void BuildsOneCompactMessagePerPlayerInTeamOrder()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10)
            .Select(index => new GameKdaPlayerSummary(
                index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : assessment))
            .ToArray();

        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build(players, onePlayerPerLine: true);

        Assert.Equal(10, messages.Count);
        for (int index = 1; index <= 10; index++)
        {
            Assert.StartsWith(index <= 5 ? "蓝 " : "红 ", messages[index - 1]);
            Assert.Contains($"玩家{index} ", messages[index - 1]);
            Assert.DoesNotContain('\n', messages[index - 1]);
            Assert.True(messages[index - 1].Length <= 32);
        }
        Assert.Equal("红 玩家10 无近期数据", messages[9]);
    }

    [Fact]
    public void LongNamesAreShortenedByDisplayWidth()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build([
            new GameKdaPlayerSummary("蓝方", "很长很长很长很长的召唤师", assessment),
            new GameKdaPlayerSummary("红方", "LongSummonerName0123456", assessment)
        ], onePlayerPerLine: true);

        Assert.Equal("蓝 很长很长很… 上等马88 K4.9", messages[0]);
        Assert.Equal("红 LongSummone… 上等马88 K4.9", messages[1]);
    }

    [Fact]
    public void NameLineBreakCannotCreateExtraChatMessages()
    {
        var messages = GameKdaAnnouncementBuilder.Build([
            new GameKdaPlayerSummary("蓝方", "玩家一\r\n额外内容", null)
        ], onePlayerPerLine: true);

        Assert.Single(messages);
        Assert.DoesNotContain('\n', messages[0]);
        Assert.Contains("玩家一 额外内容", messages[0]);
    }

    [Fact]
    public void ExtremeKdaKeepsWholeMessageWithinObservedLineBudget()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 100, 20, 55, "", 123.4, true, RecentPerformanceLabel.Upper);
        string message = Assert.Single(GameKdaAnnouncementBuilder.Build([
            new GameKdaPlayerSummary("蓝方", "很长很长很长的召唤师名字", assessment)
        ], onePlayerPerLine: true));

        int displayWidth = message.EnumerateRunes().Sum(rune => rune.Value <= 0x7f ? 1 : 2);
        Assert.True(displayWidth <= 30, message);
        Assert.EndsWith("上等马100 K123.4", message);
    }

    [Fact]
    public void DefaultKeepsOneSpacedMessagePerTeam()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10)
            .Select(index => new GameKdaPlayerSummary(
                index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : assessment))
            .ToArray();

        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build(players);

        Assert.Equal(2, messages.Count);
        Assert.StartsWith("【本局近期KDA·蓝方】", messages[0]);
        Assert.StartsWith("【本局近期KDA·红方】", messages[1]);
        Assert.Contains("      玩家2", messages[0]);
        Assert.Contains("玩家10 近期KDA暂无可查", messages[1]);
        Assert.All(messages, message => Assert.True(message.Length <= 300));
    }

    [Fact]
    public void InsufficientSamplesAreNotAnnouncedAsLowerTier()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Medium, 75, 7, 50, "", 3.2, false,
            RecentPerformanceLabel.InsufficientData);
        var player = new GameKdaPlayerSummary("蓝方", "玩家", assessment);

        Assert.Contains("样本不足7/8 K3.2", Assert.Single(
            GameKdaAnnouncementBuilder.Build([player], onePlayerPerLine: true)));
        Assert.Contains("样本不足7/8 KDA3.20", Assert.Single(
            GameKdaAnnouncementBuilder.Build([player])));
    }

    [Fact]
    public void SentKdaDoesNotRoundAcrossTierThresholds()
    {
        var lower = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Lower, 59, 20, 50, "", 2.199, true,
            RecentPerformanceLabel.Lower);
        var medium = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Medium, 84, 20, 50, "", 4.499, true,
            RecentPerformanceLabel.Medium);
        var players = new[]
        {
            new GameKdaPlayerSummary("蓝方", "玩家甲", lower),
            new GameKdaPlayerSummary("红方", "玩家乙", medium)
        };

        IReadOnlyList<string> onePerPlayer = GameKdaAnnouncementBuilder.Build(players, onePlayerPerLine: true);
        Assert.Contains("下等马59 K2.1", onePerPlayer[0]);
        Assert.Contains("中等马84 K4.4", onePerPlayer[1]);

        IReadOnlyList<string> onePerTeam = GameKdaAnnouncementBuilder.Build(players);
        Assert.Contains("下等马59分 KDA2.19", onePerTeam[0]);
        Assert.Contains("中等马84分 KDA4.49", onePerTeam[1]);
    }
}
