using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaAnnouncementBuilderTests
{
    [Fact]
    public void BuildsOneMessagePerPlayerInTeamOrder()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10)
            .Select(index => new GameKdaPlayerSummary(
                index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : assessment))
            .ToArray();

        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build(players);

        Assert.Equal(10, messages.Count);
        Assert.All(messages, message => Assert.True(message.Length <= 300));
        for (int index = 1; index <= 10; index++)
        {
            Assert.StartsWith(index <= 5 ? "【蓝方近期KDA】" : "【红方近期KDA】", messages[index - 1]);
            Assert.Contains($"玩家{index} ", messages[index - 1]);
        }
        Assert.Contains("玩家10 近期KDA暂无可查", messages[9]);
    }

    [Fact]
    public void LongNamesStillProduceOneMessagePerPlayer()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10)
            .Select(index => new GameKdaPlayerSummary(
                index <= 5 ? "蓝方" : "红方",
                $"玩家{index:00}超长召唤师名字用于验证整队消息不拆分", assessment))
            .ToArray();

        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build(players);

        Assert.Equal(10, messages.Count);
        Assert.All(messages, message => Assert.True(message.Length <= 300));
        for (int index = 1; index <= 10; index++)
            Assert.Contains($"玩家{index:00}", messages[index - 1]);
    }

    [Fact]
    public void NameLineBreakCannotCreateExtraChatMessages()
    {
        var messages = GameKdaAnnouncementBuilder.Build([
            new GameKdaPlayerSummary("蓝方", "玩家一\r\n额外内容", null)
        ]);

        Assert.Single(messages);
        Assert.DoesNotContain('\n', messages[0]);
        Assert.Contains("玩家一 额外内容", messages[0]);
    }
}
