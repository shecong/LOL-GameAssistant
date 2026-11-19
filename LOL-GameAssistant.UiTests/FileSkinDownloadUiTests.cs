using System.Reflection;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class FileSkinDownloadUiTests
{
    [Theory]
    [InlineData("Dark", 1510, 815)]
    [InlineData("Light", 1100, 820)]
    public void DownloadShowsTotalsAndPercentAndCanCancelAndRestart(string theme, int width, int height) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(theme);
            var files = new PendingFiles();
            using var page = new FileSkinBrowserForm(files,
                Service<ISkinEngineCoordinator>((method, _) => method.ReturnType == typeof(bool) ? false : null),
                Service<IApplicationSettingsStore>((method, _) => method.Name == "Load" ? new AssistantSettings() : null),
                new Preferences(), new Artwork(), () => false);
            using var host = new Form { FormBorderStyle = FormBorderStyle.None, ClientSize = new(width, height), ShowInTaskbar = false };
            host.Controls.Add(page); host.Show(); UiTheme.Apply(page);
            var cards = Find<FlowLayoutPanel>(page, "FileSkinCards");
            PumpUntil(() => cards.Controls.Count == files.Catalog.Count);
            var download = Find<AntdUI.Button>(page, "FileSkinDownloadResources");
            download.PerformClick();
            var label = Find<AntdUI.Label>(page, "FileSkinResourceProgressText");
            var bar = Find<AntdUI.Progress>(page, "FileSkinResourceProgressBar");
            PumpUntil(() => label.Text?.Contains("12.5 / 200.0 MB") == true);
            Assert.Contains("%", label.Text);
            Assert.Equal(0.0625f, bar.Value);
            Assert.Equal("取消下载", download.Text); Assert.True(download.Enabled);
            Assert.False(Find<AntdUI.Button>(page, "FileSkinImportResources").Enabled);
            Assert.False(Find<AntdUI.Select>(page, "FileSkinResourceSource").Enabled);
            Assert.True(label.Visible); Assert.True(label.Parent!.ClientRectangle.Contains(label.Bounds));
            Assert.True(page.ClientRectangle.Contains(page.PointToClient(bar.PointToScreen(new Point(bar.Width / 2, bar.Height / 2)))));
            Assert.All(cards.Controls.OfType<FileSkinArtTile>(), tile => Assert.InRange(tile.Height, 160, 225));
            using (var bitmap = new Bitmap(host.Width, host.Height))
            {
                host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "file-skin-progress-" + theme + ".png"));
            }
            download.PerformClick();
            PumpUntil(() => files.Cancelled == 1 && download.Text == "下载资源");
            string cancelled = Find<AntdUI.Label>(page, "FileSkinStatus").Text ?? "";
            Assert.Contains("已取消", cancelled); Assert.Contains("断点", cancelled);
            files.LastProgress!.Report(new(200 * 1048576L, 200 * 1048576L));
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(cancelled, Find<AntdUI.Label>(page, "FileSkinStatus").Text);
            Assert.Equal(0.0625f, bar.Value);
            download.PerformClick();
            PumpUntil(() => files.Downloads == 2 && download.Text == "取消下载");
            Find<AntdUI.Button>(page, "FileSkinStop").PerformClick();
            PumpUntil(() => files.Cancelled == 2 && download.Text == "下载资源");
        }
        finally { UiTheme.SetMode(previous); }
    });

    [Theory]
    [InlineData(960, 240)]
    [InlineData(240, 960)]
    public void SkinArtworkKeepsAllFourEdgesVisible(int width, int height) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var tile = new FileSkinArtTile { Size = new(200, 220), Caption = "完整原画", Badge = "安妮" };
        tile.SetArtwork(EdgeImage(width, height));
        using var bitmap = new Bitmap(tile.Width, tile.Height); tile.DrawToBitmap(bitmap, tile.ClientRectangle);
        var colors = EdgeColors().Select(c => c.ToArgb()).ToHashSet();
        for (int y = 42; y < tile.Height - 62; y++)
            for (int x = 8; x < tile.Width - 8; x++) colors.Remove(bitmap.GetPixel(x, y).ToArgb());
        Assert.Empty(colors);
    });

    private static T Find<T>(Control parent, string name) where T : Control => parent.Controls.Find(name, true).OfType<T>().Single();
    private static Color[] EdgeColors() => [Color.FromArgb(250, 5, 150), Color.FromArgb(5, 250, 150), Color.FromArgb(150, 5, 250), Color.FromArgb(250, 150, 5)];
    private static Bitmap EdgeImage(int width, int height)
    {
        var image = new Bitmap(width, height); using var g = Graphics.FromImage(image); g.Clear(Color.FromArgb(50, 70, 100));
        var colors = EdgeColors();
        using var left = new SolidBrush(colors[0]); using var right = new SolidBrush(colors[1]);
        using var top = new SolidBrush(colors[2]); using var bottom = new SolidBrush(colors[3]);
        g.FillRectangle(left, 0, 0, width / 4, height); g.FillRectangle(right, width * 3 / 4, 0, width / 4, height);
        g.FillRectangle(top, width / 4, 0, width / 2, height / 4);
        g.FillRectangle(bottom, width / 4, height * 3 / 4, width / 2, height / 4);
        return image;
    }
    private sealed class Artwork : IFileSkinArtworkService
    {
        public Task<GameAsset?> GetChampionAsync(CslolSkinEntry entry, CancellationToken token) => Task.FromResult<GameAsset?>(null);
        public Task<GameAsset?> GetSkinAsync(CslolSkinEntry entry, CancellationToken token)
        {
            string? folder = Environment.GetEnvironmentVariable("FILE_SKIN_CAPTURE_ART");
            if (folder != null && File.Exists(Path.Combine(folder, entry.SkinId + ".jpg")))
                return Task.FromResult<GameAsset?>(new(File.ReadAllBytes(Path.Combine(folder, entry.SkinId + ".jpg"))));
            using var image = EdgeImage(960, 540); using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Task.FromResult<GameAsset?>(new(stream.ToArray()));
        }
    }
    private sealed class Preferences : IFileSkinPreferencesStore
    {
        public FileSkinPreferences Load() => new();
        public void Save(FileSkinPreferences value) { }
    }
    private sealed class PendingFiles : ICslolSkinService, IFileSkinResourceInstaller
    {
        public bool IsAvailable => true;
        public bool IsRunning => false;
        public int Downloads, Cancelled;
        public IProgress<FileSkinResourceDownloadProgress>? LastProgress;
        public event Action<string>? StatusChanged;
        public IReadOnlyList<CslolSkinEntry> Catalog { get; } = new[] { 1001, 1002, 1003, 1008, 1010, 1011 }.Select(id => new CslolSkinEntry
            { ChampionKey = "Annie", ChampionId = 1, SkinId = id, SkinNum = id % 1000, SkinName = "Annie Skin " + id, FileName = "skin/" + id }).ToArray();
        public Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken token = default) => Task.FromResult(Catalog);
        public Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries, bool ignoreConflict = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CslolApplyResult> StopAsync() => Task.FromResult(new CslolApplyResult { Ok = true });
        public Task ImportResourcesAsync(string archive, CancellationToken token = default) => throw new NotSupportedException();
        public Task<FileSkinResourceUpdate?> CheckForUpdatesAsync(CancellationToken token = default) => Task.FromResult<FileSkinResourceUpdate?>(
            new(new("26.1116.1", "", new string('a', 64), ["https://server.test/resource.zip"], 200 * 1048576L), "26.1109", true, true, "已检查在线资源版本"));
        public async Task DownloadResourcesAsync(CancellationToken token = default, bool preferGitHub = false, IProgress<FileSkinResourceDownloadProgress>? progress = null)
        {
            Downloads++; LastProgress = progress; progress?.Report(new(13107200, 200 * 1048576L));
            StatusChanged?.Invoke("正在下载资源");
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { Cancelled++; throw; }
        }
    }
}
