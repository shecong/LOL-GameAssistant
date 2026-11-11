using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.Tests;

public class GameChatWidthCalibrationTests
{
    private const string Probe = "0123456789264810357968520914375913086247852149076349861302574927041863598014627539860415720394726185306948201753695284016374189532076";

    private static string[] Rows() => ["[12:34] 玩家123：" + Probe[..15], Probe[15..50], Probe[50..85], Probe[85..120], Probe[120..]];

    [Fact]
    public void MeasuresFirstAndRepeatedContinuationRowsWithSenderDigits()
    {
        Assert.Equal(new GameChatLineWidths(30, 70), GameChatWidthCalibration.Measure(Probe, Rows()));
    }

    [Fact]
    public void HandlesFullWidthDigitsAndOcrWhitespace()
    {
        string full = string.Concat(Probe.Select(c => (char)(c + 0xfee0)));
        var rows = Rows().Select(row => string.Join(" ", row.ToCharArray())).ToArray();
        Assert.Equal(new GameChatLineWidths(30, 70), GameChatWidthCalibration.Measure(full, rows));
    }

    [Fact]
    public void RejectsMissingRowAndUnequalContinuationWidths()
    {
        var rows = Rows();
        Assert.Null(GameChatWidthCalibration.Measure(Probe, [rows[0], rows[2], rows[3], rows[4]]));
        Assert.Null(GameChatWidthCalibration.Measure(Probe, [Probe[..15], Probe[15..50], Probe[50..84], Probe[84..120]]));
    }

    [Fact]
    public void RejectsCorruptAndStaleProbe()
    {
        var rows = Rows();
        rows[1] = "99999999999999999999999";
        Assert.Null(GameChatWidthCalibration.Measure(Probe, rows));
        Assert.Null(GameChatWidthCalibration.Measure(new string('７', 240), Rows()));
    }

    [Fact]
    public void BuilderUsesCalibratedWidthAndFallsBackForInvalidValues()
    {
        var players = new[] { new GameKdaPlayerSummary("蓝方", "玩家", null) };
        var defaults = GameKdaAnnouncementBuilder.Build(players).Single();
        Assert.Equal(defaults, GameKdaAnnouncementBuilder.Build(players, lineWidths: new(-1, 0)).Single());
        var calibrated = GameKdaAnnouncementBuilder.Build(players, lineWidths: new(8, 60)).Single();
        Assert.NotEqual(defaults, calibrated);
        Assert.StartsWith(new string('\u3000', 4), calibrated);
    }
}

