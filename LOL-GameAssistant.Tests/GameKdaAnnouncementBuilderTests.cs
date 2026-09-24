using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaAnnouncementBuilderTests
{
    [Fact]
    public void BuildsShortMessagesContainingBothTeamsAndEveryPlayer()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10)
            .Select(index => new GameKdaPlayerSummary(
                index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : assessment))
            .ToArray();

        IReadOnlyList<string> messages = GameKdaAnnouncementBuilder.Build(players);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.True(message.Length <= 300));
        for (int index = 1; index <= 10; index++)
            Assert.Contains($"玩家{index} ", string.Join("\n", messages));
        Assert.Contains("玩家10 近期KDA暂无可查", messages[1]);
    }
}
