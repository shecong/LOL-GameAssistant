using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

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
            return requests.Count == 1 ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }));
        await service.DownloadResourcesAsync();
        Assert.Equal(new[] { "server.test", "github.com" }, requests);
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
        public void Configure(byte[] zip) => File.WriteAllText(Path.Combine(Root, "source.json"), JsonSerializer.Serialize(new
        {
            fileSkinResources = new { urls = new[] { "https://server.test/resources.zip", "https://github.com/resources.zip" }, sha256 = Convert.ToHexString(SHA256.HashData(zip)) }
        }));
        public string Archive(bool corrupt = false, string? extra = null, bool omitHash = false)
        {
            string path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            var files = new Dictionary<string, byte[]>
            {
                ["Data/Skins/skins.plskin"] = [1, 2, 3],
                ["Data/Skins/manifest.json"] = Encoding.UTF8.GetBytes("[{\"ChampionKey\":\"Annie\",\"SkinName\":\"test\",\"FileName\":\"Annie/test\",\"Size\":1,\"Sha256\":\"" + new string('a', 64) + "\"}]"),
                ["Tools/Cslol/mod-tools.exe"] = [1], ["Tools/Cslol/cslol-dll.dll"] = [2], ["Tools/Cslol/hashes.game.txt"] = [3]
            };
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
