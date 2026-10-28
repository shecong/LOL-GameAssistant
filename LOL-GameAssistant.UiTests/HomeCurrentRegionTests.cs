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
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class HomeCurrentRegionTests
{
    [Theory]
    [InlineData("HN1", "艾欧尼亚")]
    [InlineData("NJ100", "联盟一区")]
    [InlineData("GZ100", "联盟二区")]
    [InlineData("CQ100", "联盟三区")]
    [InlineData("TJ100", "联盟四区")]
    [InlineData("TJ101", "联盟五区")]
    [InlineData("ME1", "中东")]
    public async Task PlatformMetadataDoesNotNeedMatchHistory(string platform, string name)
    {
        var lcu = Service<ILcuRequestSender>((method, args) =>
        {
            Assert.Equal("GetStringAsync", method.Name);
            Assert.Equal("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", args[0]);
            return Task.FromResult<string?>("\"" + platform + "\"");
        });
        Assert.Equal(name, await new LcuCurrentRegionService(lcu).GetCurrentRegionAsync());
    }

    [Fact]
    public async Task FallbackUsesCurrentAccountAndReturnsUnknownWhenLoggedOut()
    {
        var routes = new List<string>(); bool loggedIn = true;
        var lcu = Service<ILcuRequestSender>((_, args) =>
        {
            string route = (string)args[0]!; routes.Add(route);
            return Task.FromResult<string?>(route switch
            {
                "/lol-summoner/v1/current-summoner" => loggedIn ? "{\"puuid\":\"current-account\"}" : "{}",
                "/lol-chat/v1/me" => "{\"platformId\":\"HN10\"}", _ => null
            });
        });
        var service = new LcuCurrentRegionService(lcu);
        Assert.Equal("黑色玫瑰", await service.GetCurrentRegionAsync());
        loggedIn = false; routes.Clear(); Assert.Equal("未知大区", await service.GetCurrentRegionAsync());
        Assert.DoesNotContain(routes, p => p.Contains("match-history"));
    }

    [Fact]
    public void SlowRegionDoesNotBlockHistoryAndOldRegionCannotReplaceNewOne() => MatchListScrollingTests.OnUiThread(() =>
    {
        var first = new TaskCompletionSource<string>(); int requests = 0;
        var region = Service<ICurrentRegionService>((_, _) => ++requests == 1 ? first.Task : Task.FromResult("黑色玫瑰"));
        using var home = new HomeForm(Service<InfoMsgForm.IInfoMsgForm>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(Player("当前玩家"))),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
            Service<ILeagueClientConnection>((_, _) => true),
            Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchHistoryResponse?>(new() { Games = new() })),
            Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(null)),
            Service<IGameDataVersionService>((_, _) => Task.CompletedTask), regionService: region);
        Pump(home.LoadPlayerAsync(null)); Assert.False(first.Task.IsCompleted);
        Pump(home.LoadPlayerAsync("queried-player"));
        Assert.Equal("黑色玫瑰", Field<AntdUI.Label>(home, "_regionName").Text);
        first.SetResult("艾欧尼亚"); System.Windows.Forms.Application.DoEvents();
        Assert.Equal("黑色玫瑰", Field<AntdUI.Label>(home, "_regionName").Text);
        using var window = new Form { ClientSize = new(1500, 820) }; home.Dock = DockStyle.Fill; window.Controls.Add(home); window.Show();
        System.Windows.Forms.Application.DoEvents();
        var panel = Field<AntdUI.Panel>(home, "_regionPanel"); Assert.True(panel.Right <= panel.Parent!.ClientSize.Width);
        string path = Path.Combine(AppContext.BaseDirectory, "home-current-region.png");
        using var image = new Bitmap(window.Width, window.Height); window.DrawToBitmap(image, new Rectangle(Point.Empty, window.Size)); image.Save(path);
        window.Close();
    });
}
