using System.Reflection;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class ResourceOnlineLibraryTests
{
    [Fact]
    public void ConnectBrowseSearchAndDownloadUseTheChosenSourceAndDoNotMount() => MatchListScrollingTests.OnUiThread(() =>
    {
        var source = new Repository();
        using var window = new ResourceOnlineLibraryForm(source); window.Show();
        Assert.Equal(0, source.Lists);
        var access = Find<AntdUI.Input>(window, "ResourceAccessToken"); access.Text = "own-test-token"; Assert.True(access.UseSystemPasswordChar);
        Find<AntdUI.Button>(window, "ResourceConnect").PerformClick();
        Assert.Equal(1, source.Lists); Assert.Equal("own-test-token", source.Last!.AccessToken);
        var list = Find<AntdUI.Table>(window, "ResourceOnlineEntries"); Assert.Single((Array)list.DataSource!);
        list.SelectedIndex = 1;
        System.Windows.Forms.Application.DoEvents();
        var cell = list.CellRectangle(1, 0);
        var mouse = new MouseEventArgs(MouseButtons.Left, 2, cell.Left + 12, cell.Top + cell.Height / 2, 0);
        typeof(AntdUI.Table).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [mouse]);
        typeof(AntdUI.Table).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [mouse]);
        Assert.Equal(2, source.Lists); Assert.Equal(2, ((Array)list.DataSource!).Length);
        Find<AntdUI.Input>(window, "ResourceOnlineSearch").Text = "净化"; Assert.Single((Array)list.DataSource!);
        list.SelectedIndex = 1;
        Find<AntdUI.Button>(window, "ResourceDownload").PerformClick();
        Assert.Equal(1, source.Downloads); Assert.Single(window.DownloadedPaths);
        if (Environment.GetEnvironmentVariable("RESOURCE_ONLINE_CAPTURE") is { Length: > 0 } path)
        {
            access.Text = ""; // Never capture even test-only credentials.
            Find<AntdUI.Button>(window, "ResourceConnect").PerformClick();
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(0, 0, window.Width, window.Height)); bitmap.Save(path);
        }
        Find<AntdUI.Input>(window, "ResourceRepository").Text = "another/repository";
        Assert.Empty((Array)list.DataSource!); Find<AntdUI.Button>(window, "ResourceDownload").PerformClick(); Assert.Equal(1, source.Downloads);
    });
    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private sealed class Repository : IResourceSkinRepository
    {
        public int Lists, Downloads; public ResourceRepositoryOptions? Last;
        public Task<IReadOnlyList<ResourceRepositoryEntry>> ListAsync(ResourceRepositoryOptions options, string path, CancellationToken token = default)
        {
            Last = options; Lists++;
            return Task.FromResult<IReadOnlyList<ResourceRepositoryEntry>>(path.Length == 0 ? [new("暗夜猎手-薇恩", "暗夜猎手-薇恩", true, 0, "")] :
                [new("源计划：净化.wad.client", "暗夜猎手-薇恩/源计划：净化.wad.client", false, 10000000, ""), new("猎天使魔女.wad.client", "暗夜猎手-薇恩/猎天使魔女.wad.client", false, 9000000, "")]);
        }
        public Task<string> DownloadAsync(ResourceRepositoryOptions options, ResourceRepositoryEntry entry, IProgress<ResourceDownloadProgress>? progress = null, CancellationToken token = default)
        {
            Downloads++; return Task.FromResult(Path.Combine(Path.GetTempPath(), entry.Name));
        }
    }
}
