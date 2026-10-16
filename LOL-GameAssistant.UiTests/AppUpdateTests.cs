using System.Reflection;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class AppUpdateTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0.0", false)]
    [InlineData("1.0.0.0", "1.0.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "2.0.0", false)]
    public void VersionsAreComparedNumericallyWithMissingSegmentsAsZero(string remote, string local, bool expected) =>
        Assert.Equal(expected, AppVersion.IsNewer(Version.Parse(remote), Version.Parse(local)));

    [Fact]
    public void NewVersionOffersBothSourcesAndCanBeDismissedWithoutDisablingOwner() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form { ShowInTaskbar = false };
        owner.Show();
        var urls = new List<string>();
        var release = new UpdateRelease(new Version(99, 0, 0), "v99.0.0",
            "https://github.com/shecong/LOL-GameAssistant/releases/tag/v99.0.0", "更新说明\n修复问题");
        var service = new StubService(() => Task.FromResult<UpdateRelease?>(release));
        AppUpdateChecker.CheckAsync(owner, service, automatic: true, openUrl: urls.Add).GetAwaiter().GetResult();
        using var prompt = Assert.Single(owner.OwnedForms.OfType<UpdateAvailableForm>());
        System.Windows.Forms.Application.DoEvents();
        Assert.True(owner.Enabled);
        Assert.False(prompt.Modal);
        Click(prompt.Controls.Find("DownloadUpdate", true).Single());
        Click(prompt.Controls.Find("GetUpdateFromQq", true).Single());
        Assert.Equal(new[] { release.ReleaseUrl, UpdateAvailableForm.QqGroupUrl }, urls);
        Assert.Contains("1067459719", urls[1]);
        var actions = prompt.Controls.Find("UpdateLater", true).Single().Parent!;
        foreach (Control button in actions.Controls)
            Assert.True(actions.ClientRectangle.Contains(button.Bounds), $"Button clipped: {button.Bounds}; actions: {actions.ClientRectangle}");
        AppUpdateChecker.CheckAsync(owner, service, automatic: false).GetAwaiter().GetResult();
        Assert.Single(owner.OwnedForms.OfType<UpdateAvailableForm>());
        Click(prompt.Controls.Find("UpdateLater", true).Single());
        Assert.True(prompt.IsDisposed);
        Assert.True(owner.Enabled);
    });

    [Fact]
    public void AutomaticCheckStaysQuietForSameOlderMissingAndFailedReleases() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form { ShowInTaskbar = false };
        owner.Show();
        foreach (var version in new[] { AppVersion.Current, new Version(0, 0, 1) })
        {
            var release = new UpdateRelease(version, version.ToString(), "https://github.com", "");
            AppUpdateChecker.CheckAsync(owner, new StubService(() => Task.FromResult<UpdateRelease?>(release)), true).GetAwaiter().GetResult();
            Assert.Empty(owner.OwnedForms);
        }
        AppUpdateChecker.CheckAsync(owner, new StubService(() => Task.FromResult<UpdateRelease?>(null)), true).GetAwaiter().GetResult();
        AppUpdateChecker.CheckAsync(owner, new StubService(() => throw new HttpRequestException("Offline")), true).GetAwaiter().GetResult();
        Assert.Empty(owner.OwnedForms);
        Assert.True(owner.Enabled);
    });

    [Fact]
    public void PendingCheckDoesNotBlockOrOpenPromptAfterOwnerCloses() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var owner = new Form { ShowInTaskbar = false };
        owner.Show();
        var pending = new TaskCompletionSource<UpdateRelease?>();
        Task check = AppUpdateChecker.CheckAsync(owner, new StubService(() => pending.Task), true);
        Assert.False(check.IsCompleted);
        Assert.True(owner.Enabled);
        owner.Close();
        pending.SetResult(new UpdateRelease(new Version(99, 0, 0), "v99.0.0", "https://github.com", ""));
        SecondPhaseRegressionTests.PumpUntil(() => check.IsCompleted);
        check.GetAwaiter().GetResult();
        Assert.DoesNotContain(System.Windows.Forms.Application.OpenForms.Cast<Form>(), form => form is UpdateAvailableForm);
    });

    private static void Click(Control control) => control.GetType()
        .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);

    [Fact]
    public void UpdateBadgesStayVisibleAfterDismissalAndAppearOnAboutPageOpenedLater() => MatchListScrollingTests.OnUiThread(() =>
    {
        AppUpdateChecker.SetAvailableRelease(null);
        using var owner = new Form { ClientSize = new Size(1000, 700), ShowInTaskbar = false };
        var header = new AntdUI.PageHeader { Dock = DockStyle.Top, Height = 36, Text = "sc", ShowIcon = true };
        UpdateBadge.AddToHeader(header);
        owner.Controls.Add(header);
        owner.Show();
        var headerBadge = header.Controls.Find("HeaderUpdateBadge", true).Single();
        Assert.False(headerBadge.Visible);
        var release = new UpdateRelease(new Version(99, 0, 0), "v99.0.0", "https://github.com", "");
        var service = new StubService(() => Task.FromResult<UpdateRelease?>(release));
        try
        {
            AppUpdateChecker.CheckAsync(owner, service, true).GetAwaiter().GetResult();
            Assert.True(headerBadge.Visible);
            Assert.True(header.ClientRectangle.Contains(headerBadge.Bounds));
            Assert.Single(owner.OwnedForms.OfType<UpdateAvailableForm>()).Close();
            Assert.True(headerBadge.Visible);

            using var about = new AboutForm(service) { Dock = DockStyle.Fill };
            owner.Controls.Add(about);
            System.Windows.Forms.Application.DoEvents();
            var aboutBadge = about.Controls.Find("AboutUpdateBadge", true).Single();
            Assert.True(aboutBadge.Visible);
            Click(aboutBadge);
            Assert.Single(owner.OwnedForms.OfType<UpdateAvailableForm>()).Close();

            AppUpdateChecker.CheckAsync(owner, new StubService(() => throw new HttpRequestException("Offline")), true).GetAwaiter().GetResult();
            Assert.True(headerBadge.Visible);
            Assert.True(aboutBadge.Visible);
            Click(headerBadge);
            Assert.Single(owner.OwnedForms.OfType<UpdateAvailableForm>()).Close();
        }
        finally
        {
            AppUpdateChecker.SetAvailableRelease(null);
        }
    });

    private sealed class StubService(Func<Task<UpdateRelease?>> getLatest) : IUpdateReleaseService
    {
        public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default) => getLatest();
    }
}
