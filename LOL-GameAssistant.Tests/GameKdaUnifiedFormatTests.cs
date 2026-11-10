using System.Globalization;
using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class GameKdaUnifiedFormatTests
{
    private static RecentModePerformanceAssessment Ready => new(MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);

    [Fact]
    public void BothLegacySettingsMergeBothTeamsWithBracketedPlayers()
    {
        var players = Enumerable.Range(1, 10).Select(index => new GameKdaPlayerSummary(
            index <= 5 ? "蓝方" : "红方", $"玩家{index}", index == 10 ? null : Ready)).ToArray();
        var lines = GameKdaAnnouncementBuilder.Build(players);
        string merged = string.Concat(lines);
        Assert.Contains(lines, message => message.Contains("玩家1：") && !message.Contains("红 "));
        Assert.Contains(lines, message => message.Contains("玩家6：") && !message.Contains("蓝 "));
        Assert.Equal(6, lines.Count);
        Assert.Equal(new[] { 2, 2, 1, 2, 2, 1 }, lines.Select(message => message.Count(c => c == '【')));
        Assert.Contains("玩家1：", lines[0]);
        Assert.Contains("玩家3：", lines[1]);
        Assert.Contains("玩家5：", lines[2]);
        Assert.Contains("玩家6：", lines[3]);
        Assert.Contains("玩家8：", lines[4]);
        Assert.Contains("玩家10：", lines[5]);
        Assert.Equal(lines, GameKdaAnnouncementBuilder.Build(players, true));
        for (int index = 0; index < players.Length; index++)
        {
            string selected = ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(index + 1, players[index].Assessment, players[index].DisplayName));
            string row = "【" + players[index].Team + " " + selected + "】";
            int start = merged.IndexOf(row, StringComparison.Ordinal);
            Assert.True(start >= 0);
            string message = Assert.Single(lines, message => message.Contains(row, StringComparison.Ordinal));
            Assert.Equal(0, ColumnAfter(message[..message.IndexOf(row, StringComparison.Ordinal)]));
            Assert.EndsWith("】", message);
        }
        Assert.DoesNotContain('\n', merged);
        Assert.DoesNotContain('\r', merged);
        Assert.DoesNotContain("【蓝 ", merged);
        Assert.DoesNotContain("【红 ", merged);
        Assert.StartsWith("【蓝方 玩家1：上等马88分｜KDA4.90｜胜率55%】", merged.TrimStart());
        Assert.EndsWith("【红方 玩家10：战绩暂未获取】", merged);
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
        Assert.Equal("【蓝方 " + ChampSelectKdaAnnouncementBuilder.FormatPlayer(new(1, Ready, name.Replace("\r\n", " "))) + "】", line.TrimStart());
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
            Assert.EndsWith("[全模式参考]】", line);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void BatchesKeepAtMostTwoPlayersAndNeverOmitAnyone()
    {
        var players = Enumerable.Range(1, 40).Select(index => new GameKdaPlayerSummary("蓝方", $"玩家{index}", Ready)).ToArray();
        var messages = GameKdaAnnouncementBuilder.Build(players);
        Assert.Equal(20, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.InRange(message.Count(c => c == '【'), 1, 2);
            Assert.DoesNotContain('\n', message);
        });
        foreach (var player in players)
            Assert.Single(messages, message => message.Contains(player.DisplayName + "：", StringComparison.Ordinal));
    }

    [Fact]
    public void NextPlayerStartsOnANewDisplayLineWithoutSubmittingAgain()
    {
        string message = Assert.Single(GameKdaAnnouncementBuilder.Build([new("蓝方", "甲", null), new("蓝方", "乙", Ready)]));
        Assert.StartsWith("【蓝方 甲：战绩暂未获取】", message);
        Assert.EndsWith("【蓝方 乙：上等马88分｜KDA4.90｜胜率55%】", message);
        Assert.Equal(0, ColumnAfter(message[..message.IndexOf("【蓝方 乙", StringComparison.Ordinal)]));
        Assert.Equal("【蓝方 甲：战绩暂未获取】" + new string('\u3000', 2) + " "
            + "【蓝方 乙：上等马88分｜KDA4.90｜胜率55%】", message);
        Assert.DoesNotContain('\n', message);
    }

    [Fact]
    public void ThirdPlayerStartsANewMessageAndTwoPlayersHaveNoEmptyRowBetweenThem()
    {
        var messages = GameKdaAnnouncementBuilder.Build([
            new("蓝方", "甲", null), new("蓝方", "乙", null), new("蓝方", "丙", null)]);
        Assert.Equal(2, messages.Count);
        string message = messages[0];
        Assert.Contains("【蓝方 丙：", messages[1]);
        foreach (string name in new[] { "甲", "乙" })
            Assert.Equal(0, ColumnAfter(message[..message.IndexOf($"【蓝方 {name}", StringComparison.Ordinal)]));
        Assert.Equal(1, RowAfter(message[..message.IndexOf("【蓝方 乙", StringComparison.Ordinal)]));
        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\r', message);
    }

    [Fact]
    public void EmptyRosterProducesNoMessages() => Assert.Empty(GameKdaAnnouncementBuilder.Build([]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuredLayoutKeepsTwoFullPlayersOnConsecutiveLinesWithinObservedByteBudget(bool allModes)
    {
        var assessment = Ready with { UsesAllModes = allModes, Kda = 123.4, WinRate = 100 };
        string message = Assert.Single(GameKdaAnnouncementBuilder.Build([
            new("蓝方", "六个汉字名字", assessment), new("蓝方", "另一玩家名字", assessment)]));
        int first = message.IndexOf('【');
        int second = message.IndexOf('【', first + 1);
        Assert.Equal(RowAfter(message[..first]) + 1, RowAfter(message[..second]));
        Assert.Equal(0, ColumnAfter(message[..second]));
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount("/all " + message), 1, 250);
        Assert.Equal(2, message.Count(character => character == '】'));
    }

    private static int ColumnAfter(string text)
    {
        int column = 0, lineWidth = 30;
        foreach (var rune in text.EnumerateRunes())
        {
            int width = rune.Value <= 0x7f ? 1 : 2;
            if (column + width > lineWidth) { column = 0; lineWidth = 70; }
            column += width;
            if (column == lineWidth) { column = 0; lineWidth = 70; }
        }
        return column;
    }

    private static int RowAfter(string text)
    {
        int column = 0, lineWidth = 30, row = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            int width = rune.Value <= 0x7f ? 1 : 2;
            if (column + width > lineWidth) { column = 0; lineWidth = 70; row++; }
            column += width;
            if (column == lineWidth) { column = 0; lineWidth = 70; row++; }
        }
        return row;
    }
}
