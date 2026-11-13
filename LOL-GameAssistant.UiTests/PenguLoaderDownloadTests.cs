using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguLoaderDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-pengu-download-tests", Guid.NewGuid().ToString("N"));
    private const string AssetUrl = "https://github.com/PenguLoader/PenguLoader/releases/download/v1.1.6/pengu-loader-v1.1.6.zip";

    private sealed class Handler(byte[] package, string url, string? digest, bool prerelease) : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsoluteUri == PenguLoaderDownloadService.ReleaseApi)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = "v1.1.6", draft = false, prerelease,
                    assets = new[] { new { name = "pengu-loader-v1.1.6.zip", browser_download_url = url, size = package.Length, digest } }
                })) });
            Assert.Equal(AssetUrl, request.RequestUri.AbsoluteUri);
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
        }
    }

    private static byte[] Package(string? extra = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var file = zip.CreateEntry("Pengu Loader.exe").Open()) file.Write(Encoding.ASCII.GetBytes("MZ-test-loader"));
            if (extra != null) using (var file = zip.CreateEntry(extra).Open()) file.WriteByte(1);
        }
        return output.ToArray();
    }

    [Fact]
    public async Task OfficialDownloadPreparesPortableLoaderAndReusesVersion()
    {
        var handler = new Handler(Package(), AssetUrl, null, false);
        using var client = new HttpClient(handler);
        var service = new PenguLoaderDownloadService(client, _root);
        Assert.Null(service.GetInstalled());
        var result = await service.DownloadAsync();
        Assert.Equal("v1.1.6", result.Version);
        Assert.True(File.Exists(result.ExecutablePath));
        Assert.True(Directory.Exists(result.PluginsDirectory));
        Assert.Equal(result, service.GetInstalled());
        Assert.Equal(result, await service.DownloadAsync());
        Assert.Equal(1, handler.Downloads);
        Assert.Empty(Directory.EnumerateDirectories(_root, ".download-*"));
    }

    [Theory]
    [InlineData("https://untrusted.example/loader.zip", null, false)]
    [InlineData(AssetUrl, "sha256:0000000000000000000000000000000000000000000000000000000000000000", false)]
    [InlineData(AssetUrl, null, true)]
    public async Task RejectsForeignAssetsBadDigestAndPreviewReleases(string url, string? digest, bool prerelease)
    {
        using var client = new HttpClient(new Handler(Package(), url, digest, prerelease));
        var service = new PenguLoaderDownloadService(client, _root);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());
        Assert.Null(service.GetInstalled());
        if (Directory.Exists(_root)) Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task TraversalArchiveDoesNotWriteOutsideStagingOrLeaveInstallation()
    {
        using var client = new HttpClient(new Handler(Package("../../escaped.txt"), AssetUrl, null, false));
        var service = new PenguLoaderDownloadService(client, _root);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());
        Assert.Null(service.GetInstalled());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task CanceledDownloadNeverCreatesInstallation()
    {
        using var client = new HttpClient(new Handler(Package(), AssetUrl, null, false));
        var service = new PenguLoaderDownloadService(client, _root);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(cancellationToken: source.Token));
        Assert.Null(service.GetInstalled());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
