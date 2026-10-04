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
        Assert.StartsWith("【红5】测试玩家", message);
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

    [Fact]
    public void FiveLongRedNamesDoNotProduceAnExtraSinglePlayerMessage()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Upper, 100, 20, 55, "", 123.4, true, RecentPerformanceLabel.Upper);
        var players = Enumerable.Range(1, 10).Select(index => new GameKdaPlayerSummary(
            index <= 5 ? "蓝方" : "红方", $"玩家{index}非常长的名字", assessment)).ToArray();
        var messages = GameKdaAnnouncementBuilder.Build(players);
        Assert.Equal(2, messages.Count);
        Assert.StartsWith("【本局近期KDA·蓝方】", messages[0]);
        Assert.StartsWith("【本局近期KDA·红方】", messages[1]);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 120));
        foreach (var player in players)
            Assert.Contains($"玩家{Array.IndexOf(players, player) + 1}", messages[player.Team == "蓝方" ? 0 : 1]);
        Assert.Equal(10, GameKdaAnnouncementBuilder.Build(players, onePlayerPerLine: true).Count);
    }

    [Fact]
    public void MissingResultDiffersFromEmptySameModeSample()
    {
        var empty = new RecentModePerformanceAssessment(MatchPerformanceTier.Medium, 0, 0, 0, "");
        var players = new[]
        {
            new GameKdaPlayerSummary("蓝方", "未返回", null),
            new GameKdaPlayerSummary("红方", "无样本", empty)
        };
        foreach (bool onePerLine in new[] { false, true })
        {
            var messages = GameKdaAnnouncementBuilder.Build(players, onePerLine);
            Assert.Contains(messages, message => message.Contains("战绩暂未获取"));
            Assert.Contains(messages, message => message.Contains("同模式无样本"));
            Assert.DoesNotContain(messages, message => message.Contains("无战绩"));
        }
    }

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
        Assert.Equal("红 玩家10 战绩暂未获取", messages[9]);
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
        Assert.Contains("玩家一 ", messages[0]);
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
    public void DefaultKeepsBothTeamsWithinChatLength()
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
        Assert.Contains(messages, message => message.StartsWith("【本局近期KDA·红方】"));
        Assert.Contains(messages, message => message.Contains("玩家2"));
        Assert.Contains(messages, message => message.Contains("玩家10"));
        Assert.All(messages, message => Assert.True(message.Length <= 120));
        foreach (var player in players)
            Assert.Contains(messages, message => message.StartsWith($"【本局近期KDA·{player.Team}】") && message.Contains(player.DisplayName));
    }

    [Fact]
    public void LongNamesStayInOneMessagePerTeamWithoutDroppingRedPlayers()
    {
        var players = Enumerable.Range(1, 10).Select(index => new GameKdaPlayerSummary(
            index <= 5 ? "蓝方" : "红方", $"角色{index}很长很长很长的召唤师名字", null)).ToArray();
        var messages = GameKdaAnnouncementBuilder.Build(players);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 120));
        foreach (var player in players)
        {
            string marker = player.DisplayName[..player.DisplayName.IndexOf("很长", StringComparison.Ordinal)];
            Assert.Contains(messages, message => message.StartsWith($"【本局近期KDA·{player.Team}】") && message.Contains(marker));
            Assert.DoesNotContain(messages, message => !message.StartsWith($"【本局近期KDA·{player.Team}】") && message.Contains(marker + "很"));
        }
    }

    [Fact]
    public void InsufficientSamplesAreNotAnnouncedAsLowerTier()
    {
        var assessment = new RecentModePerformanceAssessment(
            MatchPerformanceTier.Medium, 75, 4, 50, "", 3.2, false,
            RecentPerformanceLabel.InsufficientData);
        var player = new GameKdaPlayerSummary("蓝方", "玩家", assessment);

        Assert.Contains("样本不足4/5 K3.2", Assert.Single(
            GameKdaAnnouncementBuilder.Build([player], onePlayerPerLine: true)));
        Assert.Contains("样本不足4/5 KDA3.20", Assert.Single(
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
