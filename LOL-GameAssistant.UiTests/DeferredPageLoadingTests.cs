using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Domain.Ranked;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class DeferredPageLoadingTests
{
    [Fact]
    public void AntdTabsOnlyCreatePageAfterItIsSelected() => MatchListScrollingTests.OnUiThread(() =>
    {
        int creates = 0;
        using var host = new Form { ShowInTaskbar = false, ClientSize = new Size(800, 500) };
        var tabs = new AntdUI.Tabs { Dock = DockStyle.Fill };
        var first = new AntdUI.TabPage { Text = "首页" };
        var second = new AntdUI.TabPage { Text = "设置" };
        second.Controls.Add(new DeferredPageHost(() => { creates++; return new Panel(); }));
        tabs.Controls.Add(first);
        tabs.Pages.Add(first);
        tabs.Controls.Add(second);
        tabs.Pages.Add(second);
        host.Controls.Add(tabs);
        host.Show();
        PumpMessages(120);
        Assert.Equal(0, creates);
        tabs.SelectedIndex = 1;
        PumpUntil(() => creates == 1);
        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 1;
        PumpMessages(100);
        Assert.Equal(1, creates);
    });

    [Fact]
    public void HiddenPageIsNotConstructedAndVisiblePageLoadsOnlyOnce() => MatchListScrollingTests.OnUiThread(() =>
    {
        int creates = 0;
        using var host = new Form { ShowInTaskbar = false };
        using var page = new DeferredPageHost(() => { creates++; return new Panel(); }) { Visible = false };
        host.Controls.Add(page);
        host.Show();
        PumpMessages(100);
        Assert.Equal(0, creates);
        page.Visible = true;
        Assert.Equal(0, creates);
        Assert.Contains(page.Controls.Cast<Control>(), control => control.Visible && control.Text.Contains("加载"));
        PumpUntil(() => creates == 1 && page.Controls.Count == 3);
        page.Visible = false;
        page.Visible = true;
        PumpMessages(100);
        Assert.Equal(1, creates);
    });

    [Fact]
    public void DisposingOrHidingBeforeTimerTickDoesNotConstructPage() => MatchListScrollingTests.OnUiThread(() =>
    {
        int creates = 0;
        using var host = new Form { ShowInTaskbar = false };
        var disposed = new DeferredPageHost(() => { creates++; return new Panel(); });
        host.Controls.Add(disposed);
        host.Show();
        disposed.Dispose();
        using var hidden = new DeferredPageHost(() => { creates++; return new Panel(); });
        host.Controls.Add(hidden);
        hidden.Visible = false;
        PumpMessages(100);
        Assert.Equal(0, creates);
    });

    [Fact]
    public void LoadedPageHasCurrentThemeBeforeItsLoadEvent() => MatchListScrollingTests.OnUiThread(() =>
    {
        string mode = UiTheme.Mode;
        try
        {
            UiTheme.SetMode("Dark");
            bool loaded = false;
            using var host = new Form { ShowInTaskbar = false };
            using var deferred = new DeferredPageHost(() =>
            {
                var page = new UserControl();
                page.Load += (_, _) =>
                {
                    Assert.True(page.BackColor.GetBrightness() < .2F);
                    loaded = true;
                };
                return page;
            });
            host.Controls.Add(deferred);
            host.Show();
            Assert.Equal(UiTheme.Palette.Surface, deferred.BackColor);
            PumpUntil(() => loaded);
        }
        finally { UiTheme.SetMode(mode); }
    });

    [Fact]
    public void FailedFactoryShowsRetryAndCanLoadSuccessfully() => MatchListScrollingTests.OnUiThread(() =>
    {
        int attempts = 0;
        using var host = new Form { ShowInTaskbar = false };
        using var page = new DeferredPageHost(() => ++attempts == 1
            ? throw new InvalidOperationException("test page failure") : new Panel());
        host.Controls.Add(page);
        host.Show();
        PumpUntil(() => page.Controls.OfType<Button>().Single().Visible);
        Assert.Contains("test page failure", page.Controls.OfType<Label>().Single().Text);
        page.Controls.OfType<Button>().Single().PerformClick();
        PumpUntil(() => attempts == 2 && page.Controls.Count == 3);
    });

    [Fact]
    public void HomeShowsProfileAndStartsAllIndependentRequestsBeforeTheyComplete() => MatchListScrollingTests.OnUiThread(() =>
    {
        var icon = new TaskCompletionSource<byte[]?>();
        var ranked = new TaskCompletionSource<RankedOverview?>();
        var history = new TaskCompletionSource<MatchHistoryResponse?>();
        var version = new TaskCompletionSource();
        int requests = 0;
        using var home = new HomeForm(Service<InfoMsgForm.IInfoMsgForm>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(Player("启动测试"))),
            Service<IProfileIconService>((_, _) => { requests++; return icon.Task; }),
            Service<ILeagueClientConnection>((_, _) => true),
            Service<IMatchHistoryService>((_, _) => { requests++; return history.Task; }),
            Service<IRankedStatsService>((_, _) => { requests++; return ranked.Task; }),
            Service<IGameDataVersionService>((_, _) => version.Task));
        Task loading = home.LoadPlayerAsync(null);
        PumpUntil(() => Field<AntdUI.Label>(home, "play_name").Text == "启动测试");
        Assert.Equal(0, requests);
        version.SetResult();
        PumpUntil(() => requests == 3);
        Assert.Equal("启动测试", Field<AntdUI.Label>(home, "play_name").Text);
        Assert.False(loading.IsCompleted);
        Assert.Contains(Field<Control>(home, "stackPanel1").Controls.Cast<Control>(), control => control.Text.Contains("加载"));
        icon.SetResult(null);
        ranked.SetResult(new RankedOverview());
        history.SetResult(new MatchHistoryResponse { Games = new MatchHistoryGames() });
        Pump(loading);
        Assert.Contains(Field<Control>(home, "stackPanel1").Controls.Cast<Control>(), control => control.Text.Contains("暂无"));
    });

    private static void PumpMessages(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(5);
        }
    }
}
