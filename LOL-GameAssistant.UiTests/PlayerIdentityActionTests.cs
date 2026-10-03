using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PlayerIdentityActionTests
{
    [Fact]
    public void SingleClickCopiesExactPlayerOnlyAfterDoubleClickWindow() => MatchListScrollingTests.OnUiThread(() =>
    {
        var copied = new List<string>();
        var queried = new List<string>();
        using var actions = new PlayerIdentityActions(() => "exact-puuid", () => null, copied.Add, queried.Add);
        actions.HandleMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        Assert.Empty(copied);
        actions.CompleteSingleClick();
        Assert.Equal(new[] { "exact-puuid" }, copied);
        Assert.Empty(queried);
    });

    [Fact]
    public void DoubleClickQueriesPlayerWithoutCopying() => MatchListScrollingTests.OnUiThread(() =>
    {
        var copied = new List<string>();
        var queried = new List<string>();
        using var actions = new PlayerIdentityActions(() => "enemy-puuid", () => null, copied.Add, queried.Add);
        actions.HandleMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        actions.HandleMouseDown(new MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0));
        actions.CompleteSingleClick();
        Assert.Empty(copied);
        Assert.Equal(new[] { "enemy-puuid" }, queried);
    });

    [Fact]
    public void UnknownPlayerAndRightClickDoNothing() => MatchListScrollingTests.OnUiThread(() =>
    {
        var actionsSeen = new List<string>();
        using var unknown = new PlayerIdentityActions(() => null, () => null, actionsSeen.Add, actionsSeen.Add);
        unknown.HandleMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        unknown.CompleteSingleClick();
        using var known = new PlayerIdentityActions(() => "player", () => null, actionsSeen.Add, actionsSeen.Add);
        known.HandleMouseDown(new MouseEventArgs(MouseButtons.Right, 2, 0, 0, 0));
        known.CompleteSingleClick();
        Assert.Empty(actionsSeen);
    });
}
