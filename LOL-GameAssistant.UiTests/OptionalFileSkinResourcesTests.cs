using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class OptionalFileSkinResourcesTests
{
    [Fact]
    public async Task ImportPersistsOutsideApplicationAndSurvivesRestart()
    {
        using var fixture = new Fixture();
        using (var service = fixture.Service())
        {
            Assert.False(service.IsAvailable);
            await service.ImportResourcesAsync(fixture.Archive());
            Assert.True(service.IsAvailable);
            Assert.Single(await service.LoadCatalogAsync());
        }
        using var restarted = fixture.Service();
        Assert.True(restarted.IsAvailable);
        Assert.Single(await restarted.LoadCatalogAsync());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "app", "Data")));
    }

    [Fact]
    public async Task CorruptReplacementKeepsPreviouslyInstalledResources()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        await service.ImportResourcesAsync(fixture.Archive());
        string before = File.ReadAllText(Path.Combine(fixture.Root, "cache", "current.txt"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportResourcesAsync(fixture.Archive(corrupt: true)));
        Assert.Equal(before, File.ReadAllText(Path.Combine(fixture.Root, "cache", "current.txt")));
        Assert.True(service.IsAvailable);
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "cache")));
    }

    [Fact]
    public async Task UnknownPathsAndMissingFilesAreRejected()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportResourcesAsync(fixture.Archive(extra: "../outside.exe")));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportResourcesAsync(fixture.Archive(omitHash: true)));
        Assert.False(service.IsAvailable);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "outside.exe")));
    }

    [Fact]
    public async Task CancellationAndActiveGameDoNotInstall()
    {
        using var fixture = new Fixture(); using var service = fixture.Service(gameRunning: () => true);
        string zip = fixture.Archive();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportResourcesAsync(zip));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportResourcesAsync(zip, new CancellationToken(true)));
        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task DownloadFallsBackFromServerToGitHubAndChecksHash()
    {
        using var fixture = new Fixture();
        byte[] zip = File.ReadAllBytes(fixture.Archive());
        fixture.Configure(zip);
        var requests = new List<string>();
        using var service = fixture.Service(handler: () => new Handler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return request.RequestUri!.Host == "server.test" ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }));
        await service.DownloadResourcesAsync();
        Assert.Equal(new[] { "server.test", "server.test", "github.com" }, requests);
        Assert.True(service.IsAvailable);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, "cache"), "*.download"));
    }

    [Fact]
    public async Task GitHubSelectionUsesGitHubBeforeServer()
    {
        using var fixture = new Fixture();
        byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip);
        var requests = new List<string>();
        using var service = fixture.Service(handler: () => new Handler(request =>
        {
            requests.Add(request.RequestUri!.Host);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }));
        await service.DownloadResourcesAsync(preferGitHub: true);
        Assert.Equal(new[] { "github.com" }, requests);
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public async Task ExistingBundledResourcesRemainUsableWithoutInstallation()
    {
        using var fixture = new Fixture();
        string app = Path.Combine(fixture.Root, "app");
        ZipFile.ExtractToDirectory(fixture.Archive(), app);
        using var service = fixture.Service();
        Assert.True(service.IsAvailable);
        Assert.Single(await service.LoadCatalogAsync());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "cache")));
    }

    [Fact]
    public async Task UntrustedDownloadDoesNotInstallOrLeaveArchive()
    {
        using var fixture = new Fixture();
        fixture.Configure(File.ReadAllBytes(fixture.Archive()));
        using var service = fixture.Service(handler: () => new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("wrong archive") }));
        await Assert.ThrowsAsync<IOException>(() => service.DownloadResourcesAsync());
        Assert.False(service.IsAvailable);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, "cache")));
    }

    [Fact]
    public async Task OnlineManifestChangesResourceWithoutChangingApplicationConfiguration()
    {
        using var fixture = new Fixture();
        byte[] zip = File.ReadAllBytes(fixture.Archive());
        fixture.Configure(zip, remote: true);
        string configuration = File.ReadAllText(Path.Combine(fixture.Root, "source.json"));
        string version = "26.1115.1";
        using var service = fixture.Service(handler: () => new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("manifest.json")
            ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { version, gamePatch = "16.22", sha256 = Convert.ToHexString(SHA256.HashData(zip)), urls = new[] { "https://github.com/resources.zip" }, size = zip.Length })) }
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
        var update = await service.CheckForUpdatesAsync();
        Assert.True(update!.CheckedOnline); Assert.True(update.UpdateAvailable); Assert.Equal(version, update.Release.Version);
        await service.DownloadResourcesAsync();
        update = await service.CheckForUpdatesAsync();
        Assert.False(update!.UpdateAvailable); Assert.Equal(version, update.InstalledVersion);
        // 新资源发布仅修改远程清单；此处不修改应用配置。
        version = "26.1115.2"; zip = File.ReadAllBytes(fixture.Archive(extraSkin: true));
        update = await service.CheckForUpdatesAsync();
        Assert.True(update!.UpdateAvailable); Assert.Equal(version, update.Release.Version);
        Assert.Equal(configuration, File.ReadAllText(Path.Combine(fixture.Root, "source.json")));
        await service.DownloadResourcesAsync();
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(fixture.Root, "cache")).Length);
    }

    [Fact]
    public async Task ManifestFailureUsesLastValidatedVersionWithoutPretendingCheckSucceeded()
    {
        using var fixture = new Fixture(); byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip, remote: true);
        bool offline = false;
        using var service = fixture.Service(handler: () => new Handler(_ => offline ? new(HttpStatusCode.ServiceUnavailable) :
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { version = "26.1115", gamePatch = "16.22", sha256 = Convert.ToHexString(SHA256.HashData(zip)), urls = new[] { "https://github.com/resources.zip" } })) }));
        Assert.True((await service.CheckForUpdatesAsync())!.CheckedOnline);
        offline = true;
        var cached = await service.CheckForUpdatesAsync();
        Assert.False(cached!.CheckedOnline); Assert.Equal("26.1115", cached.Release.Version);
        Assert.Contains("失败", cached.Message); Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task OlderMirrorCannotDowngradeKnownResourceVersion()
    {
        using var fixture = new Fixture(); byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip, remote: true);
        string version = "26.1115";
        using var service = fixture.Service(handler: () => new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        { version, gamePatch = "16.22", sha256 = Convert.ToHexString(SHA256.HashData(zip)), urls = new[] { "https://github.com/resources.zip" } })) }));
        Assert.True((await service.CheckForUpdatesAsync())!.CheckedOnline);
        version = "26.1109";
        var result = await service.CheckForUpdatesAsync();
        Assert.False(result!.CheckedOnline); Assert.Equal("26.1115", result.Release.Version);
    }

    [Fact]
    public async Task InstalledArchiveIsRecognizedAndCleanupKeepsTwoVersionsAndUnmanagedDirectories()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        string archive = fixture.Archive(); byte[] zip = File.ReadAllBytes(archive); fixture.Configure(zip);
        await service.ImportResourcesAsync(archive);
        Assert.False((await service.CheckForUpdatesAsync())!.UpdateAvailable);
        string unmanaged = Path.Combine(fixture.Root, "cache", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unmanaged);
        File.WriteAllText(Path.Combine(unmanaged, "user.txt"), "keep");
        await service.ImportResourcesAsync(archive); await service.ImportResourcesAsync(archive);
        var roots = Directory.GetDirectories(Path.Combine(fixture.Root, "cache"));
        Assert.Equal(3, roots.Length); Assert.True(File.Exists(Path.Combine(unmanaged, "user.txt")));
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public async Task AlreadyInstalledReleaseDoesNotRedownloadArchive()
    {
        using var fixture = new Fixture(); string archive = fixture.Archive(); byte[] zip = File.ReadAllBytes(archive); fixture.Configure(zip);
        using var service = fixture.Service(handler: () => new Handler(_ => throw new InvalidOperationException("already installed: no archive request expected")));
        await service.ImportResourcesAsync(archive);
        await service.DownloadResourcesAsync();
        Assert.True(service.IsAvailable);
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.Root, "cache")));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void ResourceUpdatePromptAndControlsFitPage(string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = LOL_GameAssistant.Helper.UiTheme.Mode;
        try
        {
            LOL_GameAssistant.Helper.UiTheme.SetMode(theme);
            using var fixture = new Fixture(); byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip, remote: true);
            using var service = fixture.Service(handler: () => new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { version = "26.1115", gamePatch = "16.22", sha256 = Convert.ToHexString(SHA256.HashData(zip)), urls = new[] { "https://github.com/resources.zip" } })) }));
            using var page = new LOL_GameAssistant.BaseViewForm.FileSkinBrowserForm(service,
                SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Skins.ISkinEngineCoordinator>((method, _) => method.ReturnType == typeof(bool) ? false : null),
                SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Settings.IApplicationSettingsStore>((method, _) => method.Name == "Load" ? new LOL_GameAssistant.Domain.Settings.AssistantSettings() : null),
                new FileSkinPreferencesStore(Path.Combine(fixture.Root, "preferences.json")),
                SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Skins.IFileSkinArtworkService>((_, _) => Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null)), () => false);
            using var host = new Form { ClientSize = new(1100,820), ShowInTaskbar = false };
            host.Controls.Add(page); host.Show(); LOL_GameAssistant.Helper.UiTheme.Apply(page);
            SecondPhaseRegressionTests.Pump(page.CheckResourceUpdatesAsync());
            var label = page.Controls.Find("FileSkinResourceUpdate", true).Single();
            Assert.Contains("26.1115", label.Text); Assert.Contains("可更新", label.Text);
            foreach (string name in new[] { "FileSkinCheckResourceUpdates", "FileSkinDownloadResources", "FileSkinImportResources" })
            {
                var button = page.Controls.Find(name, true).Single();
                Assert.True(button.Visible); Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds));
            }
            using var bitmap = new Bitmap(host.Width, host.Height); host.DrawToBitmap(bitmap, host.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "file-skin-resource-update-" + theme + ".png")); host.Close();
        }
        finally { LOL_GameAssistant.Helper.UiTheme.SetMode(previous); }
    });

    [Theory]
    [InlineData(0, "server.test")]
    [InlineData(1, "server.test")]
    [InlineData(2, "github.com")]
    public void SourceSelectionTriesRequestedHostBeforeFallback(int selected, string expectedHost) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var fixture = new Fixture(); byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip);
        var requested = new List<string>();
        using var service = fixture.Service(handler: () => new Handler(request =>
        {
            requested.Add(request.RequestUri!.Host);
            return new(HttpStatusCode.NotFound);
        }));
        using var page = new LOL_GameAssistant.BaseViewForm.FileSkinBrowserForm(service,
            SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Skins.ISkinEngineCoordinator>((method, _) => method.ReturnType == typeof(bool) ? false : null),
            SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Settings.IApplicationSettingsStore>((method, _) => method.Name == "Load" ? new LOL_GameAssistant.Domain.Settings.AssistantSettings() : null),
            new FileSkinPreferencesStore(Path.Combine(fixture.Root, "preferences.json")),
            SecondPhaseRegressionTests.Service<LOL_GameAssistant.Application.Skins.IFileSkinArtworkService>((_, _) => Task.FromResult<LOL_GameAssistant.Domain.GameData.GameAsset?>(null)), () => false);
        var source = (AntdUI.Select)page.Controls.Find("FileSkinResourceSource", true).Single();
        Assert.Equal(new[] { "自动选源", "服务器", "GitHub" }, source.Items.Cast<object>().Select(item => item.ToString()));
        source.SelectedIndex = selected;
        var installing = (Task)typeof(LOL_GameAssistant.BaseViewForm.FileSkinBrowserForm).GetMethod("InstallResourcesAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, [service, null])!;
        SecondPhaseRegressionTests.Pump(installing);
        Assert.Equal(expectedHost, requested.First());
        Assert.Equal(2, requested.Count);
        Assert.NotEqual(requested[0], requested[1]);
    });

    [Theory]
    [InlineData("26.1109", "26.1116.1")]
    [InlineData("26.1117.1", "26.1117.1")]
    public async Task CachePreservesNewestVersionWhenOnlineCheckFails(string cachedVersion, string expectedVersion)
    {
        using var fixture = new Fixture(); byte[] zip = File.ReadAllBytes(fixture.Archive()); fixture.Configure(zip, remote: true);
        string configPath = Path.Combine(fixture.Root, "source.json");
        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        config["fileSkinResources"]!["version"] = "26.1116.1";
        File.WriteAllText(configPath, config.ToJsonString());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "cache"));
        File.WriteAllText(Path.Combine(fixture.Root, "cache", "latest-resource.json"), JsonSerializer.Serialize(new
        {
            version = cachedVersion, gamePatch = "", sha256 = Convert.ToHexString(SHA256.HashData(zip)),
            urls = new[] { "https://server.test/resources.zip" }
        }));
        using var service = fixture.Service(handler: () => new Handler(_ => new(HttpStatusCode.NotFound)));
        var update = await service.CheckForUpdatesAsync();
        Assert.False(update!.CheckedOnline);
        Assert.Equal(expectedVersion, update.Release.Version);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "optional-skins-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public OptionalCslolSkinService Service(Func<bool>? gameRunning = null, Func<HttpMessageHandler>? handler = null) => new(
            Path.Combine(Root, "app"), Path.Combine(Root, "cache"), Path.Combine(Root, "work"),
            Path.Combine(Root, "source.json"), () => false, gameRunning ?? (() => false), handler);
        public void Configure(byte[] zip, bool remote = false) => File.WriteAllText(Path.Combine(Root, "source.json"), JsonSerializer.Serialize(new
        {
            fileSkinResources = new { version = "26.1109", gamePatch = "16.20", manifestUrls = remote ? new[] { "https://server.test/manifest.json" } : Array.Empty<string>(), urls = new[] { "https://server.test/resources.zip", "https://github.com/resources.zip" }, sha256 = Convert.ToHexString(SHA256.HashData(zip)) }
        }));
        public string Archive(bool corrupt = false, string? extra = null, bool omitHash = false, bool extraSkin = false)
        {
            string path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            var files = new Dictionary<string, byte[]>
            {
                ["Data/Skins/skins.plskin"] = [1, 2, 3],
                ["Data/Skins/manifest.json"] = Encoding.UTF8.GetBytes("[{\"ChampionKey\":\"Annie\",\"SkinName\":\"test\",\"FileName\":\"Annie/test\",\"Size\":1,\"Sha256\":\"" + new string('a', 64) + "\"}]"),
                ["Tools/Cslol/mod-tools.exe"] = [1], ["Tools/Cslol/cslol-dll.dll"] = [2], ["Tools/Cslol/hashes.game.txt"] = [3]
            };
            if (extraSkin) files["Data/Skins/skins.plskin"] = [4, 5, 6, 7];
            if (omitHash) files.Remove("Tools/Cslol/hashes.game.txt");
            if (extra != null) files[extra] = [4];
            var index = files.ToDictionary(file => file.Key, file => Convert.ToHexString(SHA256.HashData(file.Value)));
            if (corrupt) index["Data/Skins/skins.plskin"] = new string('0', 64);
            files["resource-files.json"] = JsonSerializer.SerializeToUtf8Bytes(index);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files) { using var stream = archive.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
