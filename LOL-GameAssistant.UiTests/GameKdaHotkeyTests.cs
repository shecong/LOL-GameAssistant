using System.Reflection;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameKdaHotkeyTests
{
    [Fact]
    public void ManualKdaWorksWithAutoAndPhraseHotkeysDisabled()
    {
        var settings = new AssistantSettings { GameKdaAnnouncementEnabled = false, QuickShoutHotkeysEnabled = false };
        Assert.Equal(QuickShoutHotkeyAction.GameKda, Assert.Single(Bindings(settings)).Value);
        Assert.True(Bindings(settings).ContainsKey(Keys.F9));
        settings.GameKdaHotkeyEnabled = false;
        Assert.Empty(Bindings(settings));
    }

    [Theory]
    [InlineData("F6", "Oem3")]
    [InlineData("F9", "F9")]
    public void ConflictingKeysNeverDispatchAnotherAction(string kdaKey, string holdKey)
    {
        var settings = new AssistantSettings { GameKdaHotkey = kdaKey, HoldToTopHotkey = holdKey };
        Assert.True(WindowHoldController.HasGameHotkeyConflict(settings));
        Assert.DoesNotContain(Bindings(settings), pair => pair.Key == Enum.Parse<Keys>(kdaKey));
        Assert.Equal(QuickShoutHotkeyAction.RandomCustom, Bindings(settings)[Keys.F7]);
    }

    [Fact]
    public void CustomKdaKeyCanReuseDisabledPhraseKey()
    {
        var settings = new AssistantSettings { QuickShoutHotkeysEnabled = false, GameKdaHotkey = "F6" };
        Assert.False(WindowHoldController.HasGameHotkeyConflict(settings));
        Assert.Equal(QuickShoutHotkeyAction.GameKda, Bindings(settings)[Keys.F6]);
    }

    [Fact]
    public void OldAndSavedSettingsPreserveHotkeyDefaultsAndCustomization()
    {
        var old = JsonConvert.DeserializeObject<SettingConfig>("{\"gameKdaOnePlayerPerLine\":true,\"gameKdaAnnouncementEnabled\":false}")!;
        Assert.True(old.GameKdaHotkeyEnabled);
        Assert.Equal("F9", old.GameKdaHotkey);
        Assert.True(old.GameKdaOnePlayerPerLine);
        Assert.False(old.GameKdaAnnouncementEnabled);
        var mapper = typeof(WindowHoldController).Assembly.GetType("LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper")!;
        var domain = new AssistantSettings { GameKdaHotkeyEnabled = false, GameKdaHotkey = "F10" };
        var saved = (SettingConfig)mapper.GetMethod("ToLegacy")!.Invoke(null, [domain])!;
        var restored = JsonConvert.DeserializeObject<SettingConfig>(JsonConvert.SerializeObject(saved))!;
        var result = (AssistantSettings)mapper.GetMethod("ToDomain")!.Invoke(null, [restored])!;
        Assert.False(result.GameKdaHotkeyEnabled);
        Assert.Equal("F10", result.GameKdaHotkey);
    }

    [Fact]
    public void HotkeyEditorKeepsManualAndAutomaticSettingsIndependent() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var editor = new QuickShoutForm(null!, (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)),
            (_, _, _, _, _) => Task.FromResult(default(GameShoutSendResult)),
            () => Task.FromResult(default(GameShoutSendResult)), () => { });
        var settings = new AssistantSettings { GameKdaHotkey = "F11", GameKdaHotkeyEnabled = true, GameKdaAnnouncementEnabled = false };
        editor.LoadSettings(settings);
        var saved = new AssistantSettings();
        editor.WriteSettings(saved);
        Assert.True(saved.GameKdaHotkeyEnabled);
        Assert.Equal("F11", saved.GameKdaHotkey);
        Assert.False(saved.GameKdaAnnouncementEnabled);
    });

    private static Dictionary<Keys, QuickShoutHotkeyAction> Bindings(AssistantSettings settings) =>
        (Dictionary<Keys, QuickShoutHotkeyAction>)typeof(WindowHoldController)
            .GetMethod("CreateGameHotkeyBindings", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [settings])!;
}
