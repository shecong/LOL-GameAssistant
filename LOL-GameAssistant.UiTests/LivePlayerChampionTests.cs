using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.Ranked;
using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Players;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class LivePlayerChampionTests
{
    [Theory]
    [InlineData(360, false)]
    [InlineData(360, true)]
    [InlineData(450, false)]
    [InlineData(450, true)]
    public void CurrentChampionRemainsVisibleWithPremadeTagsWhileProfilePending(int width, bool ally) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            var pendingProfile = new TaskCompletionSource<PlayerProfile?>();
            var card = CreateCard(122, pendingProfile.Task, _ => Task.FromResult<GameAsset?>(null), ally);
            card.Width = width;
            using var form = Mount(card);
            card.SetPremadeGroup(2, ["玩家甲", "玩家乙"]);
            var champion = Child(card, "lblChampionNow");
            var icon = Child(card, "picCurrent");
            Assert.True(champion.Visible);
            Assert.True(icon.Visible);
            Assert.Contains("德莱厄斯", champion.Text);
            Assert.True(champion.Width >= 110);
            Assert.False(champion.Bounds.IntersectsWith(icon.Bounds));
            Assert.False(champion.Bounds.IntersectsWith(Child(card, "lblPremadeTag").Bounds));
            Assert.False(champion.Bounds.IntersectsWith(Child(card, "lblTeamTag").Bounds));
            Assert.True(champion.Parent!.ClientRectangle.Contains(champion.Bounds));
            Assert.False(pendingProfile.Task.IsCompleted);
        });

    [Fact]
    public void UnchangedRosterCanFillInPreviouslyUnknownChampion() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pendingProfile = new TaskCompletionSource<PlayerProfile?>();
        var card = CreateCard(0, pendingProfile.Task, _ => Task.FromResult<GameAsset?>(null));
        using var form = Mount(card);
        Assert.False(Child(card, "lblChampionNow").Visible);
        LiveGameForm.UpdateCurrentChampions(form, [("enemy", "对手", 22, "", false)]);
        Assert.True(Child(card, "lblChampionNow").Visible);
        Assert.Contains("艾希", Child(card, "lblChampionNow").Text);
        // A transient missing ID must not erase a confirmed champion.
        LiveGameForm.UpdateCurrentChampions(form, [("enemy", "对手", 0, "", false)]);
        Assert.Contains("艾希", Child(card, "lblChampionNow").Text);
        Assert.False(pendingProfile.Task.IsCompleted);
    });

    [Fact]
    public void OlderIconResponseDoesNotOverwriteNewChampion() => MatchListScrollingTests.OnUiThread(() =>
    {
        var oldIcon = new TaskCompletionSource<GameAsset?>();
        var newIcon = new TaskCompletionSource<GameAsset?>();
        var card = CreateCard(0, new TaskCompletionSource<PlayerProfile?>().Task,
            id => id == 22 ? oldIcon.Task : newIcon.Task);
        using var form = Mount(card);
        Task oldLoad = card.UpdateCurrentChampionAsync(22);
        Task newLoad = card.UpdateCurrentChampionAsync(122);
        newIcon.SetResult(Icon(Color.Blue));
        System.Windows.Forms.Application.DoEvents();
        Assert.True(newLoad.IsCompletedSuccessfully);
        var picture = (PictureBox)Child(card, "picCurrent");
        Image? newestImage = picture.Image;
        Assert.NotNull(newestImage);
        oldIcon.SetResult(Icon(Color.Red));
        System.Windows.Forms.Application.DoEvents();
        Assert.True(oldLoad.IsCompletedSuccessfully);
        Assert.Same(newestImage, picture.Image);
        Assert.Contains("德莱厄斯", Child(card, "lblChampionNow").Text);
    });

    [Fact]
    public void LoadedAllySurvivesSideChangeWithoutReloadAndReplaysAssessment() => MatchListScrollingTests.OnUiThread(() =>
    {
        string puuid = Guid.NewGuid().ToString();
        int historyRequests = 0;
        var card = new LivePlayerForm(puuid, "队友", 22, "", false, true, true, 430, "CLASSIC",
            Service<IPlayerProfileService>((_, _) => Task.FromResult<PlayerProfile?>(null)),
            Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
            Service<IMatchHistoryService>((_, _) =>
            {
                historyRequests++;
                return Task.FromResult<LOL_GameAssistant.Domain.Matches.MatchHistoryResponse?>(null);
            }),
            Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null)));
        var published = new List<PlayerRecentPerformanceEventArgs>();
        card.RecentPerformanceReady += (_, result) => published.Add(result);
        using var form = new Form { Opacity = 0, ShowInTaskbar = false };
        var blue = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 200 };
        var red = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 200 };
        form.Controls.Add(blue);
        form.Controls.Add(red);
        blue.Controls.Add(card);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        Assert.Single(published);
        Assert.Equal(1, historyRequests);
        Control existingContent = Child(card, "panelMatches").Controls[0];
        var reusable = new Dictionary<string, LivePlayerForm> { [puuid] = card };
        LiveGameForm.DetachReusablePlayerCards([(puuid, "队友", 122, "", false)],
            true, true, 430, "", reusable);
        Assert.Null(card.Parent);
        Assert.False(card.IsDisposed);
        red.Controls.Add(card);
        card.ReplayRecentPerformance();
        System.Windows.Forms.Application.DoEvents();
        Assert.Same(existingContent, Child(card, "panelMatches").Controls[0]);
        Assert.Equal(1, historyRequests);
        Assert.Equal(2, published.Count);
        Assert.Equal(published[0].Puuid, published[1].Puuid);
        Assert.Equal(published[0].Assessment, published[1].Assessment);
        Assert.True(published[1].IsAlly);
        Assert.False(card.CanReuseFor(450, "ARAM", true, true, false));
        Assert.False(card.CanReuseFor(430, "CLASSIC", false, true, false));
    });

    [Fact]
    public void IncompatibleCardRemainsAttachedForDisposal() => MatchListScrollingTests.OnUiThread(() =>
    {
        var card = CreateCard(22, new TaskCompletionSource<PlayerProfile?>().Task,
            _ => Task.FromResult<GameAsset?>(null), true);
        using var panel = new FlowLayoutPanel();
        panel.Controls.Add(card);
        LiveGameForm.DetachReusablePlayerCards([("enemy", "队友", 22, "", false)],
            true, true, 450, "ARAM", new Dictionary<string, LivePlayerForm> { ["enemy"] = card });
        Assert.Same(panel, card.Parent);
    });

    [Theory]
    [InlineData("EMERALD", "II", "翡翠二")]
    [InlineData("MASTER", "I", "超凡大师")]
    [InlineData("NONE", "", "未定级")]
    public void RankAppearsAfterNameWithoutChangingPlayerIdentityAndIsCached(string tier, string division, string expected) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            string puuid = Guid.NewGuid().ToString();
            int requests = 0;
            var overview = new RankedOverview();
            overview.Queues[RankedQueues.Solo5x5] = new RankedQueue
            { QueueType = RankedQueues.Solo5x5, Tier = tier, Division = division, LeaguePoints = 25 };
            var ranked = Service<IRankedStatsService>((_, _) =>
            {
                requests++;
                return Task.FromResult<RankedOverview?>(overview);
            });
            var profile = new TaskCompletionSource<PlayerProfile?>().Task;
            var card = CreateCard(22, profile, _ => Task.FromResult<GameAsset?>(null), true, ranked, puuid);
            card.Width = 384;
            using var form = Mount(card);
            var rank = Child(card, "lblRank");
            var name = Child(card, "lblName");
            Assert.True(rank.Visible);
            Assert.Equal(expected, rank.Text);
            Assert.Equal("对手", name.Text);
            Assert.True(rank.Left > name.Left);
            Assert.True(rank.Right <= Child(card, "picCurrent").Left);
            Assert.Equal(1, requests);
            var another = CreateCard(22, profile, _ => Task.FromResult<GameAsset?>(null), true, ranked, puuid);
            using var second = Mount(another);
            Assert.Equal(expected, Child(another, "lblRank").Text);
            Assert.Equal(1, requests);
        });

    private static Form Mount(Control card)
    {
        var form = new Form { ClientSize = new Size(640, 520), Opacity = 0, ShowInTaskbar = false };
        form.Controls.Add(card);
        form.Show();
        return form;
    }

    [Theory]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(450)]
    public void HeaderReservesSpaceForRankAndChampionWithoutActionButtons(int width) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            var overview = new RankedOverview();
            overview.Queues[RankedQueues.Solo5x5] = new RankedQueue
            { QueueType = RankedQueues.Solo5x5, Tier = "CHALLENGER", Division = "I" };
            var ranked = Service<IRankedStatsService>((_, _) => Task.FromResult<RankedOverview?>(overview));
            var card = CreateCard(122, new TaskCompletionSource<PlayerProfile?>().Task,
                _ => Task.FromResult<GameAsset?>(null), true, ranked, Guid.NewGuid().ToString());
            Child(card, "lblName").Text = "名称比较长的召唤师玩家";
            Child(card, "lblSummary").Text = "10场 · 胜率 100%";
            card.Width = width;
            using var form = Mount(card);
            card.SetPremadeGroup(2, ["玩家甲", "玩家乙"]);
            var rank = Child(card, "lblRank");
            var icon = Child(card, "picCurrent");
            Assert.Empty(icon.Parent!.Controls.OfType<AntdUI.Button>());
            Assert.Equal(new Size(36, 36), icon.Size);
            Assert.True(rank.Width >= TextRenderer.MeasureText(rank.Text, rank.Font).Width);
            Assert.True(rank.Right <= icon.Left);
            Assert.False(rank.Bounds.IntersectsWith(Child(card, "lblName").Bounds));
            Assert.False(icon.Bounds.IntersectsWith(Child(card, "lblTeamTag").Bounds));
            Assert.False(icon.Bounds.IntersectsWith(Child(card, "lblPremadeTag").Bounds));
            Assert.True(icon.Parent.ClientRectangle.Contains(icon.Bounds));
        });

    [Fact]
    public void RefreshKeepsCompatibleCardsButRejectsChangedContext() => MatchListScrollingTests.OnUiThread(() =>
    {
        var card = CreateCard(22, new TaskCompletionSource<PlayerProfile?>().Task,
            _ => Task.FromResult<GameAsset?>(null));
        using var form = Mount(card);
        List<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> members =
            [("enemy", "对手", 122, "", false)];
        Assert.True(LiveGameForm.CanKeepPlayerCards(form, members, "me", 430, "CLASSIC"));
        Assert.False(LiveGameForm.CanKeepPlayerCards(form, members, "enemy", 430, "CLASSIC"));
        Assert.False(LiveGameForm.CanKeepPlayerCards(form, members, "me", 2400, "KIWI"));
        members[0] = ("new-player", "新玩家", 122, "", false);
        Assert.False(LiveGameForm.CanKeepPlayerCards(form, members, "me", 430, "CLASSIC"));
    });

    [Fact]
    public void HoverDoesNotRestartAndReversalContinuesFromCurrentBrightness() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var card = CreateCard(0, new TaskCompletionSource<PlayerProfile?>().Task,
            _ => Task.FromResult<GameAsset?>(null));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var start = typeof(LivePlayerForm).GetMethod("StartGlow", flags)!;
        var tick = typeof(LivePlayerForm).GetMethod("GlowTick", flags)!;
        var alpha = typeof(LivePlayerForm).GetField("_glowAlpha", flags)!;
        var progress = typeof(LivePlayerForm).GetField("_glowT", flags)!;
        start.Invoke(card, [true]);
        tick.Invoke(card, null);
        double before = (double)alpha.GetValue(card)!;
        object elapsed = progress.GetValue(card)!;
        start.Invoke(card, [true]);
        Assert.Equal(elapsed, progress.GetValue(card));
        start.Invoke(card, [false]);
        Assert.Equal(before, (double)alpha.GetValue(card)!);
        tick.Invoke(card, null);
        Assert.InRange((double)alpha.GetValue(card)!, 0, before);
        for (int i = 0; i < 10; i++) tick.Invoke(card, null);
        Assert.Equal(0, (double)alpha.GetValue(card)!);
    });

    private static Control Child(Control parent, string name) => parent.Controls.Find(name, true).Single();

    private static LivePlayerForm CreateCard(int id, Task<PlayerProfile?> profile,
        Func<int, Task<GameAsset?>> icons, bool ally = false, IRankedStatsService? ranked = null, string puuid = "enemy") => new(
        puuid, "对手", id, "", false, ally, true, 430, "CLASSIC",
        Service<IPlayerProfileService>((_, _) => profile),
        Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
        Service<IMatchHistoryService>((_, _) => throw new InvalidOperationException("Profile is still pending.")),
        Service<IGameAssetService>((_, args) => icons((int)args[0]!)), ranked);

    private static GameAsset Icon(Color color)
    {
        using var bitmap = new Bitmap(4, 4);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return new GameAsset(stream.ToArray());
    }

    private static T Service<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}

