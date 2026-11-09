using System.Globalization;
using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaUnifiedFormatTests
{
    private static RecentModePerformanceAssessment Ready => new(MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);

    [Fact]
    public void BothLegacySettingsMergeAllPlayersAndPadTheirDisplayRows()
    {
        var players = Enumerable.Range(1, 10).Select(index => new GameKdaPlayerSummary(
            index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : Ready)).ToArray();
        var lines = GameKdaAnnouncementBuilder.Build(players);
        string merged = Assert.Single(lines);
        Assert.Equal(lines, GameKdaAnnouncementBuilder.Build(players, true));
        for (int index = 0; index < players.Length; index++)
        {
            string selected = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(index + 1, players[index].Assessment, players[index].DisplayName));
            string row = (index < 5 ? "蓝 " : "红 ") + selected;
            int start = merged.IndexOf(row, StringComparison.Ordinal);
            Assert.True(start >= 0);
            Assert.Equal(0, ColumnAfter(merged[..start]));
        }
        Assert.DoesNotContain('\n', merged);
        Assert.DoesNotContain('\r', merged);
        Assert.StartsWith(new string('\u3000', 15) + "蓝 玩家1：上等马88分｜KDA4.90｜胜率55%", merged);
        Assert.EndsWith("红 玩家10：战绩暂未获取", merged);
    }

    [Fact]
    public void MissingKdaAndMissingWinRateRemainDistinct()
    {
        string empty = Assert.Single(GameKdaAnnouncementBuilder.Build([new("蓝方", "玩家", Ready with { SampleSize = 0, WinSampleSize = 20, HasEnoughSample = false })]));
        Assert.Contains("无KDA样本｜KDA--｜胜率55%", empty);
        string small = Assert.Single(GameKdaAnnouncementBuilder.Build([new("红方", "玩家", Ready with { SampleSize = 3, WinSampleSize = 0, HasEnoughSample = false })]));
        Assert.Contains("数据不足(3场)｜KDA4.90｜胜率--", small);
        Assert.DoesNotContain("88分", small);
    }

    [Theory]
    [InlineData("非常非常非常长的召唤师名字")]
    [InlineData("LongSummonerName0123456")]
    [InlineData("玩家一\r\n额外一行")]
    [InlineData("🧪🧪🧪🧪🧪🧪🧪测试")]
    public void NamesAreShortenedExactlyLikeChampionSelect(string name)
    {
        string line = Assert.Single(GameKdaAnnouncementBuilder.Build([new("蓝方", name, Ready)]));
        Assert.EndsWith("蓝 " + ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, Ready, name.Replace("\r\n", " "))), line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\ufffd', line);
        Assert.True(line.Length < 80);
    }

    [Theory]
    [InlineData(2.199, "2.19")]
    [InlineData(4.499, "4.49")]
    [InlineData(123.4, "123.40")]
    public void PrecisionAndFallbackScopeAreConsistentAcrossCultures(double value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string line = Assert.Single(GameKdaAnnouncementBuilder.Build([new("红方", "测试玩家", Ready with { Kda = value, UsesAllModes = true })]));
            Assert.Contains("KDA" + expected, line);
            Assert.Contains("胜率55%", line);
            Assert.EndsWith("[全模式参考]", line);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LongBatchesSplitOnlyAtInputLimitAndKeepAllPlayers()
    {
        var players = Enumerable.Range(1, 40).Select(index => new GameKdaPlayerSummary("蓝方", $"玩家{index}", Ready)).ToArray();
        var messages = GameKdaAnnouncementBuilder.Build(players);
        Assert.True(messages.Count > 1 && messages.Count < players.Length);
        Assert.All(messages, message => { Assert.InRange(message.Length, 1, 480); Assert.DoesNotContain('\n', message); });
        foreach (var player in players)
            Assert.Single(messages, message => message.Contains(player.DisplayName + "：", StringComparison.Ordinal));
    }

    [Fact]
    public void ShortFirstPlayerUsesTheFirstLineBeforePaddingTheNext()
    {
        string message = Assert.Single(GameKdaAnnouncementBuilder.Build([new("蓝方", "甲", null), new("红方", "乙", Ready)]));
        Assert.StartsWith("蓝 甲：战绩暂未获取", message);
        int next = message.IndexOf("红 乙：", StringComparison.Ordinal);
        Assert.True(next > 0);
        Assert.Equal(0, ColumnAfter(message[..next]));
    }

    private static int ColumnAfter(string text)
    {
        int column = 0, width = 30;
        foreach (var rune in text.EnumerateRunes())
        {
            int size = rune.Value <= 0x7f ? 1 : 2;
            if (column + size > width) { column = 0; width = 60; }
            column += size;
            if (column == width) { column = 0; width = 60; }
        }
        return column;
    }
}
