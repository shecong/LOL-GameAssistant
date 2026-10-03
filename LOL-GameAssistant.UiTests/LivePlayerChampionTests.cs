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

    private static Form Mount(Control card)
    {
        var form = new Form { ClientSize = new Size(640, 520), Opacity = 0, ShowInTaskbar = false };
        form.Controls.Add(card);
        form.Show();
        return form;
    }

    private static Control Child(Control parent, string name) => parent.Controls.Find(name, true).Single();

    private static LivePlayerForm CreateCard(int id, Task<PlayerProfile?> profile,
        Func<int, Task<GameAsset?>> icons, bool ally = false) => new(
        "enemy", "对手", id, "", false, ally, true, 430, "CLASSIC",
        Service<IPlayerProfileService>((_, _) => profile),
        Service<IProfileIconService>((_, _) => Task.FromResult<byte[]?>(null)),
        Service<IMatchHistoryService>((_, _) => throw new InvalidOperationException("Profile is still pending.")),
        Service<IGameAssetService>((_, args) => icons((int)args[0]!)));

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

