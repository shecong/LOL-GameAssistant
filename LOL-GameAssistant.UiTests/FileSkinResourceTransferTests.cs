using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class FileSkinResourceTransferTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private sealed class InterruptedStream(byte[] prefix, bool stall = false) : MemoryStream(prefix)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, token);
            if (stall) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new IOException("connection interrupted");
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "skin-transfer-" + Guid.NewGuid().ToString("N"));
        internal byte[] Bytes = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
        internal string File => Path.Combine(Root, "package.download");
        internal FileSkinResourceRelease Release() => new("26.1115", "16.22", Convert.ToHexString(SHA256.HashData(Bytes)), ["https://server.test/skin.zip"], Bytes.Length);
        internal Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
    private static HttpResponseMessage Partial(byte[] bytes, int offset, int total) => new(HttpStatusCode.PartialContent)
    {
        Content = new ByteArrayContent(bytes) { Headers = { ContentRange = new ContentRangeHeaderValue(offset, offset + bytes.Length - 1, total) } }
    };

    [Fact]
    public async Task InterruptedDownloadResumesAcrossAttemptsAndServiceRestarts()
    {
        using var fixture = new Fixture(); int calls = 0;
        using (var http = new HttpClient(new Handler(request =>
        {
            if (++calls > 1) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(fixture.Bytes[..700])) { Headers = { ContentLength = fixture.Bytes.Length } } };
        })))
            await Assert.ThrowsAsync<IOException>(() => new FileSkinResourceTransfer(http, false, _ => { }).DownloadAsync(fixture.Release(), fixture.File, false, default));
        Assert.Equal(700, new FileInfo(fixture.File).Length);
        using var resumed = new HttpClient(new Handler(request =>
        {
            Assert.Equal(700, request.Headers.Range!.Ranges.Single().From);
            return Partial(fixture.Bytes[700..], 700, fixture.Bytes.Length);
        }));
        await new FileSkinResourceTransfer(resumed, false, _ => { }).DownloadAsync(fixture.Release(), fixture.File, false, default);
        Assert.Equal(fixture.Bytes, await System.IO.File.ReadAllBytesAsync(fixture.File));
    }
    [Fact]
    public async Task ServerIgnoringRangeReplacesPartialInsteadOfAppending()
    {
        using var fixture = new Fixture(); await System.IO.File.WriteAllBytesAsync(fixture.File, fixture.Bytes[..700]);
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(700, request.Headers.Range!.Ranges.Single().From);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) };
        }));
        await new FileSkinResourceTransfer(http, false, _ => { }).DownloadAsync(fixture.Release(), fixture.File, false, default);
        Assert.Equal(fixture.Bytes.Length, new FileInfo(fixture.File).Length);
    }
    [Fact]
    public async Task WrongRangeOrCorruptPackageCannotBeAccepted()
    {
        using var fixture = new Fixture(); await System.IO.File.WriteAllBytesAsync(fixture.File, fixture.Bytes[..700]);
        using var http = new HttpClient(new Handler(_ => Partial(fixture.Bytes[700..], 699, fixture.Bytes.Length)));
        await Assert.ThrowsAsync<IOException>(() => new FileSkinResourceTransfer(http, false, _ => { }).DownloadAsync(fixture.Release(), fixture.File, false, default));
        Assert.False(System.IO.File.Exists(fixture.File));
    }
    [Fact]
    public async Task StalledBodyTimesOutAndFallsBackWithRetainedRange()
    {
        using var fixture = new Fixture(); int stalls = 0;
        var release = fixture.Release() with { Urls = ["https://server.test/skin.zip", "https://github.com/skin.zip"] };
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "server.test")
            {
                stalls++;
                if (request.Headers.Range != null) return new(HttpStatusCode.ServiceUnavailable);
                return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(fixture.Bytes[..700], true)) { Headers = { ContentLength = fixture.Bytes.Length } } };
            }
            Assert.Equal(700, request.Headers.Range!.Ranges.Single().From);
            return Partial(fixture.Bytes[700..], 700, fixture.Bytes.Length);
        }));
        await new FileSkinResourceTransfer(http, false, _ => { }, TimeSpan.FromMilliseconds(100)).DownloadAsync(release, fixture.File, false, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, stalls); Assert.Equal(fixture.Bytes, await System.IO.File.ReadAllBytesAsync(fixture.File));
    }
    [Fact]
    public async Task ExplicitCancelPreservesPartialForLaterResume()
    {
        using var fixture = new Fixture(); using var stop = new CancellationTokenSource();
        var updates = new List<FileSkinResourceDownloadProgress>();
        using var http = new HttpClient(new Handler(_ =>
        {
            stop.CancelAfter(100);
            return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(fixture.Bytes[..700], true)) { Headers = { ContentLength = fixture.Bytes.Length } } };
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileSkinResourceTransfer(http, false, _ => { }, reportProgress: updates.Add).DownloadAsync(fixture.Release(), fixture.File, false, stop.Token));
        Assert.Equal(700, new FileInfo(fixture.File).Length);
        Assert.Equal(700, updates.Last().Downloaded);
        Assert.Equal(fixture.Bytes.Length, updates.Last().Total);
        Assert.InRange(updates.Last().Percent!.Value, 34, 35);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProgressUsesManifestOrHttpSizeAndAlwaysReportsCompletion(bool manifestHasSize)
    {
        using var fixture = new Fixture();
        var updates = new List<FileSkinResourceDownloadProgress>();
        var release = fixture.Release() with { Size = manifestHasSize ? fixture.Bytes.Length : 0 };
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) }));
        await new FileSkinResourceTransfer(http, false, _ => { }, reportProgress: updates.Add).DownloadAsync(release, fixture.File, false, default);
        Assert.Contains(updates, p => p.Downloaded == 0 && p.Total == fixture.Bytes.Length);
        Assert.Equal(fixture.Bytes.Length, updates.Last().Downloaded);
        Assert.Equal(fixture.Bytes.Length, updates.Last().Total);
        Assert.Equal(100d, updates.Last().Percent);
        Assert.Equal("下载已完成", updates.Last().Stage);
        Assert.Contains("/", updates.Last().Description);
        Assert.Contains("MB", updates.Last().Description);
        Assert.Contains("100", updates.Last().Description);
    }
    [Fact]
    public async Task OversizedManifestAndArchiveAreRejectedBeforeInstallation()
    {
        using var fixture = new Fixture();
        using var manifest = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 70000)) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSkinResourceTransfer(manifest, false, _ => { }).ReadManifestAsync("https://server.test/index", default));
        using var archive = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) }));
        await Assert.ThrowsAsync<IOException>(() => new FileSkinResourceTransfer(archive, false, _ => { }).DownloadAsync(fixture.Release() with { Size = 100 }, fixture.File, false, default));
        Assert.False(System.IO.File.Exists(fixture.File));
    }

    [Fact]
    public async Task CompleteVerifiedArchiveNeedsNoNetworkAndInvalidRedirectIsRejected()
    {
        using var fixture = new Fixture(); await System.IO.File.WriteAllBytesAsync(fixture.File, fixture.Bytes);
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("network must not be used")));
        await new FileSkinResourceTransfer(http, false, _ => { }).DownloadAsync(fixture.Release(), fixture.File, false, default);
        using var redirect = new HttpClient(new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://server.test/manifest") } }));
        await Assert.ThrowsAsync<IOException>(() => new FileSkinResourceTransfer(redirect, true, _ => { }).ReadManifestAsync("https://server.test/manifest", default));
    }
}
