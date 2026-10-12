using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class MatchReplayTests
{
    [Fact]
    public void ButtonsUseCurrentGameIdAndRecoverAfterFailure() => MatchListScrollingTests.OnUiThread(() =>
    {
        var calls = new List<(string, long)>();
        var features = Service<IClientFeatureService>((method, args) =>
        {
            calls.Add((method.Name, (long)args[0]!));
            return Task.FromResult(ClientFeatureResult.Failure("下载回放失败"));
        });
        using var actions = new MatchReplayActions(123456789, features);
        foreach (Control button in actions.Controls)
            button.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
        Assert.Equal(new[] { ("DownloadReplayAsync", 123456789L), ("WatchReplayAsync", 123456789L) }, calls);
        Assert.All(actions.Controls.Cast<AntdUI.Button>(), button => { Assert.True(button.Enabled); Assert.False(button.Loading); });
    });

    [Fact]
    public void PendingRequestPreventsDuplicateActionsAndDisposalCancelsIt() => MatchListScrollingTests.OnUiThread(() =>
    {
        int calls = 0;
        CancellationToken token = default;
        var pending = new TaskCompletionSource<ClientFeatureResult>();
        var features = Service<IClientFeatureService>((_, args) =>
        {
            calls++;
            token = (CancellationToken)args[1]!;
            return pending.Task;
        });
        var actions = new MatchReplayActions(42, features);
        Task request = actions.RunAsync(false);
        Assert.All(actions.Controls.Cast<Control>(), button => Assert.False(button.Enabled));
        Assert.True(actions.RunAsync(true).IsCompletedSuccessfully);
        Assert.Equal(1, calls);
        actions.Dispose();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(ClientFeatureResult.Success("已开始下载回放。"));
        request.GetAwaiter().GetResult();
    });

    [Fact]
    public void InvalidGameIdDoesNotSubmitRequests() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var actions = new MatchReplayActions(0, Service<IClientFeatureService>((_, _) => throw new Exception("Unexpected request")));
        actions.RunAsync(false).GetAwaiter().GetResult();
        actions.RunAsync(true).GetAwaiter().GetResult();
        Assert.All(actions.Controls.Cast<Control>(), button => Assert.False(button.Enabled));
    });

    [Theory]
    [InlineData(340)]
    [InlineData(680)]
    [InlineData(1100)]
    public void ReplayButtonsFitBothCardLayouts(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var full = new MatchHistoryCard(new MatchDetail { GameId = 42 }, "viewer",
            Service<IGameAssetService>((_, _) => null), Service<IPremadeDetectionService>((_, _) => null));
        using var preview = new MatchHistoryPreviewCard(new MatchHistoryGame { GameId = 42 }, "viewer");
        foreach (Control card in new Control[] { full, preview })
        {
            card.Width = width;
            Control surface = card.Controls[0];
            MatchReplayActions replay = Assert.Single(surface.Controls.OfType<MatchReplayActions>());
            Assert.True(card.ClientRectangle.Contains(replay.Bounds));
            foreach (Control sibling in surface.Controls)
                if (sibling != replay) Assert.False(replay.Bounds.IntersectsWith(sibling.Bounds));
            Assert.All(replay.Controls.Cast<Control>(), button => Assert.True(replay.ClientRectangle.Contains(button.Bounds)));
        }
    });

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
