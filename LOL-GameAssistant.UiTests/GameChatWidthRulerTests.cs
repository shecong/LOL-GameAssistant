using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameChatWidthRulerTests
{
    [Fact]
    public void RulersUseKnownLengthsAndContainNoSubmissionCharacters()
    {
        var messages = GameChatWidthRuler.BuildMessages();
        Assert.Equal(3, messages.Count);
        Assert.Equal(new[] { 120, 120, 119 }, messages.Select(message => message.Length));
        Assert.StartsWith("０１０２０３", messages[0]); Assert.EndsWith("５９６０", messages[0]);
        Assert.StartsWith("010203", messages[1]); Assert.EndsWith("5960", messages[1]);
        Assert.StartsWith("０１　０２　０３", messages[2]); Assert.EndsWith("３９　４０", messages[2]);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain('\n', message); Assert.DoesNotContain('\r', message);
            Assert.DoesNotContain("【", message);
        });
    }

    [Fact]
    public void ButtonSendsAllRulersThroughSelectedChannelWithoutPerCharacterMode() => MatchListScrollingTests.OnUiThread(() =>
    {
        IReadOnlyList<string>? sent = null; bool all = false, clipboard = false, perCharacter = true;
        using var editor = new QuickShoutForm(null!, (_, _, _, _, _) => throw new InvalidOperationException("Must send a batch"),
            (messages, sendToAll, useClipboard, eachCharacter, _) =>
            {
                sent = messages; all = sendToAll; clipboard = useClipboard; perCharacter = eachCharacter;
                return Task.FromResult(new GameShoutSendResult(true, "test", messages.Count));
            }, () => throw new InvalidOperationException("Must send rulers"), () => { });
        editor.LoadSettings(new AssistantSettings { QuickShoutSendToAll = true, QuickShoutUseClipboard = true, QuickShoutPerCharacter = true });
        var button = Descendants(editor).OfType<AntdUI.Button>().Single(control => control.Name == "TestGameChatWidth");
        button.PerformClick();
        Assert.Equal(GameChatWidthRuler.BuildMessages(), sent);
        Assert.True(all); Assert.True(clipboard); Assert.False(perCharacter);
        Assert.True(button.Enabled);
    });

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
