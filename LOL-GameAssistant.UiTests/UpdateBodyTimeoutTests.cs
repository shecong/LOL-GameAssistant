using System.Net;
using System.Text.Json;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class UpdateBodyTimeoutTests
{
    [Fact]
    public async Task StalledIndexBodyFallsBackAndLaterCheckCanRecover()
    {
        using var folder = new Folder();
        string config = Path.Combine(folder.Path, "source.json");
        File.WriteAllText(config, "{\"serverBaseUrl\":\"https://updates.example.com/\"}");
        var stalled = new StalledStream();
        bool stall = true;
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = stall ? new StreamContent(stalled) : new StringContent("{\"schemaVersion\":1,\"version\":\"99.0.0\"}")
        })) { Timeout = TimeSpan.FromMilliseconds(30) };
        var fallback = new GithubStub();
        var service = new ServerUpdateReleaseService(client, fallback, config, TimeSpan.FromMilliseconds(200));
        var release = await service.GetLatestAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stalled.Started.Task.IsCompleted);
        Assert.True(stalled.Disposed);
        Assert.Equal(1, fallback.Calls);
        Assert.Equal(new Version(98, 0, 0), release!.Version);
        Assert.False(release.PreferServerDownload);
        stall = false;
        Assert.Equal(new Version(99, 0, 0), (await service.GetLatestAsync())!.Version);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task UserCancellationDuringIndexBodyDoesNotStartFallback()
    {
        using var folder = new Folder();
        string config = Path.Combine(folder.Path, "source.json");
        File.WriteAllText(config, "{\"serverBaseUrl\":\"https://updates.example.com/\"}");
        var stalled = new StalledStream();
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stalled) }));
        var fallback = new GithubStub();
        var service = new ServerUpdateReleaseService(client, fallback, config);
        using var cancel = new CancellationTokenSource();
        var request = service.GetLatestAsync(cancel.Token);
        await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, fallback.Calls);
        Assert.True(stalled.Disposed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PackagePreparationBoundsBodyReadsAndCleansPartialDownloads(bool stallPackage, bool userCancels)
    {
        using var folder = new Folder();
        File.WriteAllText(Path.Combine(folder.Path, "Updater.exe"), "placeholder");
        var stalled = new StalledStream(stallPackage);
        var manifest = new UpdateManifest(1, "65534.0.0", UpdatePlatform.Current,
            UpdatePlatform.Package(UpdatePlatform.Current), 2, new string('0', 64), "sc助手.exe");
        using var client = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        {
            Content = stallPackage && request.RequestUri!.AbsolutePath.EndsWith(".json")
                ? new StringContent(JsonSerializer.Serialize(manifest)) : new StreamContent(stalled)
        })) { Timeout = TimeSpan.FromMilliseconds(30) };
        var service = new UpdatePackageService(client, folder.Path,
            userCancels ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(200));
        var release = new UpdateRelease(new Version(65534, 0, 0), "v65534.0.0", "https://github.com/releases", "");
        using var cancel = new CancellationTokenSource();
        var request = service.PrepareAsync(release, null, cancel.Token);
        await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (userCancels)
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("更新准备超时", error.Message);
        }
        Assert.True(stalled.Disposed);
        string cache = Path.Combine(folder.Path, ".update");
        Assert.True(!Directory.Exists(cache) || !Directory.EnumerateFileSystemEntries(cache).Any());
        Assert.Equal("placeholder", File.ReadAllText(Path.Combine(folder.Path, "Updater.exe")));
    }

    private sealed class StalledStream(bool prefix = false) : Stream
    {
        private bool _sent;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (prefix && !_sent) { _sent = true; buffer.Span[0] = 1; return 1; }
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class GithubStub : IUpdateReleaseService
    {
        public int Calls { get; private set; }
        public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult<UpdateRelease?>(new(new Version(98, 0, 0), "v98.0.0", "https://github.com/releases", "")); }
    }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "update-body-tests-" + Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
