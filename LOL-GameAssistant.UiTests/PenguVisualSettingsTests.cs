using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;
namespace LOL_GameAssistant.UiTests;
public sealed class PenguVisualSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-visual-settings", Guid.NewGuid().ToString("N"));
    private PenguVisualSettingsService Service() => new(() => _root);
    [Fact]
    public void PersistsAllControlsAcrossInstancesAndChangesRevision()
    {
        Assert.True(Service().Read().Enabled);
        var saved = Service().Save(new() { ShowBadges = false, ShowPlayerStats = false, HideNavigation = true, TierMode = "aram", WallpaperUrl = "https://images.example/test.png" });
        Assert.Equal(saved, Service().Read());
        Assert.NotEqual(saved.Revision, Service().Save(saved with { Enabled = false }).Revision);
        Assert.False(Service().Read().Enabled);
    }
    [Theory]
    [InlineData("file:///C:/secret.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://unsplash.com/photos/vernazza-coastal-village-in-italy-cYrMQA7a3Wc")]
    public void InvalidLinksLeaveSavedSettingsUnchanged(string url)
    {
        var saved = Service().Save(new());
        Assert.Throws<InvalidOperationException>(() => Service().Save(saved with { WallpaperUrl = url }));
        Assert.Equal(saved, Service().Read());
    }
    [Fact]
    public void CopiesLocalImageAndRetainsItAfterSourceRemoved()
    {
        Directory.CreateDirectory(_root);
        string source = Path.Combine(_root, "source.bmp");
        using (var image = new System.Drawing.Bitmap(16, 16)) image.Save(source, System.Drawing.Imaging.ImageFormat.Bmp);
        var settings = Service().ImportWallpaper(source, new());
        Service().Save(settings);
        File.Delete(source);
        Assert.StartsWith("https://plugins/lol-game-assistant-visuals/wallpaper-", Service().Read().WallpaperUrl);
        string asset = Path.Combine(_root, PenguPluginService.PluginId, new Uri(settings.WallpaperUrl).Segments.Last());
        using var result = System.Drawing.Image.FromFile(asset);
        Assert.Equal(16, result.Width);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
