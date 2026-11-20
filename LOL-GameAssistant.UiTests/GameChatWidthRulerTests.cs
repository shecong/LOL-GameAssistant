using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameChatWidthRulerTests
{
    [Fact]
    public void ButtonInvokesCalibrationWithoutUsingManualSend() => MatchListScrollingTests.OnUiThread(() =>
    {
        int calibrations = 0;
        using var editor = new QuickShoutForm(null!,
            (_, _, _, _, _) => throw new InvalidOperationException("Unexpected manual send"),
            (_, _, _, _, _) => throw new InvalidOperationException("Unexpected batch send"),
            () => throw new InvalidOperationException("Unexpected chat test"), () => { },
            calibrateGameChatWidth: () =>
            {
                calibrations++;
                return Task.FromResult(new GameShoutSendResult(true, "test"));
            });
        var button = Descendants(editor).OfType<AntdUI.Button>().Single(control => control.Name == "TestGameChatWidth");
        button.PerformClick();
        Assert.Equal(1, calibrations);
        Assert.True(button.Enabled);
    });

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
