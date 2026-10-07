using System.Net;
using System.Text;
using LOL_GameApi.Controllers;
using LOL_GameApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LOL_GameApi.Tests;

public sealed class DataDragonServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return send(token);
        }
    }

    private static HttpResponseMessage Good() => new(HttpStatusCode.OK) { Content = new StringContent("[\"16.20.1\"]") };
    private sealed class NonSeekableStream(byte[] bytes, bool block = false) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public long BytesRead => _inner.Position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (block) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return await _inner.ReadAsync(buffer, token);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    private static DataDragonService Service(Handler handler, Clock clock, DataDragonOptions? options = null) =>
        new(new HttpClient(handler), NullLogger<DataDragonService>.Instance, clock, Options.Create(options ?? new()));

    [Fact]
    public async Task ConcurrentCallersShareOneRefreshAndCancellationDoesNotCancelOthers()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async token => { entered.SetResult(); await release.Task.WaitAsync(token); return Good(); });
        var service = Service(handler, new());
        using var cancelled = new CancellationTokenSource();
        var first = service.GetLatestVersionAsync(cancelled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var others = Enumerable.Range(0, 50).Select(_ => service.GetLatestVersionAsync(default)).ToArray();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.All(await Task.WhenAll(others).WaitAsync(TimeSpan.FromSeconds(3)), value => Assert.Equal("16.20.1", value));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task FailuresUseCappedExponentialCooldownAndRecover()
    {
        bool fail = true;
        var handler = new Handler(_ => Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Good()));
        var clock = new Clock();
        var service = Service(handler, clock, new() { FailureCooldown = TimeSpan.FromSeconds(10), MaxFailureCooldown = TimeSpan.FromSeconds(20) });
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(clock.Now.AddSeconds(10), service.GetStatus().NextRetry);
        for (int i = 0; i < 50; i++) Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(1, handler.Calls);
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(clock.Now.AddSeconds(20), service.GetStatus().NextRetry);
        clock.Now = clock.Now.AddSeconds(20);
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(clock.Now.AddSeconds(20), service.GetStatus().NextRetry);
        fail = false;
        clock.Now = clock.Now.AddSeconds(20);
        Assert.Equal("16.20.1", await service.GetLatestVersionAsync(default));
        Assert.Equal(0, service.GetStatus().ConsecutiveFailures);
        Assert.Null(service.GetStatus().NextRetry);
    }

    [Fact]
    public async Task ExpiredCacheSurvivesUpstreamFailureAndReadinessReportsDegradation()
    {
        bool fail = false;
        var handler = new Handler(_ => Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Good()));
        var clock = new Clock();
        var service = Service(handler, clock);
        Assert.Equal("16.20.1", await service.GetLatestVersionAsync(default));
        Assert.Equal("16.20.1", await service.GetLatestVersionAsync(default));
        Assert.Equal(1, handler.Calls);
        fail = true;
        clock.Now = clock.Now.AddHours(7);
        Assert.Equal("16.20.1", await service.GetLatestVersionAsync(default));
        Assert.True(service.GetStatus().IsStale);
        var result = Assert.IsType<ObjectResult>(await new HealthController().Ready(service, default));
        Assert.Equal(200, result.StatusCode);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.True(json.GetProperty("degraded").GetBoolean());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task HangingUpstreamTimesOutAndEntersCooldown()
    {
        var handler = new Handler(async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Good(); });
        var service = Service(handler, new(), new() { RequestTimeout = TimeSpan.FromMilliseconds(100) });
        Assert.Null(await service.GetLatestVersionAsync(default).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, service.GetStatus().ConsecutiveFailures);
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("[\"invalid\"]")]
    public async Task InvalidPayloadIsAFailureWithCooldown(string payload)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) }));
        var service = Service(handler, new());
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(1, service.GetStatus().ConsecutiveFailures);
    }

    [Fact]
    public async Task UnknownLengthOversizedPayloadIsRejectedDuringReading()
    {
        using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(new string('x', 10000)));
        var content = new StreamContent(stream);
        Assert.Null(content.Headers.ContentLength);
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var service = Service(handler, new(), new() { MaxResponseBytes = 1024 });
        Assert.Null(await service.GetLatestVersionAsync(default));
        Assert.Equal(1, service.GetStatus().ConsecutiveFailures);
    }

    [Fact]
    public async Task TimeoutAlsoCoversHangingResponseBody()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new NonSeekableStream([], block: true)) }));
        var service = Service(handler, new(), new() { RequestTimeout = TimeSpan.FromMilliseconds(100) });
        Assert.Null(await service.GetLatestVersionAsync(default).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, service.GetStatus().ConsecutiveFailures);
    }

    [Fact]
    public async Task ColdReadinessFailsWhenNoUsableVersionExists()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var service = Service(handler, new());
        var result = Assert.IsType<ObjectResult>(await new HealthController().Ready(service, default));
        Assert.Equal(503, result.StatusCode);
    }
}
